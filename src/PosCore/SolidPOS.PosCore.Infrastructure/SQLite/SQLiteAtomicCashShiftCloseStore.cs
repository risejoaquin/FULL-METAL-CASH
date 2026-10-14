using Microsoft.Data.Sqlite;
using SolidPOS.PosCore.Application.Cash;

namespace SolidPOS.PosCore.Infrastructure.SQLite;

/// <summary>
/// Cash closing runs as one immediate SQLite write transaction. Ledger totals,
/// permission, scope, status, final snapshot and audit can never partially
/// commit. No remote API, dummy cash counter or production database is used.
/// </summary>
public sealed class SQLiteAtomicCashShiftCloseStore : IAtomicCashShiftCloseStore
{
    private readonly SQLiteLocalDatabase database;

    public SQLiteAtomicCashShiftCloseStore(SQLiteLocalDatabase database)
        => this.database = database ?? throw new ArgumentNullException(nameof(database));

    public Task<AtomicCashShiftCloseResult> CloseAsync(
        AtomicCashShiftCloseRequest request, DateTimeOffset closedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ShiftId == Guid.Empty || request.SessionId == Guid.Empty ||
            request.CountedCashCents < 0)
            throw new InvalidOperationException("Invalid cash-shift close request.");
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        Guid userId, tenantId, storeId;

        using (var cmd = Query(connection, transaction, """
SELECT s.user_id, s.tenant_id, s.store_id, s.status, s.expires_at_utc,
       u.is_active, u.last_synced_at_utc, u.max_offline_hours
FROM local_sessions s JOIN local_users u ON u.user_id = s.user_id
WHERE s.session_id = $session
  AND u.tenant_id = s.tenant_id AND u.store_id = s.store_id;
""", ("$session", request.SessionId.ToString())))
        using (var reader = cmd.ExecuteReader())
        {
            if (!reader.Read() || reader.GetString(3) != "active" ||
                DateTimeOffset.Parse(reader.GetString(4)) <= closedAtUtc ||
                reader.GetInt32(5) != 1 || reader.GetInt32(7) <= 0 ||
                DateTimeOffset.Parse(reader.GetString(6)).AddHours(reader.GetInt32(7)) < closedAtUtc)
                throw new InvalidOperationException("Cash closing requires a valid same-store offline session.");

            userId = Guid.Parse(reader.GetString(0));
            tenantId = Guid.Parse(reader.GetString(1));
            storeId = Guid.Parse(reader.GetString(2));
        }
        if (Count(connection, transaction, """
SELECT COUNT(*) FROM local_user_permissions WHERE user_id = $user AND permission_code = 'cash.close';
""", ("$user", userId.ToString())) != 1)
            throw new InvalidOperationException("The operator lacks cash.close permission.");

