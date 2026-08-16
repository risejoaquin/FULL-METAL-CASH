using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using SolidPOS.PosServer.Application.Sync;
using SolidPOS.PosServer.Contracts.Sync;
using SolidPOS.PosServer.Infrastructure.PostgreSql;

namespace SolidPOS.PosServer.Infrastructure.Sync;

public sealed class PostgreSqlSyncPushRepository : ISyncPushRepository
{
    private readonly string _connectionString;

    public PostgreSqlSyncPushRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    }

    public async Task<IReadOnlyCollection<SyncPushEventResultResponse>> IngestAsync(
        Guid tenantId,
        Guid storeId,
        Guid terminalId,
        Guid batchId,
        IReadOnlyCollection<SyncPushEventEnvelope> events,
        CancellationToken cancellationToken)
    {
        List<SyncPushEventResultResponse> results = [];

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken, transaction);

        foreach (SyncPushEventEnvelope syncEvent in events)
        {
            bool inserted = await InsertEventAsync(connection, transaction, tenantId, storeId, terminalId, batchId, syncEvent, cancellationToken);
            results.Add(new SyncPushEventResultResponse(
                syncEvent.EventId,
                inserted ? "accepted" : "duplicate",
                inserted ? null : "event_already_received"));
        }

        await transaction.CommitAsync(cancellationToken);
        return results;
    }

    private static async Task<bool> InsertEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid storeId,
        Guid terminalId,
        Guid batchId,
        SyncPushEventEnvelope syncEvent,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH inserted AS (
              INSERT INTO pos.sync_inbox_events (
                tenant_id,
                terminal_id,
                store_id,
                batch_id,
                event_id,
                event_type,
                entity_type,
                entity_id,
                local_occurred_at,
                schema_version,
                sequence_number,
                payload_hash,
                payload,
                status
              )
              VALUES (
                @tenant_id,
                @terminal_id,
                @store_id,
                @batch_id,
                @event_id,
                @event_type,
                @entity_type,
                @entity_id,
                @local_occurred_at,
                @schema_version,
                @sequence_number,
                @payload_hash,
                CAST(@payload AS jsonb),
                'received'
              )
              ON CONFLICT (tenant_id, terminal_id, event_id) DO NOTHING
              RETURNING id
            )
            SELECT EXISTS (SELECT 1 FROM inserted);
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("terminal_id", terminalId);
        command.Parameters.AddWithValue("store_id", storeId);
        command.Parameters.AddWithValue("batch_id", batchId);
        command.Parameters.AddWithValue("event_id", syncEvent.EventId);
        command.Parameters.AddWithValue("event_type", syncEvent.EventType);
        command.Parameters.AddWithValue("entity_type", syncEvent.EntityType);

        NpgsqlParameter entityIdParameter = command.Parameters.Add("entity_id", NpgsqlDbType.Uuid);
        entityIdParameter.Value = syncEvent.EntityId.HasValue ? syncEvent.EntityId.Value : DBNull.Value;

        command.Parameters.AddWithValue("local_occurred_at", syncEvent.LocalOccurredAt);
        command.Parameters.AddWithValue("schema_version", syncEvent.SchemaVersion);
        command.Parameters.AddWithValue("sequence_number", syncEvent.SequenceNumber);
        command.Parameters.AddWithValue("payload_hash", syncEvent.PayloadHash);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, syncEvent.Payload.GetRawText());

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is true;
    }
}
