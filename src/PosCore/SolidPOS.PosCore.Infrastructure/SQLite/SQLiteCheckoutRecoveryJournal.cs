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
WHERE terminal_id=$terminal AND state='pending' AND local_sale_id<>$sale;
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
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM local_checkout_commits WHERE local_sale_id=$sale;";
        cmd.Parameters.AddWithValue("$sale", localSaleId.ToString());
        // A checkout never committed when the table itself has not been created.
        using var exists = connection.CreateCommand();
        exists.Transaction = tx;
        exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='local_checkout_commits';";
        if (Convert.ToInt64(exists.ExecuteScalar()) == 0) return false;
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
