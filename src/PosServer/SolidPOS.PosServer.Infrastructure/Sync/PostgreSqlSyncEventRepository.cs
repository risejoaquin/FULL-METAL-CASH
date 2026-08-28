using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using SolidPOS.PosServer.Application.Sync;
using SolidPOS.PosServer.Infrastructure.PostgreSql;

namespace SolidPOS.PosServer.Infrastructure.Sync;

public sealed class PostgreSqlSyncEventRepository : ISyncEventRepository
{
    private readonly string _connectionString;

    public PostgreSqlSyncEventRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    }

    public async Task<IReadOnlyCollection<SyncInboxEvent>> ReadPendingAsync(
        Guid tenantId,
        Guid storeId,
        Guid terminalId,
        Guid? batchId,
        int maxEvents,
        CancellationToken cancellationToken)
    {
        const string recoverSql = """
            UPDATE pos.sync_inbox_events
            SET status = 'retry_pending',
                next_retry_at = now(),
                error_code = COALESCE(error_code, 'stuck_processing_recovered'),
                error_message = COALESCE(error_message, 'Processing lease expired and event was returned to retry queue.')
            WHERE tenant_id = @tenant_id
              AND store_id = @store_id
              AND terminal_id = @terminal_id
              AND status = 'processing'
              AND last_attempt_at < now() - interval '15 minutes';
            """;

        const string sql = """
            WITH claimed AS (
              SELECT id
              FROM pos.sync_inbox_events
              WHERE tenant_id = @tenant_id
                AND store_id = @store_id
                AND terminal_id = @terminal_id
                AND status IN ('received', 'retry_pending')
                AND (next_retry_at IS NULL OR next_retry_at <= now())
                AND (@batch_id IS NULL OR batch_id = @batch_id)
              ORDER BY created_at, sequence_number NULLS LAST, id
              LIMIT @max_events
              FOR UPDATE SKIP LOCKED
            )
            UPDATE pos.sync_inbox_events inbox
            SET status = 'processing',
                attempts = inbox.attempts + 1,
                last_attempt_at = now(),
                next_retry_at = NULL
            FROM claimed
            WHERE inbox.id = claimed.id
            RETURNING inbox.id, inbox.tenant_id, inbox.store_id, inbox.terminal_id, inbox.batch_id,
                      inbox.event_id, inbox.event_type, inbox.entity_type, inbox.entity_id,
                      inbox.local_occurred_at, inbox.schema_version, inbox.payload::text;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken, transaction);

        await using (var recoverCommand = new NpgsqlCommand(recoverSql, connection, transaction))
        {
            recoverCommand.Parameters.AddWithValue("tenant_id", tenantId);
            recoverCommand.Parameters.AddWithValue("store_id", storeId);
            recoverCommand.Parameters.AddWithValue("terminal_id", terminalId);
            await recoverCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("store_id", storeId);
        command.Parameters.AddWithValue("terminal_id", terminalId);

        NpgsqlParameter batchIdParameter = command.Parameters.Add("batch_id", NpgsqlDbType.Uuid);
        batchIdParameter.Value = batchId.HasValue ? batchId.Value : (object)DBNull.Value;

        command.Parameters.AddWithValue("max_events", maxEvents);

        List<SyncInboxEvent> events = [];
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                events.Add(new SyncInboxEvent(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetGuid(2),
                    reader.GetGuid(3),
                    reader.IsDBNull(4) ? null : reader.GetGuid(4),
                    reader.GetGuid(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetGuid(8),
                    reader.GetFieldValue<DateTimeOffset>(9),
                    reader.GetInt32(10),
                    JsonDocument.Parse(reader.GetString(11)).RootElement.Clone()));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return events;
    }

    public async Task MarkProcessedAsync(Guid tenantId, Guid inboxEventId, JsonElement result, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pos.sync_inbox_events
            SET status = 'processed',
                result = CAST(@result AS jsonb),
                error_code = NULL,
                error_message = NULL,
                next_retry_at = NULL,
                processed_at = now()
            WHERE tenant_id = @tenant_id
              AND id = @inbox_event_id
              AND status = 'processing';
            """;

        await ExecuteStatusUpdateAsync(tenantId, inboxEventId, sql, result.GetRawText(), null, null, null, cancellationToken);
    }

    public async Task MarkRejectedAsync(Guid tenantId, Guid inboxEventId, string errorCode, string errorMessage, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pos.sync_inbox_events
            SET status = CASE WHEN attempts >= max_attempts THEN 'dead_letter' ELSE 'retry_pending' END,
                result = NULL,
                error_code = @error_code,
                error_message = @error_message,
                next_retry_at = CASE WHEN attempts >= max_attempts THEN NULL ELSE now() + ((LEAST(300, attempts * 30)::text || ' seconds')::interval) END,
                dead_lettered_at = CASE WHEN attempts >= max_attempts THEN now() ELSE dead_lettered_at END,
                processed_at = CASE WHEN attempts >= max_attempts THEN now() ELSE processed_at END
            WHERE tenant_id = @tenant_id
              AND id = @inbox_event_id
              AND status = 'processing';
            """;

        await ExecuteStatusUpdateAsync(tenantId, inboxEventId, sql, null, errorCode, errorMessage, null, cancellationToken);
    }

    public async Task MarkConflictAsync(Guid tenantId, Guid inboxEventId, Guid conflictId, string errorCode, string errorMessage, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pos.sync_inbox_events
            SET status = 'conflict',
                result = jsonb_build_object('conflictId', @conflict_id),
                error_code = @error_code,
                error_message = @error_message,
                conflict_id = @conflict_id,
                next_retry_at = NULL,
                processed_at = now()
            WHERE tenant_id = @tenant_id
              AND id = @inbox_event_id
              AND status = 'processing';
            """;

        await ExecuteStatusUpdateAsync(tenantId, inboxEventId, sql, null, errorCode, errorMessage, conflictId, cancellationToken);
    }

    private async Task ExecuteStatusUpdateAsync(
        Guid tenantId,
        Guid inboxEventId,
        string sql,
        string? result,
        string? errorCode,
        string? errorMessage,
        Guid? conflictId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("inbox_event_id", inboxEventId);

        if (result is not null)
        {
            command.Parameters.AddWithValue("result", NpgsqlDbType.Jsonb, result);
        }

        if (errorCode is not null)
        {
            command.Parameters.AddWithValue("error_code", errorCode);
            command.Parameters.AddWithValue("error_message", errorMessage ?? string.Empty);
        }

        if (conflictId.HasValue)
        {
            command.Parameters.AddWithValue("conflict_id", conflictId.Value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
