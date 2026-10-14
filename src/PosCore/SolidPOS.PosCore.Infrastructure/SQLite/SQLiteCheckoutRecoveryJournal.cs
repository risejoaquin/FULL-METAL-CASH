using System.Text.Json;
using Microsoft.Data.Sqlite;
using SolidPOS.PosCore.Application.OfflineSales;

namespace SolidPOS.PosCore.Infrastructure.SQLite;

/// <summary>
/// Write-ahead checkout intent. An unresolved intent blocks subsequent checkout
/// after process restart. Never automatically retries an uncertain payment.
/// </summary>
public sealed class SQLiteCheckoutRecoveryJournal
{
    private readonly SQLiteLocalDatabase database;
    public SQLiteCheckoutRecoveryJournal(SQLiteLocalDatabase database)
        => this.database = database ?? throw new ArgumentNullException(nameof(database));

    private static void Ensure(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
CREATE TABLE IF NOT EXISTS local_checkout_recovery (
  local_sale_id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL,
  store_id TEXT NOT NULL,
  terminal_id TEXT NOT NULL,
  request_json TEXT NOT NULL,
  state TEXT NOT NULL CHECK (state IN ('pending','committed')),
  created_at_utc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS local_checkout_recovery_resolutions (
  local_sale_id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL,
  store_id TEXT NOT NULL,
  terminal_id TEXT NOT NULL,
  supervisor_user_id TEXT NOT NULL,
  supervisor_session_id TEXT NOT NULL,
  reason TEXT NOT NULL,
  evidence_reference TEXT NOT NULL,
  resolved_at_utc TEXT NOT NULL,
  FOREIGN KEY(local_sale_id) REFERENCES local_checkout_recovery(local_sale_id)
);
CREATE INDEX IF NOT EXISTS ix_checkout_recovery_terminal_state
 ON local_checkout_recovery (terminal_id, state);
""";
        command.ExecuteNonQuery();
    }

    public void Register(AtomicCashCheckoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var connection = database.OpenConnection();
        Ensure(connection);
        using var tx = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
INSERT INTO local_checkout_recovery
(local_sale_id,tenant_id,store_id,terminal_id,request_json,state,created_at_utc)
VALUES ($sale,$tenant,$store,$terminal,$json,'pending',$now)
ON CONFLICT(local_sale_id) DO NOTHING;
""";
        cmd.Parameters.AddWithValue("$sale", request.Sale.LocalSaleId.ToString());
        cmd.Parameters.AddWithValue("$tenant", request.Sale.TenantId.ToString());
        cmd.Parameters.AddWithValue("$store", request.Sale.StoreId.ToString());
        cmd.Parameters.AddWithValue("$terminal", request.Sale.TerminalId.ToString());
        cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(request));
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
        // Any unresolved operation, including a prior crashed process, blocks a new ID.
        using var check = connection.CreateCommand();
        check.Transaction = tx;
        check.CommandText = """
SELECT COUNT(*) FROM local_checkout_recovery
WHERE terminal_id=$terminal AND state='pending' AND local_sale_id<>$sale
  AND NOT EXISTS (SELECT 1 FROM local_checkout_recovery_resolutions x WHERE x.local_sale_id=local_checkout_recovery.local_sale_id);
""";
        check.Parameters.AddWithValue("$terminal", request.Sale.TerminalId.ToString());
        check.Parameters.AddWithValue("$sale", request.Sale.LocalSaleId.ToString());
        if (Convert.ToInt64(check.ExecuteScalar()) != 0)
            throw new InvalidOperationException("Unresolved checkout intent exists; reconciliation required.");
        using var verify = connection.CreateCommand();
        verify.Transaction = tx;
        verify.CommandText = "SELECT request_json FROM local_checkout_recovery WHERE local_sale_id=$sale;";
        verify.Parameters.AddWithValue("$sale", request.Sale.LocalSaleId.ToString());
        if (!string.Equals(verify.ExecuteScalar() as string, JsonSerializer.Serialize(request), StringComparison.Ordinal))
            throw new InvalidOperationException("Checkout intent ID reused with changed financial data.");
        using var resolved = connection.CreateCommand();
        resolved.Transaction = tx;
        resolved.CommandText = "SELECT COUNT(*) FROM local_checkout_recovery_resolutions WHERE local_sale_id=$sale;";
        resolved.Parameters.AddWithValue("$sale", request.Sale.LocalSaleId.ToString());
        if (Convert.ToInt64(resolved.ExecuteScalar()) != 0)
            throw new InvalidOperationException("Cannot reuse an already resolved checkout ID.");
        tx.Commit();
    }

