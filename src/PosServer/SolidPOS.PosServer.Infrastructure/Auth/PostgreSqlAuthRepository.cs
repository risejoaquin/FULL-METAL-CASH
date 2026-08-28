using Microsoft.Extensions.Configuration;
using Npgsql;
using SolidPOS.PosServer.Application.Auth;
using SolidPOS.PosServer.Infrastructure.PostgreSql;

namespace SolidPOS.PosServer.Infrastructure.Auth;

public sealed class PostgreSqlAuthRepository : IAuthRepository
{
    private readonly string _connectionString;

    public PostgreSqlAuthRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    }

    public async Task<AuthenticatedUser?> FindLoginUserAsync(string email, Guid? tenantId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
              u.id,
              u.tenant_id,
              u.email::text,
              u.full_name,
              u.status,
              u.password_hash,
              t.name,
              t.status,
              COALESCE(u.login_failed_count, 0),
              u.locked_until,
              COALESCE(u.password_reset_required, false)
            FROM pos.users u
            JOIN pos.tenants t ON t.id = u.tenant_id
            WHERE u.email = @email
              AND (@tenant_id IS NULL OR u.tenant_id = @tenant_id)
              AND u.deleted_at IS NULL
              AND t.deleted_at IS NULL
            ORDER BY u.created_at
            LIMIT 2;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        if (tenantId.HasValue)
        {
            await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId.Value, cancellationToken);
        }
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("email", email.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("tenant_id", tenantId.HasValue ? tenantId.Value : (object)DBNull.Value);

        List<AuthenticatedUser> users = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(new AuthenticatedUser(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
                reader.GetBoolean(10)));
        }

        return users.Count == 1 ? users[0] : null;
    }

    public async Task<IReadOnlyCollection<string>> GetRoleCodesAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT r.code
            FROM pos.user_roles ur
            JOIN pos.roles r ON r.id = ur.role_id AND r.tenant_id = ur.tenant_id
            WHERE ur.tenant_id = @tenant_id
              AND ur.user_id = @user_id
              AND r.deleted_at IS NULL
            ORDER BY r.code;
            """;

        return await ReadStringsAsync(sql, tenantId, userId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<string>> GetPermissionCodesAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DISTINCT rp.permission_code
            FROM pos.user_roles ur
            JOIN pos.role_permissions rp ON rp.role_id = ur.role_id AND rp.tenant_id = ur.tenant_id
            WHERE ur.tenant_id = @tenant_id
              AND ur.user_id = @user_id
            ORDER BY rp.permission_code;
            """;

        return await ReadStringsAsync(sql, tenantId, userId, cancellationToken);
    }

    public async Task StoreRefreshTokenAsync(Guid tenantId, Guid userId, string refreshTokenHash, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.refresh_tokens (tenant_id, user_id, token_hash, expires_at)
            VALUES (@tenant_id, @user_id, @token_hash, @expires_at);
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("token_hash", refreshTokenHash);
        command.Parameters.AddWithValue("expires_at", expiresAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordFailedLoginAsync(Guid tenantId, Guid userId, int maxFailedAttempts, DateTimeOffset lockedUntil, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pos.users
            SET
              login_failed_count = login_failed_count + 1,
              login_last_failed_at = now(),
              locked_until = CASE
                WHEN login_failed_count + 1 >= @max_failed_attempts THEN @locked_until
                ELSE locked_until
              END
            WHERE tenant_id = @tenant_id
              AND id = @user_id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("max_failed_attempts", maxFailedAttempts);
        command.Parameters.AddWithValue("locked_until", lockedUntil);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ResetLoginFailuresAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pos.users
            SET
              login_failed_count = 0,
              login_last_failed_at = NULL,
              locked_until = NULL
            WHERE tenant_id = @tenant_id
              AND id = @user_id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("user_id", userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AuthenticatedUser?> FindUserByRefreshTokenHashAsync(Guid? tenantId, string refreshTokenHash, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
              u.id,
              u.tenant_id,
              u.email::text,
              u.full_name,
              u.status,
              u.password_hash,
              t.name,
              t.status,
              COALESCE(u.login_failed_count, 0),
              u.locked_until,
              COALESCE(u.password_reset_required, false)
            FROM pos.refresh_tokens rt
            JOIN pos.users u ON u.id = rt.user_id AND u.tenant_id = rt.tenant_id
            JOIN pos.tenants t ON t.id = u.tenant_id
            WHERE rt.token_hash = @token_hash
              AND rt.revoked_at IS NULL
              AND rt.expires_at > now()
              AND u.deleted_at IS NULL
              AND t.deleted_at IS NULL
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        if (tenantId.HasValue)
        {
            await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId.Value, cancellationToken);
        }
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("token_hash", refreshTokenHash);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AuthenticatedUser(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt32(8),
            reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
            reader.GetBoolean(10));
    }

    public async Task<AuthenticatedUser?> RotateRefreshTokenAndFindUserAsync(Guid tenantId, string oldRefreshTokenHash, string newRefreshTokenHash, DateTimeOffset newExpiresAt, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH old_token AS (
              UPDATE pos.refresh_tokens rt
              SET revoked_at = now(),
                  rotated_at = now(),
                  last_used_at = now(),
                  revoked_reason = 'rotated'
              WHERE rt.tenant_id = @tenant_id
                AND rt.token_hash = @old_hash
                AND rt.revoked_at IS NULL
                AND rt.expires_at > now()
              RETURNING rt.id, rt.tenant_id, rt.user_id
            ),
            new_token AS (
              INSERT INTO pos.refresh_tokens (tenant_id, user_id, token_hash, expires_at)
              SELECT tenant_id, user_id, @new_hash, @new_expires_at
              FROM old_token
              RETURNING id
            ),
            replace_old AS (
              UPDATE pos.refresh_tokens rt
              SET replaced_by_token_id = (SELECT id FROM new_token)
              WHERE rt.id = (SELECT id FROM old_token)
              RETURNING 1
            )
            SELECT
              u.id,
              u.tenant_id,
              u.email::text,
              u.full_name,
              u.status,
              u.password_hash,
              t.name,
              t.status,
              COALESCE(u.login_failed_count, 0),
              u.locked_until,
              COALESCE(u.password_reset_required, false)
            FROM old_token ot
            JOIN pos.users u ON u.id = ot.user_id AND u.tenant_id = ot.tenant_id
            JOIN pos.tenants t ON t.id = u.tenant_id
            WHERE u.deleted_at IS NULL
              AND t.deleted_at IS NULL
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("old_hash", oldRefreshTokenHash);
        command.Parameters.AddWithValue("new_hash", newRefreshTokenHash);
        command.Parameters.AddWithValue("new_expires_at", newExpiresAt);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AuthenticatedUser(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt32(8),
            reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
            reader.GetBoolean(10));
    }

    public async Task<bool> RotateRefreshTokenAsync(Guid tenantId, string oldRefreshTokenHash, string newRefreshTokenHash, DateTimeOffset newExpiresAt, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH old_token AS (
              UPDATE pos.refresh_tokens
              SET revoked_at = now(),
                  rotated_at = now(),
                  last_used_at = now(),
                  revoked_reason = 'rotated'
              WHERE token_hash = @old_hash
                AND revoked_at IS NULL
                AND expires_at > now()
              RETURNING tenant_id, user_id
            ),
            new_token AS (
              INSERT INTO pos.refresh_tokens (tenant_id, user_id, token_hash, expires_at)
              SELECT tenant_id, user_id, @new_hash, @new_expires_at
              FROM old_token
              RETURNING id
            )
            UPDATE pos.refresh_tokens
            SET replaced_by_token_id = (SELECT id FROM new_token)
            WHERE token_hash = @old_hash;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("old_hash", oldRefreshTokenHash);
        command.Parameters.AddWithValue("new_hash", newRefreshTokenHash);
        command.Parameters.AddWithValue("new_expires_at", newExpiresAt);
        int affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        return affectedRows > 0;
    }

    public async Task RevokeRefreshTokenAsync(Guid? tenantId, string refreshTokenHash, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pos.refresh_tokens
            SET revoked_at = now(),
                revoked_reason = 'logout'
            WHERE token_hash = @token_hash
              AND revoked_at IS NULL;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        if (tenantId.HasValue)
        {
            await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId.Value, cancellationToken);
        }
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("token_hash", refreshTokenHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<IReadOnlyCollection<string>> ReadStringsAsync(string sql, Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("user_id", userId);

        List<string> values = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}
