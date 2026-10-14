using System.Text.Json;
using Microsoft.Data.Sqlite;
using SolidPOS.PosCore.Application.OfflineSales;

namespace SolidPOS.PosCore.Infrastructure.SQLite;

/// <summary>
/// Second-operator, fail-closed resolution of a checkout intent that never
/// produced financial records. Retains the original intent and an append-only
/// resolution + audit record. Never refunds, replays or creates a sale.
/// </summary>
public sealed class SQLiteSupervisedCheckoutRecoveryService
{
    private readonly SQLiteLocalDatabase database;

    public SQLiteSupervisedCheckoutRecoveryService(SQLiteLocalDatabase database)
        => this.database = database ?? throw new ArgumentNullException(nameof(database));

    public void ResolveUncommitted(Guid saleId, Guid supervisorSessionId, string reason,
        string evidenceReference, DateTimeOffset atUtc, bool physicalCashAttested = false)
    {
        if (!physicalCashAttested)
            throw new InvalidOperationException("Supervisor must explicitly attest physical cash verification.");
        if (saleId == Guid.Empty || supervisorSessionId == Guid.Empty)
            throw new InvalidOperationException("Checkout and supervisor IDs are required.");
        reason = reason?.Trim() ?? "";
        evidenceReference = evidenceReference?.Trim() ?? "";
        if (reason.Length < 20 || reason.Length > 1000 ||
            evidenceReference.Length < 8 || evidenceReference.Length > 512)
            throw new InvalidOperationException("Detailed reason and external evidence reference are required.");

        using var connection = database.OpenConnection();
        using var tx = connection.BeginTransaction();
        long Count(string sql, params (string Name, object Value)[] parameters)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            foreach (var (key, value) in parameters)
                cmd.Parameters.AddWithValue(key, value);
            return Convert.ToInt64(cmd.ExecuteScalar());
        }

