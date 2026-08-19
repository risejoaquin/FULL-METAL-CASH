using Microsoft.Data.Sqlite;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Infrastructure.SQLite;

public sealed class SQLiteLocalPosRepository : ILocalPosRepository
{
    private readonly SQLiteLocalDatabase _database;

    public SQLiteLocalPosRepository(SQLiteLocalDatabase database)
    {
        _database = database;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        Execute(connection, """
CREATE TABLE IF NOT EXISTS terminal_binding (
  id INTEGER PRIMARY KEY CHECK (id = 1),
  tenant_id TEXT NOT NULL,
  store_id TEXT NOT NULL,
  terminal_id TEXT NOT NULL,
  terminal_fingerprint TEXT NOT NULL,
  terminal_token TEXT NOT NULL,
  bound_at_utc TEXT NOT NULL,
  schema_version INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS offline_sales (
  local_sale_id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL,
  store_id TEXT NOT NULL,
  terminal_id TEXT NOT NULL,
  occurred_at_utc TEXT NOT NULL,
  currency TEXT NOT NULL,
  subtotal_cents INTEGER NOT NULL,
  discount_cents INTEGER NOT NULL,
  total_cents INTEGER NOT NULL,
  paid_cents INTEGER NOT NULL,
  status TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS local_outbox_events (
  id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL,
  store_id TEXT NOT NULL,
  terminal_id TEXT NOT NULL,
  event_type TEXT NOT NULL,
  schema_version INTEGER NOT NULL,
  sequence_number INTEGER NOT NULL,
  payload_json TEXT NOT NULL,
  status INTEGER NOT NULL,
  created_at_utc TEXT NOT NULL,
  synced_at_utc TEXT NULL,
  last_error TEXT NULL,
  attempts INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS local_sync_acknowledgements (
  id TEXT PRIMARY KEY,
  batch_id TEXT NOT NULL,
  outbox_event_id TEXT NOT NULL,
  remote_status TEXT NOT NULL,
  remote_response_json TEXT NOT NULL,
  acknowledged_at_utc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_local_outbox_pending ON local_outbox_events(status, sequence_number);
CREATE INDEX IF NOT EXISTS idx_local_sync_ack_event ON local_sync_acknowledgements(outbox_event_id, acknowledged_at_utc);
""");
        return Task.CompletedTask;
    }