    public Guid? FindUnresolved(Guid tenantId, Guid storeId, Guid terminalId)
    {
        using var connection = database.OpenConnection();
        Ensure(connection);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
SELECT local_sale_id FROM local_checkout_recovery
WHERE tenant_id=$tenant AND store_id=$store AND terminal_id=$terminal AND state='pending'
AND NOT EXISTS (SELECT 1 FROM local_checkout_recovery_resolutions x WHERE x.local_sale_id=local_checkout_recovery.local_sale_id)
ORDER BY created_at_utc LIMIT 1;
""";
        cmd.Parameters.AddWithValue("$tenant", tenantId.ToString());
        cmd.Parameters.AddWithValue("$store", storeId.ToString());
        cmd.Parameters.AddWithValue("$terminal", terminalId.ToString());
        var id = cmd.ExecuteScalar() as string;
        return id is null ? null : Guid.Parse(id);
    }

    /// <summary>Check financial commit evidence, not a UI flag; leave uncertain intents locked.</summary>
    public bool ReconcileCommitted(Guid localSaleId)
    {
        using var connection = database.OpenConnection();
        Ensure(connection);
        using var tx = connection.BeginTransaction();
        // A marker alone is not enough: verify all financial side effects,
        // and fail closed if an expected table is absent or incomplete.
        string[] tables = { "local_checkout_commits", "offline_sales",
            "local_sale_payments", "local_cash_movements",
            "local_outbox_events", "local_print_jobs", "local_checkout_recovery" };
        foreach (var table in tables)
        {
            using var exists = connection.CreateCommand();
            exists.Transaction = tx;
            exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$table;";
            exists.Parameters.AddWithValue("$table", table);
            if (Convert.ToInt64(exists.ExecuteScalar()) != 1) return false;
        }
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
SELECT COUNT(*) FROM local_checkout_commits c
JOIN local_checkout_recovery r ON r.local_sale_id = c.local_sale_id
JOIN offline_sales s ON s.local_sale_id = c.local_sale_id
JOIN local_sale_payments p ON p.local_sale_id = c.local_sale_id
JOIN local_cash_movements m ON m.source_id = c.local_sale_id
JOIN local_outbox_events o ON o.id = c.outbox_event_id
JOIN local_print_jobs j ON j.id = c.print_job_id
WHERE c.local_sale_id=$sale
  AND c.cash_shift_id=p.shift_id
  AND p.local_sale_id=s.local_sale_id
  AND p.method_code='cash'
  AND p.amount_cents=c.total_cents
  AND s.total_cents=c.total_cents AND s.paid_cents=c.total_cents
  AND m.source_type='offline_sale' AND m.movement_type='sale_cash'
  AND m.amount_cents=c.total_cents
  AND m.shift_id=c.cash_shift_id
  AND m.tenant_id=r.tenant_id AND m.store_id=r.store_id
  AND m.terminal_id=r.terminal_id
  AND o.event_type='sale.completed' AND o.schema_version=4
  AND o.tenant_id=r.tenant_id AND o.store_id=r.store_id
  AND o.terminal_id=r.terminal_id
  AND j.sale_id=c.local_sale_id AND j.tenant_id=r.tenant_id
  AND j.store_id=r.store_id AND j.terminal_id=r.terminal_id
  AND s.tenant_id=r.tenant_id AND s.store_id=r.store_id
  AND s.terminal_id=r.terminal_id
  AND c.total_cents > 0 AND c.tendered_cents >= c.total_cents
  AND c.change_cents = c.tendered_cents - c.total_cents;
""";
        cmd.Parameters.AddWithValue("$sale", localSaleId.ToString());
        if (Convert.ToInt64(cmd.ExecuteScalar()) != 1) return false;
        using var update = connection.CreateCommand();
        update.Transaction = tx;
        update.CommandText = """
UPDATE local_checkout_recovery SET state='committed'
WHERE local_sale_id=$sale AND state='pending';
""";
        update.Parameters.AddWithValue("$sale", localSaleId.ToString());
        update.ExecuteNonQuery();
        tx.Commit();
        return true;
    }
}
