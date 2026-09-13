using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;
using SolidPOS.PosServer.Application.Auth;
using SolidPOS.PosServer.Application.Terminals;
using SolidPOS.PosServer.Contracts.Terminals;
using SolidPOS.PosServer.Infrastructure.PostgreSql;

namespace SolidPOS.PosServer.Infrastructure.Terminals;

public sealed class PostgreSqlTerminalRepository : ITerminalRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString;

    public PostgreSqlTerminalRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    }

    public async Task<bool> StoreExistsAsync(Guid tenantId, Guid storeId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
              SELECT 1
              FROM pos.stores
              WHERE tenant_id = @tenant_id
                AND id = @store_id
                AND status = 'active'
                AND deleted_at IS NULL
            );
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("store_id", storeId);

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is true;
    }

    public async Task StoreEnrollmentTokenAsync(
        Guid tenantId,
        Guid storeId,
        string tokenHash,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.enrollment_tokens (tenant_id, store_id, token_hash, purpose, expires_at)
            VALUES (@tenant_id, @store_id, @token_hash, 'terminal_register', @expires_at);
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("store_id", storeId);
        command.Parameters.AddWithValue("token_hash", tokenHash);
        command.Parameters.AddWithValue("expires_at", expiresAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AuthenticatedTerminal?> RegisterTerminalAsync(
        string enrollmentTokenHash,
        string name,
        string fingerprint,
        string? appVersion,
        CancellationToken cancellationToken)
    {
        const string tokenSql = """
            SELECT tenant_id, store_id
            FROM pos.enrollment_tokens
            WHERE token_hash = @token_hash
              AND purpose = 'terminal_register'
              AND used_at IS NULL
              AND revoked_at IS NULL
              AND expires_at > now()
            FOR UPDATE;
            """;

        const string upsertTerminalSql = """
            INSERT INTO pos.terminals (
              tenant_id,
              store_id,
              name,
              fingerprint,
              status,
              app_version,
              last_seen_at,
              hard_locked_at,
              hard_lock_reason
            )
            VALUES (
              @tenant_id,
              @store_id,
              @name,
              @fingerprint,
              'active',
              @app_version,
              now(),
              NULL,
              NULL
            )
            ON CONFLICT (tenant_id, fingerprint)
            DO UPDATE SET
              store_id = EXCLUDED.store_id,
              name = EXCLUDED.name,
              status = 'active',
              app_version = EXCLUDED.app_version,
              last_seen_at = now(),
              hard_locked_at = NULL,
              hard_lock_reason = NULL,
              updated_at = now()
            RETURNING id, tenant_id, store_id, name, status;
            """;

        const string consumeTokenSql = """
            UPDATE pos.enrollment_tokens
            SET used_at = now()
            WHERE token_hash = @token_hash;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var tokenCommand = new NpgsqlCommand(tokenSql, connection, transaction);
        tokenCommand.Parameters.AddWithValue("token_hash", enrollmentTokenHash);

        Guid tenantId;
        Guid storeId;
        await using (NpgsqlDataReader tokenReader = await tokenCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (!await tokenReader.ReadAsync(cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            tenantId = tokenReader.GetGuid(0);
            storeId = tokenReader.GetGuid(1);
        }

        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken, transaction);

        await using var terminalCommand = new NpgsqlCommand(upsertTerminalSql, connection, transaction);
        terminalCommand.Parameters.AddWithValue("tenant_id", tenantId);
        terminalCommand.Parameters.AddWithValue("store_id", storeId);
        terminalCommand.Parameters.AddWithValue("name", name);
        terminalCommand.Parameters.AddWithValue("fingerprint", fingerprint);
        terminalCommand.Parameters.AddWithValue("app_version", string.IsNullOrWhiteSpace(appVersion) ? (object)DBNull.Value : appVersion);

        AuthenticatedTerminal? terminal;
        await using (NpgsqlDataReader terminalReader = await terminalCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (!await terminalReader.ReadAsync(cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            terminal = new AuthenticatedTerminal(
                terminalReader.GetGuid(0),
                terminalReader.GetGuid(1),
                terminalReader.GetGuid(2),
                terminalReader.GetString(3),
                terminalReader.GetString(4));
        }

        await using var consumeCommand = new NpgsqlCommand(consumeTokenSql, connection, transaction);
        consumeCommand.Parameters.AddWithValue("token_hash", enrollmentTokenHash);
        await consumeCommand.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return terminal;
    }

    public async Task UpdateTerminalTokenHashAsync(Guid tenantId, Guid terminalId, string tokenHash, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pos.terminals
            SET device_token_hash = @token_hash,
                last_seen_at = now(),
                updated_at = now()
            WHERE tenant_id = @tenant_id
              AND id = @terminal_id
              AND deleted_at IS NULL;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("terminal_id", terminalId);
        command.Parameters.AddWithValue("token_hash", tokenHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<TerminalResponse>> ListTerminalsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, tenant_id, store_id, name, fingerprint, status, app_version, last_seen_at
            FROM pos.terminals
            WHERE tenant_id = @tenant_id
              AND deleted_at IS NULL
            ORDER BY created_at DESC;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);

        List<TerminalResponse> terminals = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            terminals.Add(ReadTerminalResponse(reader));
        }

        return terminals;
    }

    public async Task<bool> RevokeTerminalAsync(Guid tenantId, Guid terminalId, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pos.terminals
            SET status = 'blocked',
                hard_locked_at = now(),
                hard_lock_reason = 'revoked',
                device_token_hash = NULL,
                updated_at = now()
            WHERE tenant_id = @tenant_id
              AND id = @terminal_id
              AND deleted_at IS NULL
              AND status <> 'blocked';
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("terminal_id", terminalId);
        int affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);

        return affectedRows > 0;
    }

    public async Task<bool> IsTerminalActiveAsync(Guid tenantId, Guid terminalId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
              SELECT 1
              FROM pos.terminals
              WHERE tenant_id = @tenant_id
                AND id = @terminal_id
                AND status = 'active'
                AND hard_locked_at IS NULL
                AND deleted_at IS NULL
            );
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("terminal_id", terminalId);

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is true;
    }

    public async Task<TerminalDetailResponse?> GetTerminalAsync(Guid tenantId, Guid terminalId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, tenant_id, store_id, name, fingerprint, status, app_version, last_seen_at,
                   hard_locked_at, hard_lock_reason, device_health, remote_config_metadata
            FROM pos.terminals
            WHERE tenant_id = @tenant_id
              AND id = @terminal_id
              AND deleted_at IS NULL;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("terminal_id", terminalId);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadTerminalDetailResponse(reader);
    }

    public async Task<bool> AssignTerminalStoreAsync(Guid tenantId, Guid terminalId, Guid storeId, CancellationToken cancellationToken)
    {
        if (!await StoreExistsAsync(tenantId, storeId, cancellationToken))
        {
            return false;
        }

        const string sql = """
            UPDATE pos.terminals
            SET store_id = @store_id,
                updated_at = now()
            WHERE tenant_id = @tenant_id
              AND id = @terminal_id
              AND deleted_at IS NULL;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("terminal_id", terminalId);
        command.Parameters.AddWithValue("store_id", storeId);

        int affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        return affectedRows > 0;
    }

    public async Task<bool> DisableTerminalAsync(Guid tenantId, Guid terminalId, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pos.terminals
            SET status = 'blocked',
                hard_locked_at = now(),
                hard_lock_reason = 'disabled',
                updated_at = now()
            WHERE tenant_id = @tenant_id
              AND id = @terminal_id
              AND deleted_at IS NULL
              AND (hard_lock_reason IS NULL OR hard_lock_reason <> 'revoked');
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("terminal_id", terminalId);

        int affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        return affectedRows > 0;
    }

    public async Task<bool> EnableTerminalAsync(Guid tenantId, Guid terminalId, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pos.terminals
            SET status = 'active',
                hard_locked_at = NULL,
                hard_lock_reason = NULL,
                updated_at = now()
            WHERE tenant_id = @tenant_id
              AND id = @terminal_id
              AND deleted_at IS NULL
              AND (hard_lock_reason IS NULL OR hard_lock_reason <> 'revoked');
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("terminal_id", terminalId);

        int affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        return affectedRows > 0;
    }

    public async Task<TerminalHeartbeatResponse?> RecordHeartbeatAsync(
        Guid tenantId,
        Guid terminalId,
        string? appVersion,
        int? localDbVersion,
        string? lastSyncCursor,
        string? deviceHealthJson,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pos.terminals
            SET last_seen_at = now(),
                app_version = CASE WHEN @app_version IS NOT NULL THEN @app_version ELSE app_version END,
                local_db_version = CASE WHEN @local_db_version IS NOT NULL THEN @local_db_version ELSE local_db_version END,
                last_sync_cursor = CASE WHEN @last_sync_cursor IS NOT NULL THEN @last_sync_cursor ELSE last_sync_cursor END,
                device_health = CASE WHEN @device_health IS NOT NULL THEN @device_health::jsonb ELSE device_health END,
                updated_at = now()
            WHERE tenant_id = @tenant_id
              AND id = @terminal_id
              AND deleted_at IS NULL
            RETURNING id, tenant_id, store_id, status, last_seen_at, remote_config_metadata;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("terminal_id", terminalId);
        command.Parameters.AddWithValue("app_version", string.IsNullOrWhiteSpace(appVersion) ? (object)DBNull.Value : appVersion.Trim());
        command.Parameters.AddWithValue("local_db_version", localDbVersion.HasValue ? (object)localDbVersion.Value : DBNull.Value);
        command.Parameters.AddWithValue("last_sync_cursor", string.IsNullOrWhiteSpace(lastSyncCursor) ? (object)DBNull.Value : lastSyncCursor.Trim());
        command.Parameters.AddWithValue("device_health", string.IsNullOrWhiteSpace(deviceHealthJson) ? (object)DBNull.Value : deviceHealthJson);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        Guid readTerminalId = reader.GetGuid(0);
        Guid readTenantId = reader.GetGuid(1);
        Guid readStoreId = reader.GetGuid(2);
        string readStatus = reader.GetString(3);
        DateTimeOffset readLastSeenAt = reader.GetFieldValue<DateTimeOffset>(4);

        TerminalRemoteConfigMetadata remoteConfig = new();
        if (!reader.IsDBNull(5))
        {
            string json = reader.GetString(5);
            remoteConfig = JsonSerializer.Deserialize<TerminalRemoteConfigMetadata>(json, JsonOptions) ?? new();
        }

        return new TerminalHeartbeatResponse(
            readTerminalId,
            readTenantId,
            readStoreId,
            readStatus,
            readLastSeenAt,
            remoteConfig);
    }

    public async Task<TerminalDeviceHealthDto?> GetDeviceHealthAsync(Guid tenantId, Guid terminalId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT device_health
            FROM pos.terminals
            WHERE tenant_id = @tenant_id
              AND id = @terminal_id
              AND deleted_at IS NULL;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("terminal_id", terminalId);

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null || result is DBNull)
        {
            return null;
        }

        string json = result is string str ? str : result.ToString()!;
        return JsonSerializer.Deserialize<TerminalDeviceHealthDto>(json, JsonOptions);
    }

    public async Task<TerminalRemoteConfigMetadata?> GetRemoteConfigAsync(Guid tenantId, Guid terminalId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT remote_config_metadata
            FROM pos.terminals
            WHERE tenant_id = @tenant_id
              AND id = @terminal_id
              AND deleted_at IS NULL;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("terminal_id", terminalId);

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null || result is DBNull)
        {
            return new TerminalRemoteConfigMetadata();
        }

        string json = result is string str ? str : result.ToString()!;
        return JsonSerializer.Deserialize<TerminalRemoteConfigMetadata>(json, JsonOptions) ?? new TerminalRemoteConfigMetadata();
    }

    public async Task<bool> UpdateRemoteConfigAsync(Guid tenantId, Guid terminalId, string remoteConfigJson, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pos.terminals
            SET remote_config_metadata = @remote_config_metadata::jsonb,
                updated_at = now()
            WHERE tenant_id = @tenant_id
              AND id = @terminal_id
              AND deleted_at IS NULL;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("terminal_id", terminalId);
        command.Parameters.AddWithValue("remote_config_metadata", remoteConfigJson);

        int affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        return affectedRows > 0;
    }

    private static TerminalResponse ReadTerminalResponse(NpgsqlDataReader reader)
    {
        return new TerminalResponse(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7));
    }

    private static TerminalDetailResponse ReadTerminalDetailResponse(NpgsqlDataReader reader)
    {
        TerminalDeviceHealthDto? deviceHealth = null;
        if (!reader.IsDBNull(10))
        {
            string healthJson = reader.GetString(10);
            deviceHealth = JsonSerializer.Deserialize<TerminalDeviceHealthDto>(healthJson, JsonOptions);
        }

        TerminalRemoteConfigMetadata? remoteConfig = null;
        if (!reader.IsDBNull(11))
        {
            string configJson = reader.GetString(11);
            remoteConfig = JsonSerializer.Deserialize<TerminalRemoteConfigMetadata>(configJson, JsonOptions);
        }

        return new TerminalDetailResponse(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
            reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            deviceHealth,
            remoteConfig ?? new TerminalRemoteConfigMetadata());
    }
}