    public Task SaveTerminalBindingAsync(TerminalBinding binding, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO terminal_binding (id, tenant_id, store_id, terminal_id, terminal_fingerprint, terminal_token, bound_at_utc, schema_version)
VALUES (1, $tenantId, $storeId, $terminalId, $fingerprint, $token, $boundAtUtc, $schemaVersion)
ON CONFLICT(id) DO UPDATE SET
  tenant_id = excluded.tenant_id,
  store_id = excluded.store_id,
  terminal_id = excluded.terminal_id,
  terminal_fingerprint = excluded.terminal_fingerprint,
  terminal_token = excluded.terminal_token,
  bound_at_utc = excluded.bound_at_utc,
  schema_version = excluded.schema_version;
""";
        command.Parameters.AddWithValue("$tenantId", binding.TenantId.ToString());
        command.Parameters.AddWithValue("$storeId", binding.StoreId.ToString());
        command.Parameters.AddWithValue("$terminalId", binding.TerminalId.ToString());
        command.Parameters.AddWithValue("$fingerprint", binding.TerminalFingerprint);
        command.Parameters.AddWithValue("$token", binding.TerminalToken);
        command.Parameters.AddWithValue("$boundAtUtc", binding.BoundAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$schemaVersion", binding.SchemaVersion);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<TerminalBinding?> GetTerminalBindingAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT tenant_id, store_id, terminal_id, terminal_fingerprint, terminal_token, bound_at_utc, schema_version FROM terminal_binding WHERE id = 1;";
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return Task.FromResult<TerminalBinding?>(null);

        var binding = new TerminalBinding(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(2)),
            reader.GetString(3),
            reader.GetString(4),
            DateTimeOffset.Parse(reader.GetString(5)),
            reader.GetInt32(6));
        return Task.FromResult<TerminalBinding?>(binding);
    }

    public Task SaveOfflineSaleAsync(OfflineSaleDraft sale, LocalOutboxEvent outboxEvent, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var saleCommand = connection.CreateCommand())
        {
            saleCommand.Transaction = transaction;
            saleCommand.CommandText = """
INSERT INTO offline_sales (local_sale_id, tenant_id, store_id, terminal_id, occurred_at_utc, currency, subtotal_cents, discount_cents, total_cents, paid_cents, status)
VALUES ($saleId, $tenantId, $storeId, $terminalId, $occurredAtUtc, $currency, $subtotalCents, $discountCents, $totalCents, $paidCents, 'pending_sync');
""";
            saleCommand.Parameters.AddWithValue("$saleId", sale.LocalSaleId.ToString());
            saleCommand.Parameters.AddWithValue("$tenantId", sale.TenantId.ToString());
            saleCommand.Parameters.AddWithValue("$storeId", sale.StoreId.ToString());
            saleCommand.Parameters.AddWithValue("$terminalId", sale.TerminalId.ToString());
            saleCommand.Parameters.AddWithValue("$occurredAtUtc", sale.OccurredAtUtc.ToString("O"));
            saleCommand.Parameters.AddWithValue("$currency", sale.Currency);
            saleCommand.Parameters.AddWithValue("$subtotalCents", sale.SubtotalCents);
            saleCommand.Parameters.AddWithValue("$discountCents", sale.DiscountCents);
            saleCommand.Parameters.AddWithValue("$totalCents", sale.TotalCents);
            saleCommand.Parameters.AddWithValue("$paidCents", sale.PaidCents);
            saleCommand.ExecuteNonQuery();
        }

        InsertOutboxEvent(connection, transaction, outboxEvent);
        transaction.Commit();
        return Task.CompletedTask;
    }

    public Task SaveOutboxEventAsync(LocalOutboxEvent outboxEvent, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        InsertOutboxEvent(connection, transaction, outboxEvent);
        transaction.Commit();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LocalOutboxEvent>> GetPendingOutboxEventsAsync(int limit, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT id, tenant_id, store_id, terminal_id, event_type, schema_version, sequence_number, payload_json, status, created_at_utc, synced_at_utc, last_error, attempts
FROM local_outbox_events
WHERE status = $status
ORDER BY sequence_number
LIMIT $limit;
""";
        command.Parameters.AddWithValue("$status", (int)LocalOutboxStatus.Pending);
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var events = new List<LocalOutboxEvent>();
        while (reader.Read())
        {
            events.Add(ReadOutboxEvent(reader));
        }

        return Task.FromResult<IReadOnlyList<LocalOutboxEvent>>(events);
    }

