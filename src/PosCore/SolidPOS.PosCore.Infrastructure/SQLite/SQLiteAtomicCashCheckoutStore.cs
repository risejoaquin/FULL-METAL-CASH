using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SolidPOS.PosCore.Application.OfflineSales;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Infrastructure.SQLite;

/// <summary>
/// Commits cash-only checkout, local price snapshots, stock movements, cash
/// ledger, schema-v4 outbox, print job and audit as ONE SQLite transaction.
/// It does not send HTTP, print or contact a payment provider.
/// </summary>
public sealed class SQLiteAtomicCashCheckoutStore : IAtomicCashCheckoutStore
{
    private readonly SQLiteLocalDatabase database;

    public SQLiteAtomicCashCheckoutStore(SQLiteLocalDatabase database)
        => this.database = database ?? throw new ArgumentNullException(nameof(database));

    public Task<AtomicCashCheckoutResult> CommitAsync(
        AtomicCashCheckoutRequest request, LocalOutboxEvent outboxEvent,
        IReadOnlyCollection<LocalInventoryMovement> inventoryMovements,
        DateTimeOffset committedAtUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(outboxEvent);
        ArgumentNullException.ThrowIfNull(inventoryMovements);
        cancellationToken.ThrowIfCancellationRequested();

        OfflineSaleDraft sale = request.Sale;
        if (outboxEvent.EventType != "sale.completed" || outboxEvent.SchemaVersion != 4 ||
            outboxEvent.TenantId != sale.TenantId || outboxEvent.StoreId != sale.StoreId ||
            outboxEvent.TerminalId != sale.TerminalId ||
            outboxEvent.Status != LocalOutboxStatus.Pending)
            throw new InvalidOperationException("Invalid checkout outbox contract.");
        if (sale.TotalCents <= 0 || request.TenderedCents < sale.TotalCents ||
            sale.Payments.Count != 1 ||
            !string.Equals(sale.Payments[0].MethodCode, "cash", StringComparison.OrdinalIgnoreCase) ||
            sale.Payments[0].AmountCents != sale.TotalCents ||
            sale.Payments[0].LocalPaymentId is null ||
            sale.Payments[0].LocalPaymentId == Guid.Empty)
            throw new InvalidOperationException("A cash-only, exact-amount payment is required.");
        if (sale.TipCents != 0 || sale.CashierUserId is null || sale.CashierUserId == Guid.Empty)
            throw new InvalidOperationException("Unsupported tip or missing cashier.");
        if (request.SessionId == Guid.Empty || request.CashShiftId == Guid.Empty ||
            request.ReceiptId == Guid.Empty || request.PrintJobId == Guid.Empty ||
            sale.LocalSaleId == Guid.Empty)
            throw new InvalidOperationException("Checkout identifiers are required.");

        int changeCents = checked(request.TenderedCents - sale.TotalCents);
        string fingerprint = Fingerprint(request);
        using var connection = database.OpenConnection();
        using var tx = connection.BeginTransaction();
        // Local additive tables only; no PostgreSQL or remote schema modification.
        Execute(connection, tx, """
CREATE TABLE IF NOT EXISTS local_checkout_commits (
  local_sale_id TEXT PRIMARY KEY,
  fingerprint TEXT NOT NULL,
  cash_shift_id TEXT NOT NULL,
  outbox_event_id TEXT NOT NULL,
  print_job_id TEXT NOT NULL,
  receipt_number TEXT NOT NULL,
  total_cents INTEGER NOT NULL,
  tendered_cents INTEGER NOT NULL,
  change_cents INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS local_checkout_lines (
  local_sale_id TEXT NOT NULL,
  line_index INTEGER NOT NULL,
  product_id TEXT NOT NULL,
  variant_id TEXT NULL,
  sku TEXT NOT NULL,
  name TEXT NOT NULL,
  quantity INTEGER NOT NULL,
  unit_price_cents INTEGER NOT NULL,
  discount_cents INTEGER NOT NULL,
  PRIMARY KEY(local_sale_id, line_index)
);
""");

        // A supervisor may have resolved a pre-commit intent while an old
        // process was suspended. Recheck under the same SQLite writer lock:
        // resolved sale IDs can never subsequently create financial records.
        if (Scalar(connection, tx,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='local_checkout_recovery_resolutions';") > 0 &&
            Scalar(connection, tx,
                "SELECT COUNT(*) FROM local_checkout_recovery_resolutions WHERE local_sale_id=$sale;",
                ("$sale", sale.LocalSaleId.ToString())) != 0)
            throw new InvalidOperationException("Checkout ID was resolved without sale and cannot be committed.");

        // Authentication and authority are read inside the same transaction,
        // including on an idempotent replay. Never trust a WPF enabled button.
        using (var cmd = Command(connection, tx, """
SELECT s.user_id, s.tenant_id, s.store_id, s.status, s.expires_at_utc,
       u.is_active, u.last_synced_at_utc, u.max_offline_hours
FROM local_sessions s JOIN local_users u ON u.user_id = s.user_id
WHERE s.session_id = $session;
""", ("$session", request.SessionId.ToString())))
        using (var reader = cmd.ExecuteReader())
        {
            if (!reader.Read() || !string.Equals(reader.GetString(3), "active", StringComparison.Ordinal) ||
                reader.GetString(0) != sale.CashierUserId.Value.ToString() ||
                reader.GetString(1) != sale.TenantId.ToString() ||
                reader.GetString(2) != sale.StoreId.ToString() ||
                DateTimeOffset.Parse(reader.GetString(4)) <= committedAtUtc ||
                reader.GetInt64(5) != 1 ||
                reader.GetInt32(7) <= 0 ||
                DateTimeOffset.Parse(reader.GetString(6)).AddHours(reader.GetInt32(7)) < committedAtUtc)
                throw new InvalidOperationException("Checkout requires a valid same-store offline session.");
        }

        if (Scalar(connection, tx, """
SELECT COUNT(*) FROM local_user_permissions
WHERE user_id = $user AND permission_code = 'sales.create';
""", ("$user", sale.CashierUserId.Value.ToString())) != 1)
            throw new InvalidOperationException("Cashier lacks sales.create permission.");

        if (Scalar(connection, tx, """
SELECT COUNT(*) FROM terminal_binding
WHERE id = 1 AND tenant_id = $tenant AND store_id = $store
  AND terminal_id = $terminal AND schema_version = 4
  AND length(terminal_token) > 0 AND length(terminal_fingerprint) > 0;
""", ("$tenant", sale.TenantId.ToString()), ("$store", sale.StoreId.ToString()),
           ("$terminal", sale.TerminalId.ToString())) != 1)
            throw new InvalidOperationException("Terminal is not bound for schema v4.");

        using (var cmd = Command(connection, tx, """
SELECT fingerprint, cash_shift_id, outbox_event_id, print_job_id,
       receipt_number, total_cents, tendered_cents, change_cents
FROM local_checkout_commits WHERE local_sale_id = $sale;
""", ("$sale", sale.LocalSaleId.ToString())))
        using (var reader = cmd.ExecuteReader())
        {
            if (reader.Read())
            {
                if (!string.Equals(reader.GetString(0), fingerprint, StringComparison.Ordinal))
                    throw new InvalidOperationException("Checkout ID reuse with changed financial data.");
                var existing = new AtomicCashCheckoutResult(sale.LocalSaleId,
                    Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2)),
                    Guid.Parse(reader.GetString(3)), reader.GetString(4),
                    reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), true);
                tx.Commit();
                return Task.FromResult(existing);
            }
        }

        if (Scalar(connection, tx, """
SELECT COUNT(*) FROM local_cash_shifts
WHERE id = $shift AND status = 'open' AND tenant_id = $tenant
AND store_id = $store AND terminal_id = $terminal;
""", ("$shift", request.CashShiftId.ToString()),
           ("$tenant", sale.TenantId.ToString()), ("$store", sale.StoreId.ToString()),
           ("$terminal", sale.TerminalId.ToString())) != 1)
            throw new InvalidOperationException("An open matching cash shift is required.");

        for (int index = 0; index < sale.Lines.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = sale.Lines[index];
            if (line.DiscountCents != 0)
                throw new InvalidOperationException("Promotional discounts require an authorized rule workflow.");
            if (Scalar(connection, tx, """
SELECT COUNT(*) FROM local_catalog_products
WHERE product_id = $product AND variant_id IS $variant AND sku = $sku
AND name = $name AND price_cents = $price AND currency = 'MXN'
AND status = 'active';
""", ("$product", line.ProductId.ToString()),
                ("$variant", line.VariantId?.ToString()),
                ("$sku", line.Sku), ("$name", line.Name),
                ("$price", line.UnitPriceCents)) != 1)
                throw new InvalidOperationException("Checkout catalog price or product changed; refresh cart.");
        }

        foreach (var movement in inventoryMovements)
        {
            if (movement.LocalSaleId != sale.LocalSaleId ||
                movement.TenantId != sale.TenantId || movement.StoreId != sale.StoreId ||
                movement.TerminalId != sale.TerminalId || movement.QuantityDelta >= 0)
                throw new InvalidOperationException("Invalid inventory movement scope or quantity.");
        }

        string receiptNumber = "LOCAL-" + sale.LocalSaleId.ToString("N").ToUpperInvariant();
        Execute(connection, tx, """
INSERT INTO offline_sales
(local_sale_id, tenant_id, store_id, terminal_id, occurred_at_utc,
 currency, subtotal_cents, discount_cents, total_cents, paid_cents, status)
VALUES ($sale, $tenant, $store, $terminal, $occurred, $currency,
        $subtotal, $discount, $total, $paid, 'pending_sync');
""", ("$sale", sale.LocalSaleId.ToString()), ("$tenant", sale.TenantId.ToString()),
            ("$store", sale.StoreId.ToString()), ("$terminal", sale.TerminalId.ToString()),
            ("$occurred", sale.OccurredAtUtc.ToString("O")), ("$currency", sale.Currency),
            ("$subtotal", sale.SubtotalCents), ("$discount", sale.DiscountCents),
            ("$total", sale.TotalCents), ("$paid", sale.PaidCents));

        for (int i = 0; i < sale.Lines.Count; i++)
        {
            var line = sale.Lines[i];
            Execute(connection, tx, """
INSERT INTO local_checkout_lines
(local_sale_id, line_index, product_id, variant_id, sku, name, quantity,
 unit_price_cents, discount_cents)
VALUES ($sale, $idx, $product, $variant, $sku, $name, $qty, $price, $discount);
""", ("$sale", sale.LocalSaleId.ToString()), ("$idx", i),
                ("$product", line.ProductId.ToString()), ("$variant", line.VariantId?.ToString()),
                ("$sku", line.Sku), ("$name", line.Name), ("$qty", line.Quantity),
                ("$price", line.UnitPriceCents), ("$discount", line.DiscountCents));
        }

        Execute(connection, tx, """
INSERT INTO local_sale_payments
(local_payment_id, local_sale_id, shift_id, method_code, amount_cents,
 tendered_cents, change_cents, created_at_utc)
VALUES ($payment, $sale, $shift, 'cash', $total, $tendered, $change, $now);
""", ("$payment", sale.Payments[0].LocalPaymentId!.Value.ToString()),
            ("$sale", sale.LocalSaleId.ToString()), ("$shift", request.CashShiftId.ToString()),
            ("$total", sale.TotalCents), ("$tendered", request.TenderedCents),
            ("$change", changeCents), ("$now", committedAtUtc.ToString("O")));

        Execute(connection, tx, """
INSERT INTO local_cash_movements
(id, shift_id, tenant_id, store_id, terminal_id, movement_type,
 amount_cents, occurred_at_utc, source_type, source_id, note)
VALUES ($id, $shift, $tenant, $store, $terminal, 'sale_cash',
        $total, $now, 'offline_sale', $sale, $note);
""", ("$id", Guid.NewGuid().ToString()), ("$shift", request.CashShiftId.ToString()),
            ("$tenant", sale.TenantId.ToString()), ("$store", sale.StoreId.ToString()),
            ("$terminal", sale.TerminalId.ToString()), ("$total", sale.TotalCents),
            ("$now", committedAtUtc.ToString("O")), ("$sale", sale.LocalSaleId.ToString()),
            ("$note", $"tendered={request.TenderedCents};change={changeCents}"));

        foreach (var movement in inventoryMovements)
        {
            Execute(connection, tx, """
INSERT INTO local_inventory_movements
(id, local_sale_id, tenant_id, store_id, terminal_id, product_id, variant_id,
 movement_type, quantity_delta, unit_id, occurred_at_utc, source, created_at_utc)
VALUES ($id, $sale, $tenant, $store, $terminal, $product, $variant,
        $type, $qty, $unit, $occurred, $source, $created);
""", ("$id", movement.Id.ToString()), ("$sale", sale.LocalSaleId.ToString()),
                ("$tenant", movement.TenantId.ToString()), ("$store", movement.StoreId.ToString()),
                ("$terminal", movement.TerminalId.ToString()), ("$product", movement.ProductId.ToString()),
                ("$variant", movement.VariantId?.ToString()), ("$type", movement.MovementType),
                ("$qty", movement.QuantityDelta.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("$unit", movement.UnitId.ToString()), ("$occurred", movement.OccurredAtUtc.ToString("O")),
                ("$source", movement.Source), ("$created", movement.CreatedAtUtc.ToString("O")));
        }

        Execute(connection, tx, """
INSERT INTO local_outbox_events
(id, tenant_id, store_id, terminal_id, event_type, schema_version,
 sequence_number, payload_json, status, created_at_utc, attempts)
VALUES ($id, $tenant, $store, $terminal, 'sale.completed', 4,
        $sequence, $payload, $status, $created, 0);
""", ("$id", outboxEvent.Id.ToString()), ("$tenant", sale.TenantId.ToString()),
            ("$store", sale.StoreId.ToString()), ("$terminal", sale.TerminalId.ToString()),
            ("$sequence", outboxEvent.SequenceNumber), ("$payload", outboxEvent.PayloadJson),
            ("$status", (int)LocalOutboxStatus.Pending), ("$created", committedAtUtc.ToString("O")));

        string receiptContent = BuildReceipt(sale, receiptNumber, request.TenderedCents, changeCents);
        Execute(connection, tx, """
INSERT INTO local_print_jobs
(id, tenant_id, store_id, terminal_id, sale_id, receipt_id, receipt_number,
 content, status, queued_at_utc, attempts)
VALUES ($id, $tenant, $store, $terminal, $sale, $receipt, $number,
        $content, 'pending', $now, 0);
""", ("$id", request.PrintJobId.ToString()), ("$tenant", sale.TenantId.ToString()),
            ("$store", sale.StoreId.ToString()), ("$terminal", sale.TerminalId.ToString()),
            ("$sale", sale.LocalSaleId.ToString()), ("$receipt", request.ReceiptId.ToString()),
            ("$number", receiptNumber), ("$content", receiptContent),
            ("$now", committedAtUtc.ToString("O")));

        Execute(connection, tx, """
INSERT INTO local_audit_events
(id, tenant_id, store_id, user_id, session_id, event_type, message, occurred_at_utc)
VALUES ($id, $tenant, $store, $user, $session, 'local.checkout.cash_committed',
        $message, $now);
""", ("$id", Guid.NewGuid().ToString()), ("$tenant", sale.TenantId.ToString()),
            ("$store", sale.StoreId.ToString()), ("$user", sale.CashierUserId.Value.ToString()),
            ("$session", request.SessionId.ToString()),
            ("$message", $"sale={sale.LocalSaleId:N};amount={sale.TotalCents};shift={request.CashShiftId:N}"),
            ("$now", committedAtUtc.ToString("O")));

        Execute(connection, tx, """
INSERT INTO local_checkout_commits
(local_sale_id, fingerprint, cash_shift_id, outbox_event_id, print_job_id,
 receipt_number, total_cents, tendered_cents, change_cents)
VALUES ($sale, $fingerprint, $shift, $outbox, $print, $number, $total, $tendered, $change);
""", ("$sale", sale.LocalSaleId.ToString()), ("$fingerprint", fingerprint),
            ("$shift", request.CashShiftId.ToString()), ("$outbox", outboxEvent.Id.ToString()),
            ("$print", request.PrintJobId.ToString()), ("$number", receiptNumber),
            ("$total", sale.TotalCents), ("$tendered", request.TenderedCents), ("$change", changeCents));

        cancellationToken.ThrowIfCancellationRequested();
        tx.Commit();
        return Task.FromResult(new AtomicCashCheckoutResult(
            sale.LocalSaleId, request.CashShiftId, outboxEvent.Id, request.PrintJobId,
            receiptNumber, sale.TotalCents, request.TenderedCents, changeCents, false));
    }

    private static string Fingerprint(AtomicCashCheckoutRequest req)
    {
        var s = req.Sale;
        string json = JsonSerializer.Serialize(new
        {
            s.LocalSaleId, s.TenantId, s.StoreId, s.TerminalId, s.CashierUserId,
            s.CustomerId, s.Currency, s.OccurredAtUtc, s.TipCents,
            req.CashShiftId, req.TenderedCents,
            Lines = s.Lines.Select(l => new
            {
                l.ProductId, l.VariantId, l.Sku, l.Name, l.Quantity,
                l.UnitPriceCents, l.DiscountCents, l.DiscountId, l.PreparationNote,
                l.ModifierIds
            }),
            Payments = s.Payments.Select(p => new
            {
                p.LocalPaymentId, p.MethodCode, p.AmountCents, p.Reference
            })
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static string BuildReceipt(OfflineSaleDraft sale, string number, int tendered, int change)
    {
        static string Clean(string value) => string.Concat(value.Where(ch => !char.IsControl(ch)));
        var lines = new List<string>
        {
            "SOLIDPOS - COMPROBANTE DE VENTA (NO CFDI)",
            number, sale.OccurredAtUtc.ToString("O"), "------------------------------"
        };
        foreach (var line in sale.Lines)
            lines.Add($"{line.Quantity} x {Clean(line.Name)} [{Clean(line.Sku)}] : {line.NetCents} centavos");
        lines.Add($"TOTAL: {sale.TotalCents} centavos MXN");
        lines.Add($"EFECTIVO: {tendered} centavos");
        lines.Add($"CAMBIO: {change} centavos");
        return string.Join(Environment.NewLine, lines);
    }

    private static long Scalar(SqliteConnection c, SqliteTransaction t, string sql,
        params (string Name, object? Value)[] values)
    {
        using var cmd = Command(c, t, sql, values);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static void Execute(SqliteConnection c, SqliteTransaction t, string sql,
        params (string Name, object? Value)[] values)
    {
        using var cmd = Command(c, t, sql, values);
        cmd.ExecuteNonQuery();
    }

    private static SqliteCommand Command(SqliteConnection c, SqliteTransaction t, string sql,
        params (string Name, object? Value)[] values)
    {
        var cmd = c.CreateCommand();
        cmd.Transaction = t;
        cmd.CommandText = sql;
        foreach (var (name, value) in values)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }
}
