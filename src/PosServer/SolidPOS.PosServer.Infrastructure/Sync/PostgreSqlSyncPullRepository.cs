using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using SolidPOS.PosServer.Application.Sync;
using SolidPOS.PosServer.Contracts.Sync;
using SolidPOS.PosServer.Infrastructure.PostgreSql;

namespace SolidPOS.PosServer.Infrastructure.Sync;

public sealed class PostgreSqlSyncPullRepository : ISyncPullRepository
{
    private readonly string _connectionString;

    public PostgreSqlSyncPullRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    }

    public async Task<IReadOnlyCollection<SyncPullChangeResponse>> ReadChangesAsync(
        Guid tenantId,
        Guid storeId,
        Guid terminalId,
        DateTimeOffset? changedAfter,
        int limit,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, entity_type, entity_id, operation, entity_version, changed_at,
                   payload::text, store_id, source_terminal_id
            FROM pos.sync_changes
            WHERE tenant_id = @tenant_id
              AND (@changed_after IS NULL OR changed_at > @changed_after)
              AND (store_id IS NULL OR store_id = @store_id)
              AND (source_terminal_id IS NULL OR source_terminal_id <> @terminal_id)
            ORDER BY changed_at, id
            LIMIT @limit;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("store_id", storeId);
        command.Parameters.AddWithValue("terminal_id", terminalId);
        command.Parameters.AddWithValue("limit", limit);

        NpgsqlParameter changedAfterParameter = command.Parameters.Add("changed_after", NpgsqlDbType.TimestampTz);
        changedAfterParameter.Value = changedAfter.HasValue ? changedAfter.Value : DBNull.Value;

        List<SyncPullChangeResponse> changes = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            changes.Add(new SyncPullChangeResponse(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetInt64(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                JsonDocument.Parse(reader.GetString(6)).RootElement.Clone(),
                reader.IsDBNull(7) ? null : reader.GetGuid(7),
                reader.IsDBNull(8) ? null : reader.GetGuid(8)));
        }

        return changes;
    }

    public async Task<JsonElement> ReadAccessSnapshotAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT jsonb_build_object(
              'users', COALESCE((
                SELECT jsonb_agg(jsonb_build_object(
                  'id', u.id,
                  'email', u.email::text,
                  'name', u.full_name,
                  'status', u.status,
                  'roles', COALESCE((
                    SELECT jsonb_agg(r.code ORDER BY r.code)
                    FROM pos.user_roles ur
                    JOIN pos.roles r ON r.tenant_id = ur.tenant_id AND r.id = ur.role_id
                    WHERE ur.tenant_id = u.tenant_id
                      AND ur.user_id = u.id
                  ), '[]'::jsonb),
                  'permissions', COALESCE((
                    SELECT jsonb_agg(DISTINCT p.code ORDER BY p.code)
                    FROM pos.user_roles ur
                    JOIN pos.role_permissions rp ON rp.tenant_id = ur.tenant_id AND rp.role_id = ur.role_id
                    JOIN pos.permissions p ON p.code = rp.permission_code
                    WHERE ur.tenant_id = u.tenant_id
                      AND ur.user_id = u.id
                  ), '[]'::jsonb)
                ) ORDER BY u.email)
                FROM pos.users u
                WHERE u.tenant_id = @tenant_id
                  AND u.deleted_at IS NULL
              ), '[]'::jsonb)
            )::text;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);

        string payload = (string)(await command.ExecuteScalarAsync(cancellationToken) ?? """{"users":[]}""");
        return JsonDocument.Parse(payload).RootElement.Clone();
    }
}