    public Task<LocalOutboxEvent?> GetLatestOutboxEventByStatusAsync(LocalOutboxStatus status, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT id, tenant_id, store_id, terminal_id, event_type, schema_version, sequence_number, payload_json, status, created_at_utc, synced_at_utc, last_error, attempts
FROM local_outbox_events
WHERE status = $status
ORDER BY sequence_number DESC
LIMIT 1;
""";
        command.Parameters.AddWithValue("$status", (int)status);
        using var reader = command.ExecuteReader();
        return Task.FromResult(reader.Read() ? ReadOutboxEvent(reader) : null);
    }

    public Task MarkOutboxSyncedAsync(IEnumerable<Guid> eventIds, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        foreach (var eventId in eventIds)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE local_outbox_events SET status = $status, synced_at_utc = $syncedAtUtc WHERE id = $id;";
            command.Parameters.AddWithValue("$status", (int)LocalOutboxStatus.Synced);
            command.Parameters.AddWithValue("$syncedAtUtc", syncedAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$id", eventId.ToString());
            command.ExecuteNonQuery();
        }

        return Task.CompletedTask;
    }

    public Task MarkOutboxFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE local_outbox_events SET status = $status, last_error = $error, attempts = attempts + 1 WHERE id = $id;";
        command.Parameters.AddWithValue("$status", (int)LocalOutboxStatus.Failed);
        command.Parameters.AddWithValue("$error", error);
        command.Parameters.AddWithValue("$id", eventId.ToString());
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task ResetOutboxEventToPendingAsync(Guid eventId, string reason, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
UPDATE local_outbox_events
SET status = $pendingStatus, synced_at_utc = NULL, last_error = $reason
WHERE id = $id;
""";
        command.Parameters.AddWithValue("$pendingStatus", (int)LocalOutboxStatus.Pending);
        command.Parameters.AddWithValue("$reason", reason);
        command.Parameters.AddWithValue("$id", eventId.ToString());
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<int> RetryFailedOutboxEventsAsync(int maxAttempts, string reason, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
UPDATE local_outbox_events
SET status = $pendingStatus, last_error = $reason
WHERE status = $failedStatus AND attempts < $maxAttempts;
SELECT changes();
""";
        command.Parameters.AddWithValue("$pendingStatus", (int)LocalOutboxStatus.Pending);
        command.Parameters.AddWithValue("$failedStatus", (int)LocalOutboxStatus.Failed);
        command.Parameters.AddWithValue("$maxAttempts", maxAttempts);
        command.Parameters.AddWithValue("$reason", reason);
        return Task.FromResult(Convert.ToInt32(command.ExecuteScalar()));
    }

    public Task SaveSyncAcknowledgementsAsync(IEnumerable<LocalSyncAcknowledgement> acknowledgements, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var acknowledgement in acknowledgements)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO local_sync_acknowledgements (id, batch_id, outbox_event_id, remote_status, remote_response_json, acknowledged_at_utc)
VALUES ($id, $batchId, $outboxEventId, $remoteStatus, $remoteResponseJson, $acknowledgedAtUtc);";
            command.Parameters.AddWithValue("$id", acknowledgement.Id.ToString());
            command.Parameters.AddWithValue("$batchId", acknowledgement.BatchId.ToString());
            command.Parameters.AddWithValue("$outboxEventId", acknowledgement.OutboxEventId.ToString());
            command.Parameters.AddWithValue("$remoteStatus", acknowledgement.RemoteStatus);
            command.Parameters.AddWithValue("$remoteResponseJson", acknowledgement.RemoteResponseJson);
            command.Parameters.AddWithValue("$acknowledgedAtUtc", acknowledgement.AcknowledgedAtUtc.ToString("O"));
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        return Task.CompletedTask;
    }

    public Task<int> CountOutboxByStatusAsync(LocalOutboxStatus status, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM local_outbox_events WHERE status = $status;";
        command.Parameters.AddWithValue("$status", (int)status);
        return Task.FromResult(Convert.ToInt32(command.ExecuteScalar()));
    }

    private static void InsertOutboxEvent(SqliteConnection connection, SqliteTransaction transaction, LocalOutboxEvent outboxEvent)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
INSERT INTO local_outbox_events (id, tenant_id, store_id, terminal_id, event_type, schema_version, sequence_number, payload_json, status, created_at_utc, synced_at_utc, last_error, attempts)
VALUES ($id, $tenantId, $storeId, $terminalId, $eventType, $schemaVersion, $sequenceNumber, $payloadJson, $status, $createdAtUtc, $syncedAtUtc, $lastError, $attempts);
""";
        command.Parameters.AddWithValue("$id", outboxEvent.Id.ToString());
        command.Parameters.AddWithValue("$tenantId", outboxEvent.TenantId.ToString());
        command.Parameters.AddWithValue("$storeId", outboxEvent.StoreId.ToString());
        command.Parameters.AddWithValue("$terminalId", outboxEvent.TerminalId.ToString());
        command.Parameters.AddWithValue("$eventType", outboxEvent.EventType);
        command.Parameters.AddWithValue("$schemaVersion", outboxEvent.SchemaVersion);
        command.Parameters.AddWithValue("$sequenceNumber", outboxEvent.SequenceNumber);
        command.Parameters.AddWithValue("$payloadJson", outboxEvent.PayloadJson);
        command.Parameters.AddWithValue("$status", (int)outboxEvent.Status);
        command.Parameters.AddWithValue("$createdAtUtc", outboxEvent.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$syncedAtUtc", outboxEvent.SyncedAtUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$lastError", outboxEvent.LastError ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$attempts", outboxEvent.Attempts);
        command.ExecuteNonQuery();
    }

    private static LocalOutboxEvent ReadOutboxEvent(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        Guid.Parse(reader.GetString(2)),
        Guid.Parse(reader.GetString(3)),
        reader.GetString(4),
        reader.GetInt32(5),
        reader.GetInt64(6),
        reader.GetString(7),
        (LocalOutboxStatus)reader.GetInt32(8),
        DateTimeOffset.Parse(reader.GetString(9)),
        reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10)),
        reader.IsDBNull(11) ? null : reader.GetString(11),
        reader.GetInt32(12));

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
