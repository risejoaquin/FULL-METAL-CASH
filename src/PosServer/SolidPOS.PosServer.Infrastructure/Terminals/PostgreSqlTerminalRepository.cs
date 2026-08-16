using Microsoft.Extensions.Configuration;
using Npgsql;
using SolidPOS.PosServer.Application.Auth;
using SolidPOS.PosServer.Application.Terminals;
using SolidPOS.PosServer.Contracts.Terminals;
using SolidPOS.PosServer.Infrastructure.PostgreSql;

namespace SolidPOS.PosServer.Infrastructure.Terminals;

public sealed class PostgreSqlTerminalRepository : ITerminalRepository
{
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
}
