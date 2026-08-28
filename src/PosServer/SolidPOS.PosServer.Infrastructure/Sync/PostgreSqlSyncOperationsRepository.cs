using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using SolidPOS.PosServer.Application.Sync;
using SolidPOS.PosServer.Contracts.Sync;
using SolidPOS.PosServer.Infrastructure.PostgreSql;

namespace SolidPOS.PosServer.Infrastructure.Sync;

public sealed class PostgreSqlSyncOperationsRepository : ISyncOperationsRepository
{
    private readonly string _connectionString;

    public PostgreSqlSyncOperationsRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    }

    public async Task<SyncRuntimeStatusResponse> GetStatusAsync(
        Guid tenantId,
        Guid? storeId,
        Guid? terminalId,
        DateTimeOffset serverTime,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
              status,
              count(*)::int AS status_count,
              min(CASE WHEN status IN ('received', 'retry_pending') THEN created_at ELSE NULL END) AS oldest_pending_at,
              max(processed_at) AS last_processed_at
            FROM pos.sync_inbox_events
            WHERE tenant_id = @tenant_id
              AND (@store_id IS NULL OR store_id = @store_id)
              AND (@terminal_id IS NULL OR terminal_id = @terminal_id)
            GROUP BY status
            ORDER BY status;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        NpgsqlParameter storeIdParameter = command.Parameters.Add("store_id", NpgsqlDbType.Uuid);
        storeIdParameter.Value = storeId.HasValue ? storeId.Value : DBNull.Value;
        NpgsqlParameter terminalIdParameter = command.Parameters.Add("terminal_id", NpgsqlDbType.Uuid);
        terminalIdParameter.Value = terminalId.HasValue ? terminalId.Value : DBNull.Value;

        List<SyncStatusBucketResponse> buckets = [];
        DateTimeOffset? oldestPendingAt = null;
        DateTimeOffset? lastProcessedAt = null;

        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                string status = reader.GetString(0);
                int count = reader.GetInt32(1);
                buckets.Add(new SyncStatusBucketResponse(status, count));

                if (!reader.IsDBNull(2))
                {
                    DateTimeOffset candidate = reader.GetFieldValue<DateTimeOffset>(2);
                    oldestPendingAt = oldestPendingAt is null || candidate < oldestPendingAt.Value ? candidate : oldestPendingAt;
                }

                if (!reader.IsDBNull(3))
                {
                    DateTimeOffset candidate = reader.GetFieldValue<DateTimeOffset>(3);
                    lastProcessedAt = lastProcessedAt is null || candidate > lastProcessedAt.Value ? candidate : lastProcessedAt;
                }
            }
        }

        int Count(string status) => buckets.FirstOrDefault(x => x.Status == status)?.Count ?? 0;

        return new SyncRuntimeStatusResponse(
            tenantId,
            storeId,
            terminalId,
            serverTime,
            buckets.Sum(x => x.Count),
            Count("received"),
            Count("processing"),
            Count("processed"),
            Count("duplicate"),
            Count("rejected"),
            Count("retry_pending"),
            Count("conflict"),
            Count("dead_letter"),
            oldestPendingAt,
            lastProcessedAt,
            buckets);
    }

    public async Task<IReadOnlyCollection<SyncDeadLetterEventResponse>> ListDeadLetterAsync(
        Guid tenantId,
        Guid? terminalId,
        int limit,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, event_id, tenant_id, store_id, terminal_id, batch_id,
                   event_type, entity_type, entity_id, schema_version, attempts, max_attempts,
                   error_code, error_message, local_occurred_at, created_at, last_attempt_at,
                   dead_lettered_at, payload::text
            FROM pos.sync_inbox_events
            WHERE tenant_id = @tenant_id
              AND status = 'dead_letter'
              AND (@terminal_id IS NULL OR terminal_id = @terminal_id)
            ORDER BY dead_lettered_at DESC NULLS LAST, created_at DESC
            LIMIT @limit;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        NpgsqlParameter terminalIdParameter = command.Parameters.Add("terminal_id", NpgsqlDbType.Uuid);
        terminalIdParameter.Value = terminalId.HasValue ? terminalId.Value : DBNull.Value;
        command.Parameters.AddWithValue("limit", limit);

        List<SyncDeadLetterEventResponse> events = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new SyncDeadLetterEventResponse(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetGuid(8),
                reader.GetInt32(9),
                reader.GetInt32(10),
                reader.GetInt32(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.GetFieldValue<DateTimeOffset>(14),
                reader.GetFieldValue<DateTimeOffset>(15),
                reader.IsDBNull(16) ? null : reader.GetFieldValue<DateTimeOffset>(16),
                reader.IsDBNull(17) ? null : reader.GetFieldValue<DateTimeOffset>(17),
                JsonDocument.Parse(reader.GetString(18)).RootElement.Clone()));
        }

        return events;
    }

    public async Task<RetrySyncDeadLetterResponse?> RetryDeadLetterAsync(
        Guid tenantId,
        Guid inboxEventId,
        string reason,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pos.sync_inbox_events
            SET status = 'retry_pending',
                attempts = 0,
                next_retry_at = now(),
                dead_lettered_at = NULL,
                error_code = 'manual_retry_requested',
                error_message = @reason,
                replayed_at = now(),
                replay_reason = @reason,
                processed_at = NULL
            WHERE tenant_id = @tenant_id
              AND id = @inbox_event_id
              AND status = 'dead_letter'
            RETURNING id, event_id, status, attempts, next_retry_at;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("inbox_event_id", inboxEventId);
        command.Parameters.AddWithValue("reason", reason);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new RetrySyncDeadLetterResponse(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
            "Dead-letter event scheduled for retry.");
    }
}