        Guid terminalId;
        string status;
        Guid? closedBy;
        int? storedCounted, storedExpected, storedDifference;
        int openingCash;
        using (var cmd = Query(connection, transaction, """
SELECT tenant_id, store_id, terminal_id, status, closed_by_user_id,
       opening_amount_cents, counted_cash_cents, expected_cash_cents, difference_cents
FROM local_cash_shifts WHERE id = $shift;
""", ("$shift", request.ShiftId.ToString())))
        using (var reader = cmd.ExecuteReader())
        {
            if (!reader.Read() || reader.GetString(0) != tenantId.ToString() ||
                reader.GetString(1) != storeId.ToString())
                throw new InvalidOperationException("Shift is not accessible from this store.");
            terminalId = Guid.Parse(reader.GetString(2));
            status = reader.GetString(3);
            closedBy = reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4));
            openingCash = reader.GetInt32(5);
            storedCounted = reader.IsDBNull(6) ? null : reader.GetInt32(6);
            storedExpected = reader.IsDBNull(7) ? null : reader.GetInt32(7);
            storedDifference = reader.IsDBNull(8) ? null : reader.GetInt32(8);
        }

        if (Count(connection, transaction, """
SELECT COUNT(*) FROM terminal_binding
WHERE id = 1 AND tenant_id = $tenant AND store_id = $store
  AND terminal_id = $terminal AND schema_version = 4
  AND length(terminal_token) > 0 AND length(terminal_fingerprint) > 0;
""", ("$tenant", tenantId.ToString()), ("$store", storeId.ToString()),
               ("$terminal", terminalId.ToString())) != 1)
            throw new InvalidOperationException("Terminal binding does not match the cash shift.");

        if (status is not ("open" or "closed"))
            throw new InvalidOperationException("Unsupported cash shift state.");

        // Under the write lock, reconcile every cash payment against the local
        // sale and exactly one matching sale_cash ledger movement.
        if (Count(connection, transaction, """
SELECT COUNT(*) FROM local_cash_movements
WHERE shift_id = $shift AND (
    tenant_id != $tenant OR store_id != $store OR terminal_id != $terminal
    OR amount_cents < 0
    OR movement_type NOT IN ('opening','cash_in','cash_out','sale_cash')
);
""", ("$shift", request.ShiftId.ToString()), ("$tenant", tenantId.ToString()),
               ("$store", storeId.ToString()), ("$terminal", terminalId.ToString())) != 0)
            throw new InvalidOperationException("Cash ledger contains invalid or foreign movements.");

        if (Count(connection, transaction, """
SELECT COUNT(*) FROM local_cash_movements
WHERE shift_id = $shift AND movement_type = 'opening' AND amount_cents = $amount
AND source_type = 'local_cash_shift' AND source_id = $shift;
""", ("$shift", request.ShiftId.ToString()), ("$amount", openingCash)) != 1 ||
            Count(connection, transaction, """
SELECT COUNT(*) FROM local_cash_movements
WHERE shift_id = $shift AND movement_type = 'opening';
""", ("$shift", request.ShiftId.ToString())) != 1)
            throw new InvalidOperationException("Cash shift opening ledger does not reconcile.");

        if (Count(connection, transaction, """
SELECT COUNT(*) FROM local_sale_payments p
LEFT JOIN offline_sales s ON s.local_sale_id = p.local_sale_id
WHERE p.shift_id = $shift AND
    (p.method_code != 'cash' OR p.amount_cents <= 0 OR
     s.local_sale_id IS NULL OR s.tenant_id != $tenant OR
     s.store_id != $store OR s.terminal_id != $terminal);
""", ("$shift", request.ShiftId.ToString()),
               ("$tenant", tenantId.ToString()), ("$store", storeId.ToString()),
               ("$terminal", terminalId.ToString())) != 0)
            throw new InvalidOperationException("Cash payment references a foreign or invalid sale.");

        if (Count(connection, transaction, """
SELECT COUNT(*) FROM local_sale_payments p
LEFT JOIN local_cash_movements m
ON m.shift_id = p.shift_id AND m.movement_type = 'sale_cash'
 AND m.source_type = 'offline_sale' AND m.source_id = p.local_sale_id
WHERE p.shift_id = $shift AND (m.id IS NULL OR m.amount_cents != p.amount_cents);
""", ("$shift", request.ShiftId.ToString())) != 0)
            throw new InvalidOperationException("Cash sale ledger does not match recorded payments.");

        long movementCount = Count(connection, transaction, """
SELECT COUNT(*) FROM local_cash_movements
WHERE shift_id = $shift AND movement_type = 'sale_cash';
""", ("$shift", request.ShiftId.ToString()));
        long paymentCount = Count(connection, transaction, """
SELECT COUNT(*) FROM local_sale_payments WHERE shift_id = $shift;
""", ("$shift", request.ShiftId.ToString()));
        if (paymentCount != movementCount)
            throw new InvalidOperationException("Cash ledger and payment counts differ.");

        long cashSales = Count(connection, transaction, """
SELECT COALESCE(SUM(amount_cents),0) FROM local_cash_movements
WHERE shift_id = $shift AND movement_type = 'sale_cash';
""", ("$shift", request.ShiftId.ToString()));
        long cashPaid = Count(connection, transaction, """
SELECT COALESCE(SUM(amount_cents),0) FROM local_sale_payments
WHERE shift_id = $shift AND method_code = 'cash';
""", ("$shift", request.ShiftId.ToString()));
        if (cashSales != cashPaid)
            throw new InvalidOperationException("Cash ledger does not balance with recorded payments.");

        long cashIn = Count(connection, transaction, """
SELECT COALESCE(SUM(amount_cents),0) FROM local_cash_movements
WHERE shift_id = $shift AND movement_type = 'cash_in';
""", ("$shift", request.ShiftId.ToString()));
        long cashOut = Count(connection, transaction, """
SELECT COALESCE(SUM(amount_cents),0) FROM local_cash_movements
WHERE shift_id = $shift AND movement_type = 'cash_out';
""", ("$shift", request.ShiftId.ToString()));

        long expectedLong = checked((long)openingCash + cashSales + cashIn - cashOut);
        if (expectedLong < 0 || expectedLong > int.MaxValue ||
            cashSales > int.MaxValue || cashIn > int.MaxValue || cashOut > int.MaxValue)
            throw new InvalidOperationException("Cash ledger amount is outside supported cent range.");
        int expected = (int)expectedLong;
        int difference = checked(request.CountedCashCents - expected);

        if (status == "closed")
        {
            if (closedBy != userId || storedCounted != request.CountedCashCents ||
                storedExpected != expected || storedDifference != difference)
                throw new InvalidOperationException("Closed cash shift is immutable; manual reconciliation required.");

            transaction.Commit();
            return Task.FromResult(new AtomicCashShiftCloseResult(
                request.ShiftId, expected, request.CountedCashCents, difference,
                (int)cashSales, (int)cashIn, (int)cashOut, true));
        }

        using (var cmd = Query(connection, transaction, """
UPDATE local_cash_shifts
SET status = 'closed', closed_by_user_id = $user, closed_at_utc = $now,
    counted_cash_cents = $counted, expected_cash_cents = $expected,
    difference_cents = $difference
WHERE id = $shift AND tenant_id = $tenant AND store_id = $store
  AND terminal_id = $terminal AND status = 'open';
""", ("$user", userId.ToString()), ("$now", closedAtUtc.ToString("O")),
               ("$counted", request.CountedCashCents), ("$expected", expected),
               ("$difference", difference), ("$shift", request.ShiftId.ToString()),
               ("$tenant", tenantId.ToString()), ("$store", storeId.ToString()),
               ("$terminal", terminalId.ToString())))
        {
            if (cmd.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Open shift could not be atomically closed.");
        }

        using (var cmd = Query(connection, transaction, """
INSERT INTO local_audit_events
(id, tenant_id, store_id, user_id, session_id, event_type, message, occurred_at_utc)
VALUES ($id, $tenant, $store, $user, $session, 'local.cash_shift.closed', $message, $now);
""", ("$id", Guid.NewGuid().ToString()), ("$tenant", tenantId.ToString()),
               ("$store", storeId.ToString()), ("$user", userId.ToString()),
               ("$session", request.SessionId.ToString()),
               ("$message", $"shift={request.ShiftId:N};expected={expected};counted={request.CountedCashCents};difference={difference}"),
               ("$now", closedAtUtc.ToString("O"))))
            cmd.ExecuteNonQuery();

        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
        return Task.FromResult(new AtomicCashShiftCloseResult(
            request.ShiftId, expected, request.CountedCashCents, difference,
            (int)cashSales, (int)cashIn, (int)cashOut, false));
    }

    private static long Count(SqliteConnection connection, SqliteTransaction transaction,
        string sql, params (string Key, object? Value)[] parameters)
    {
        using var cmd = Query(connection, transaction, sql, parameters);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static SqliteCommand Query(SqliteConnection connection, SqliteTransaction transaction,
        string sql, params (string Key, object? Value)[] parameters)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        foreach (var (key, value) in parameters)
            cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return cmd;
    }
}