        string tenant, store, terminal, requestJson;
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
SELECT tenant_id, store_id, terminal_id, request_json
FROM local_checkout_recovery r
WHERE local_sale_id=$sale AND state='pending'
AND NOT EXISTS (SELECT 1 FROM local_checkout_recovery_resolutions x WHERE x.local_sale_id=r.local_sale_id);
""";
            cmd.Parameters.AddWithValue("$sale", saleId.ToString());
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                throw new InvalidOperationException("Unresolved checkout intent not found.");
            tenant = reader.GetString(0);
            store = reader.GetString(1);
            terminal = reader.GetString(2);
            requestJson = reader.GetString(3);
        }
        var prior = JsonSerializer.Deserialize<AtomicCashCheckoutRequest>(requestJson)
            ?? throw new InvalidOperationException("Checkout journal is unreadable.");
        if (prior.Sale.LocalSaleId != saleId ||
            prior.Sale.TenantId.ToString() != tenant ||
            prior.Sale.StoreId.ToString() != store ||
            prior.Sale.TerminalId.ToString() != terminal)
            throw new InvalidOperationException("Checkout journal scope mismatch.");

        string supervisorId;
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
SELECT s.user_id, s.status, s.expires_at_utc,
       u.is_active, u.last_synced_at_utc, u.max_offline_hours
FROM local_sessions s JOIN local_users u ON u.user_id=s.user_id
WHERE s.session_id=$session AND s.tenant_id=$tenant AND s.store_id=$store
AND u.tenant_id=$tenant AND u.store_id=$store;
""";
            cmd.Parameters.AddWithValue("$session", supervisorSessionId.ToString());
            cmd.Parameters.AddWithValue("$tenant", tenant);
            cmd.Parameters.AddWithValue("$store", store);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                throw new InvalidOperationException("Supervisor session is outside checkout store.");
            supervisorId = reader.GetString(0);
            if (reader.GetString(1) != "active" ||
                DateTimeOffset.Parse(reader.GetString(2)) <= atUtc ||
                reader.GetInt64(3) != 1 ||
                reader.GetInt32(5) <= 0 ||
                DateTimeOffset.Parse(reader.GetString(4)).AddHours(reader.GetInt32(5)) < atUtc)
                throw new InvalidOperationException("Supervisor session or offline authority expired.");
        }
        if (prior.Sale.CashierUserId is null ||
            prior.Sale.CashierUserId.Value.ToString() == supervisorId)
            throw new InvalidOperationException("Resolution requires an independent supervisor.");

        if (Count("""
SELECT COUNT(*) FROM local_user_permissions
WHERE user_id=$user AND permission_code IN ('sales.void','cash.close');
""", ("$user", supervisorId)) != 2)
            throw new InvalidOperationException("Both sales.void and cash.close permissions are required.");

        if (Count("""
SELECT COUNT(*) FROM terminal_binding WHERE id=1 AND tenant_id=$tenant AND
store_id=$store AND terminal_id=$terminal AND schema_version=4;
""", ("$tenant", tenant), ("$store", store), ("$terminal", terminal)) != 1)
            throw new InvalidOperationException("Terminal binding has changed.");

        // Closed list of database table/column names; no user-controlled SQL.
        var linked = new (string Table, string Condition)[]
        {
            ("local_checkout_commits", "local_sale_id=$sale"),
            ("offline_sales", "local_sale_id=$sale"),
            ("local_checkout_lines", "local_sale_id=$sale"),
            ("local_sale_payments", "local_sale_id=$sale"),
            ("local_cash_movements", "source_id=$sale"),
            ("local_inventory_movements", "local_sale_id=$sale"),
            ("local_print_jobs", "sale_id=$sale")
        };
        foreach (var (table, condition) in linked)
        {
            if (Count("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$table;",
                ("$table", table)) == 0) continue;
            if (Count("SELECT COUNT(*) FROM " + table + " WHERE " + condition + ";",
                ("$sale", saleId.ToString())) != 0)
                throw new InvalidOperationException("Financial effects exist; cannot mark checkout uncommitted.");
        }
        if (Count("""
SELECT COUNT(*) FROM local_outbox_events
WHERE tenant_id=$tenant AND store_id=$store AND terminal_id=$terminal
AND payload_json LIKE $sale_pattern;
""", ("$tenant", tenant), ("$store", store), ("$terminal", terminal),
             ("$sale_pattern", "%" + saleId.ToString() + "%")) != 0)
            throw new InvalidOperationException("Outbox contains the sale; recovery denied.");
        if (Count("""
SELECT COUNT(*) FROM local_audit_events
WHERE event_type='local.checkout.cash_committed' AND
tenant_id=$tenant AND store_id=$store AND message LIKE $sale_pattern;
""", ("$tenant", tenant), ("$store", store),
             ("$sale_pattern", "%sale=" + saleId.ToString("N") + "%")) != 0)
            throw new InvalidOperationException("A financial commit audit exists; recovery denied.");

        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
INSERT INTO local_checkout_recovery_resolutions
(local_sale_id,tenant_id,store_id,terminal_id,supervisor_user_id,
 supervisor_session_id,reason,evidence_reference,resolved_at_utc)
VALUES ($sale,$tenant,$store,$terminal,$user,$session,$reason,$evidence,$now);
""";
            cmd.Parameters.AddWithValue("$sale", saleId.ToString());
            cmd.Parameters.AddWithValue("$tenant", tenant);
            cmd.Parameters.AddWithValue("$store", store);
            cmd.Parameters.AddWithValue("$terminal", terminal);
            cmd.Parameters.AddWithValue("$user", supervisorId);
            cmd.Parameters.AddWithValue("$session", supervisorSessionId.ToString());
            cmd.Parameters.AddWithValue("$reason", reason);
            cmd.Parameters.AddWithValue("$evidence", evidenceReference);
            cmd.Parameters.AddWithValue("$now", atUtc.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
INSERT INTO local_audit_events
(id,tenant_id,store_id,user_id,session_id,event_type,message,occurred_at_utc)
VALUES ($id,$tenant,$store,$user,$session,
'local.checkout.intent_resolved_uncommitted',$message,$now);
""";
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$tenant", tenant);
            cmd.Parameters.AddWithValue("$store", store);
            cmd.Parameters.AddWithValue("$user", supervisorId);
            cmd.Parameters.AddWithValue("$session", supervisorSessionId.ToString());
            cmd.Parameters.AddWithValue("$message",
                $"sale={saleId:N};physical_cash_attested=true;reason={reason};evidence={evidenceReference}");
            cmd.Parameters.AddWithValue("$now", atUtc.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
}
