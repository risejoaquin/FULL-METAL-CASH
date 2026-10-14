using Microsoft.Data.Sqlite;
using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Auth;
using SolidPOS.PosCore.Application.OfflineSales;
using SolidPOS.PosCore.Domain;
using SolidPOS.PosCore.Infrastructure.SQLite;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

public sealed class SQLiteAtomicCashCheckoutTests
{
    [Fact]
    public async Task Cash_checkout_commits_sale_lines_payment_cash_outbox_receipt_and_audit_together()
    {
        using var f = await Fixture.CreateAsync();
        var result = await f.Checkout.CheckoutAsync(f.Request);
        Assert.False(result.AlreadyCommitted);
        Assert.Equal(2000, result.ChangeCents);
        Assert.Equal(1, f.Count("offline_sales"));
        Assert.Equal(1, f.Count("local_checkout_lines"));
        Assert.Equal(1, f.Count("local_sale_payments"));
        Assert.Equal(1, f.Count("local_outbox_events"));
        Assert.Equal(1, f.Count("local_print_jobs"));
        Assert.Equal(1, f.Count("local_checkout_commits"));
        Assert.Equal(1, f.Count("local_audit_events", "event_type='local.checkout.cash_committed'"));
        Assert.Equal(9000, f.Amount("local_cash_movements", "movement_type='sale_cash'"));
        Assert.Equal(9000, (await f.Repository.GetLocalCashShiftSummaryAsync(f.ShiftId)).CashSalesCents);
        Assert.Single(await f.Repository.GetPendingOutboxEventsAsync(10));
        Assert.NotNull(await f.Repository.GetNextPendingReceiptPrintJobAsync());
    }

    [Fact]
    public async Task Repeated_sale_id_returns_original_commit_without_double_charging_after_reopen()
    {
        using var f = await Fixture.CreateAsync();
        var first = await f.Checkout.CheckoutAsync(f.Request);
        var reopened = new AtomicCashCheckoutService(
            new SQLiteLocalPosRepository(new SQLiteLocalDatabase(f.Path)),
            new SQLiteAtomicCashCheckoutStore(new SQLiteLocalDatabase(f.Path)),
            f.Clock);
        var second = await reopened.CheckoutAsync(f.Request with
        {
            ReceiptId = Guid.NewGuid(),
            PrintJobId = Guid.NewGuid()
        });
        Assert.True(second.AlreadyCommitted);
        Assert.Equal(first.OutboxEventId, second.OutboxEventId);
        Assert.Equal(first.PrintJobId, second.PrintJobId);
        Assert.Equal(first.ReceiptNumber, second.ReceiptNumber);
        Assert.Equal(1, f.Count("offline_sales"));
        Assert.Equal(1, f.Count("local_print_jobs"));
        Assert.Equal(1, f.Count("local_checkout_commits"));
        Assert.Equal(9000, f.Amount("local_cash_movements", "movement_type='sale_cash'"));
    }

    [Fact]
    public async Task Printing_queue_insert_failure_rolls_back_all_financial_records()
    {
        using var f = await Fixture.CreateAsync();
        using (var db = f.Database.OpenConnection())
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
CREATE TRIGGER test_reject_print BEFORE INSERT ON local_print_jobs
BEGIN SELECT RAISE(ABORT, 'simulated print queue disk failure'); END;
""";
            cmd.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<SqliteException>(() => f.Checkout.CheckoutAsync(f.Request));
        Assert.Equal(0, f.Count("offline_sales"));
        Assert.Equal(0, f.Count("local_sale_payments"));
        Assert.Equal(0, f.Count("local_checkout_lines"));
        Assert.Equal(0, f.Count("local_outbox_events"));
        Assert.Equal(0, f.Count("local_checkout_commits"));
        Assert.Equal(0, f.Count("local_cash_movements", "movement_type='sale_cash'"));
    }

    [Fact]
    public async Task Changed_sale_data_using_same_id_is_rejected_after_commit()
    {
        using var f = await Fixture.CreateAsync();
        await f.Checkout.CheckoutAsync(f.Request);
        var altered = f.Request with { TenderedCents = 12000 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Checkout.CheckoutAsync(altered));
        Assert.Equal(1, f.Count("offline_sales"));
        Assert.Equal(9000, f.Amount("local_cash_movements", "movement_type='sale_cash'"));
    }

    [Fact]
    public async Task Missing_cashier_permission_never_writes_a_sale()
    {
        using var f = await Fixture.CreateAsync();
        using (var db = f.Database.OpenConnection())
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM local_user_permissions WHERE permission_code='sales.create';";
            cmd.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Checkout.CheckoutAsync(f.Request));
        Assert.Equal(0, f.Count("offline_sales"));
    }

    [Fact]
    public async Task Changed_catalog_price_is_rejected_without_financial_writes()
    {
        using var f = await Fixture.CreateAsync();
        using (var db = f.Database.OpenConnection())
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "UPDATE local_catalog_products SET price_cents = 8000;";
            cmd.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Checkout.CheckoutAsync(f.Request));
        Assert.Equal(0, f.Count("offline_sales"));
        Assert.Equal(0, f.Count("local_outbox_events"));
    }

    [Fact]
    public async Task No_open_shift_and_insufficient_tender_are_rejected()
    {
        using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => f.Checkout.CheckoutAsync(f.Request with { TenderedCents = 1 }));
        await f.Repository.CloseLocalCashShiftAsync(f.ShiftId, f.UserId, 5000, f.Clock.UtcNow);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Checkout.CheckoutAsync(f.Request));
        Assert.Equal(0, f.Count("offline_sales"));
    }


    [Fact]
    public async Task Crash_after_financial_commit_reconciles_journal_without_duplicate_money()
    {
        using var f = await Fixture.CreateAsync();
        var journal = new SQLiteCheckoutRecoveryJournal(f.Database);
        journal.Register(f.Request);
        var result = await f.Checkout.CheckoutAsync(f.Request);
        Assert.False(result.AlreadyCommitted);

        var restarted = new SQLiteCheckoutRecoveryJournal(new SQLiteLocalDatabase(f.Path));
        Assert.Equal(f.Request.Sale.LocalSaleId, restarted.FindUnresolved(
            f.Request.Sale.TenantId, f.Request.Sale.StoreId, f.Request.Sale.TerminalId));
        Assert.True(restarted.ReconcileCommitted(f.Request.Sale.LocalSaleId));
        Assert.Null(restarted.FindUnresolved(
            f.Request.Sale.TenantId, f.Request.Sale.StoreId, f.Request.Sale.TerminalId));
        Assert.Equal(1, f.Count("offline_sales"));
        Assert.Equal(1, f.Count("local_sale_payments"));
        Assert.Equal(1, f.Count("local_outbox_events"));
        Assert.Equal(9000, f.Amount("local_cash_movements", "movement_type='sale_cash'"));
    }

    [Fact]
    public async Task Missing_cash_ledger_evidence_keeps_restarted_terminal_blocked()
    {
        using var f = await Fixture.CreateAsync();
        var journal = new SQLiteCheckoutRecoveryJournal(f.Database);
        journal.Register(f.Request);
        await f.Checkout.CheckoutAsync(f.Request);
        using (var db = f.Database.OpenConnection())
        using (var command = db.CreateCommand())
        {
            command.CommandText = "DELETE FROM local_cash_movements WHERE source_id=$sale;";
            command.Parameters.AddWithValue("$sale", f.Request.Sale.LocalSaleId.ToString());
            command.ExecuteNonQuery();
        }
        var restarted = new SQLiteCheckoutRecoveryJournal(new SQLiteLocalDatabase(f.Path));
        Assert.False(restarted.ReconcileCommitted(f.Request.Sale.LocalSaleId));
        Assert.Equal(f.Request.Sale.LocalSaleId, restarted.FindUnresolved(
            f.Request.Sale.TenantId, f.Request.Sale.StoreId, f.Request.Sale.TerminalId));
        Assert.Throws<InvalidOperationException>(() => restarted.Register(
            f.Request with { Sale = f.Request.Sale with { LocalSaleId = Guid.NewGuid() } }));
    }


    [Theory]
    [InlineData("local_sale_payments")]
    [InlineData("local_outbox_events")]
    [InlineData("local_print_jobs")]
    public async Task Missing_checkout_artifact_cannot_be_reconciled(string artifact)
    {
        using var f = await Fixture.CreateAsync();
        var journal = new SQLiteCheckoutRecoveryJournal(f.Database);
        journal.Register(f.Request);
        await f.Checkout.CheckoutAsync(f.Request);
        using (var connection = f.Database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            // Identifiers come exclusively from InlineData in this test file.
            command.CommandText = "DELETE FROM " + artifact + " WHERE " +
                (artifact == "local_outbox_events" ? "event_type='sale.completed'" :
                artifact == "local_print_jobs" ? "sale_id=$sale" : "local_sale_id=$sale");
            command.Parameters.AddWithValue("$sale", f.Request.Sale.LocalSaleId.ToString());
            command.ExecuteNonQuery();
        }
        var restarted = new SQLiteCheckoutRecoveryJournal(new SQLiteLocalDatabase(f.Path));
        Assert.False(restarted.ReconcileCommitted(f.Request.Sale.LocalSaleId));
        Assert.Equal(f.Request.Sale.LocalSaleId, restarted.FindUnresolved(
            f.Request.Sale.TenantId, f.Request.Sale.StoreId, f.Request.Sale.TerminalId));
    }

    [Fact]
    public async Task Supervisor_resolves_no_commit_with_audit_and_stale_worker_is_blocked()
    {
        using var f = await Fixture.CreateAsync();
        var journal = new SQLiteCheckoutRecoveryJournal(f.Database);
        journal.Register(f.Request);
        var supervisor = await f.CreateSupervisorAsync(new[] { "sales.void", "cash.close" });
        var recovery = new SQLiteSupervisedCheckoutRecoveryService(f.Database);
        Assert.Throws<InvalidOperationException>(() =>
            recovery.ResolveUncommitted(f.Request.Sale.LocalSaleId, supervisor.SessionId,
                "Se confirmó que nunca se entregó ni cobró efectivo.",
                "INCIDENT-TEST-2026-001", f.Clock.UtcNow));
        recovery.ResolveUncommitted(f.Request.Sale.LocalSaleId, supervisor.SessionId,
            "Se confirmó que nunca se entregó ni cobró efectivo.",
            "INCIDENT-TEST-2026-001", f.Clock.UtcNow, true);

        Assert.Null(journal.FindUnresolved(f.Request.Sale.TenantId,
            f.Request.Sale.StoreId, f.Request.Sale.TerminalId));
        Assert.Equal(1, f.Count("local_checkout_recovery_resolutions"));
        Assert.Equal(1, f.Count("local_audit_events",
            "event_type='local.checkout.intent_resolved_uncommitted'"));
        Assert.Equal(0, f.Count("offline_sales"));
        Assert.Equal(0, f.Count("local_sale_payments"));
        Assert.Equal(0, f.Count("local_outbox_events"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Checkout.CheckoutAsync(f.Request));
        Assert.Throws<InvalidOperationException>(() => journal.Register(f.Request));
        Assert.Equal(0, f.Amount("local_cash_movements", "movement_type='sale_cash'"));

        var newSale = f.Request with
        {
            Sale = f.Request.Sale with { LocalSaleId = Guid.NewGuid() },
            ReceiptId = Guid.NewGuid(),
            PrintJobId = Guid.NewGuid()
        };
        journal.Register(newSale);
        Assert.Equal(newSale.Sale.LocalSaleId,
            journal.FindUnresolved(newSale.Sale.TenantId, newSale.Sale.StoreId,
                newSale.Sale.TerminalId));
    }

    [Fact]
    public async Task Recovery_requires_both_supervisor_permissions_and_a_second_user()
    {
        using var f = await Fixture.CreateAsync();
        var journal = new SQLiteCheckoutRecoveryJournal(f.Database);
        journal.Register(f.Request);
        var recovery = new SQLiteSupervisedCheckoutRecoveryService(f.Database);
        var limited = await f.CreateSupervisorAsync(new[] { "cash.close" });
        Assert.Throws<InvalidOperationException>(() =>
            recovery.ResolveUncommitted(f.Request.Sale.LocalSaleId, limited.SessionId,
                "Revisé turno y registros y no se recibió efectivo.",
                "INCIDENT-TEST-2026-002", f.Clock.UtcNow, true));
        Assert.Throws<InvalidOperationException>(() =>
            recovery.ResolveUncommitted(f.Request.Sale.LocalSaleId, f.Request.SessionId,
                "No hay venta guardada y se verificó la operación.",
                "INCIDENT-TEST-2026-003", f.Clock.UtcNow, true));
        Assert.NotNull(journal.FindUnresolved(f.Request.Sale.TenantId,
            f.Request.Sale.StoreId, f.Request.Sale.TerminalId));
        Assert.Equal(0, f.Count("local_checkout_recovery_resolutions"));
    }

    [Fact]
    public async Task Supervisor_cannot_resolve_sale_that_already_committed()
    {
        using var f = await Fixture.CreateAsync();
        var journal = new SQLiteCheckoutRecoveryJournal(f.Database);
        journal.Register(f.Request);
        await f.Checkout.CheckoutAsync(f.Request);
        var supervisor = await f.CreateSupervisorAsync(new[] { "sales.void", "cash.close" });
        var recovery = new SQLiteSupervisedCheckoutRecoveryService(f.Database);
        Assert.Throws<InvalidOperationException>(() =>
            recovery.ResolveUncommitted(f.Request.Sale.LocalSaleId, supervisor.SessionId,
                "El cajero reportó un resultado de operación incierto.",
                "INCIDENT-TEST-2026-004", f.Clock.UtcNow, true));
        Assert.Equal(0, f.Count("local_checkout_recovery_resolutions"));
        Assert.Equal(1, f.Count("offline_sales"));
        Assert.Equal(9000, f.Amount("local_cash_movements", "movement_type='sale_cash'"));
    }

    [Fact]
    public async Task Audit_insert_failure_rolls_back_supervised_resolution()
    {
        using var f = await Fixture.CreateAsync();
        var journal = new SQLiteCheckoutRecoveryJournal(f.Database);
        journal.Register(f.Request);
        var supervisor = await f.CreateSupervisorAsync(new[] { "sales.void", "cash.close" });
        using (var db = f.Database.OpenConnection())
        using (var trigger = db.CreateCommand())
        {
            trigger.CommandText = """
CREATE TRIGGER test_reject_supervisor_audit BEFORE INSERT ON local_audit_events
WHEN NEW.event_type='local.checkout.intent_resolved_uncommitted'
BEGIN SELECT RAISE(ABORT, 'simulated audit persistence failure'); END;
""";
            trigger.ExecuteNonQuery();
        }
        var recovery = new SQLiteSupervisedCheckoutRecoveryService(f.Database);
        Assert.Throws<SqliteException>(() =>
            recovery.ResolveUncommitted(f.Request.Sale.LocalSaleId, supervisor.SessionId,
                "La auditoría falló y no se debe liberar caja.",
                "INCIDENT-TEST-2026-005", f.Clock.UtcNow, true));
        Assert.NotNull(journal.FindUnresolved(f.Request.Sale.TenantId,
            f.Request.Sale.StoreId, f.Request.Sale.TerminalId));
        Assert.Equal(0, f.Count("local_checkout_recovery_resolutions"));
    }

    [Fact]
    public async Task Uncommitted_resolution_requires_reason_evidence_and_matching_tenant_store()
    {
        using var f = await Fixture.CreateAsync();
        var journal = new SQLiteCheckoutRecoveryJournal(f.Database);
        journal.Register(f.Request);
        var supervisor = await f.CreateSupervisorAsync(new[] { "sales.void", "cash.close" });
        var recovery = new SQLiteSupervisedCheckoutRecoveryService(f.Database);
        Assert.Throws<InvalidOperationException>(() =>
            recovery.ResolveUncommitted(f.Request.Sale.LocalSaleId, supervisor.SessionId,
                "short", "INCIDENT-2026-007", f.Clock.UtcNow, true));
        Assert.Throws<InvalidOperationException>(() =>
            recovery.ResolveUncommitted(f.Request.Sale.LocalSaleId, supervisor.SessionId,
                "No money was taken and financial logs were checked.", "x", f.Clock.UtcNow, true));

        var externalUser = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var otherStore = Guid.NewGuid();
        await f.Repository.SaveLocalUserAsync(new LocalUser(externalUser, otherTenant,
            otherStore, "external-supervisor@test.invalid", "External Supervisor",
            LocalPasswordHasher.Hash("Other-store-test-password!"),
            "supervisor", true, f.Clock.UtcNow, 12),
            new[] { "sales.void", "cash.close" });
        var outsider = await new LocalAuthService(f.Repository, f.Clock).LoginForTerminalAsync(
            "external-supervisor@test.invalid", "Other-store-test-password!",
            otherTenant, otherStore);
        Assert.Throws<InvalidOperationException>(() =>
            recovery.ResolveUncommitted(f.Request.Sale.LocalSaleId, outsider.SessionId,
                "No funds were physically received according to logs.",
                "INCIDENT-2026-008", f.Clock.UtcNow, true));
        Assert.Equal(0, f.Count("local_checkout_recovery_resolutions"));
        Assert.Equal(f.Request.Sale.LocalSaleId,
            journal.FindUnresolved(f.Request.Sale.TenantId,
                f.Request.Sale.StoreId, f.Request.Sale.TerminalId));
    }

    [Fact]
    public async Task Orphaned_outbox_record_blocks_supervisor_from_claiming_no_sale()
    {
        using var f = await Fixture.CreateAsync();
        var journal = new SQLiteCheckoutRecoveryJournal(f.Database);
        journal.Register(f.Request);
        var supervisor = await f.CreateSupervisorAsync(new[] { "sales.void", "cash.close" });
        await f.Repository.SaveOutboxEventAsync(new LocalOutboxEvent(
            Guid.NewGuid(), f.Request.Sale.TenantId, f.Request.Sale.StoreId,
            f.Request.Sale.TerminalId, "sale.completed", 4, 123456789,
            System.Text.Json.JsonSerializer.Serialize(new { localSaleId = f.Request.Sale.LocalSaleId }),
            LocalOutboxStatus.Pending, f.Clock.UtcNow));
        var recovery = new SQLiteSupervisedCheckoutRecoveryService(f.Database);
        Assert.Throws<InvalidOperationException>(() =>
            recovery.ResolveUncommitted(f.Request.Sale.LocalSaleId, supervisor.SessionId,
                "An outbox record survives and must prevent supervisor release.",
                "INCIDENT-2026-009", f.Clock.UtcNow, true));
        Assert.Equal(0, f.Count("local_checkout_recovery_resolutions"));
        Assert.NotNull(journal.FindUnresolved(f.Request.Sale.TenantId,
            f.Request.Sale.StoreId, f.Request.Sale.TerminalId));
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 13, 18, 0, 0, TimeSpan.Zero);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "solidpos-cashcheckout-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(directory, "terminal.sqlite");
        public SQLiteLocalDatabase Database { get; }
        public SQLiteLocalPosRepository Repository { get; }
        public FixedClock Clock { get; } = new();
        public AtomicCashCheckoutService Checkout { get; }
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid ShiftId { get; } = Guid.NewGuid();
        public AtomicCashCheckoutRequest Request { get; private set; } = null!;

        private Fixture()
        {
            Directory.CreateDirectory(directory);
            Database = new SQLiteLocalDatabase(Path);
            Repository = new SQLiteLocalPosRepository(Database);
            Checkout = new AtomicCashCheckoutService(Repository,
                new SQLiteAtomicCashCheckoutStore(Database), Clock);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture();
            await f.Repository.InitializeAsync();
            Guid tenant = Guid.NewGuid(), store = Guid.NewGuid(), terminal = Guid.NewGuid();
            Guid product = Guid.NewGuid(), saleId = Guid.NewGuid();
            await f.Repository.SaveTerminalBindingAsync(new TerminalBinding(
                tenant, store, terminal, "test-fingerprint", "test-token", f.Clock.UtcNow, 4));
            await f.Repository.SaveCatalogProductsAsync(new[]
            {
                new LocalCatalogProduct(product, null, "SKU-1", "Test product",
                    4500, "MXN", "active", f.Clock.UtcNow, f.Clock.UtcNow)
            }, f.Clock.UtcNow);
            await f.Repository.SaveLocalUserAsync(new LocalUser(
                f.UserId, tenant, store, "cashier@test.invalid", "Cashier",
                LocalPasswordHasher.Hash("Only-for-disposable-unit-tests!"),
                "cashier", true, f.Clock.UtcNow, 12), new[] { "sales.create" });
            var session = await new LocalAuthService(f.Repository, f.Clock)
                .LoginForTerminalAsync("cashier@test.invalid",
                    "Only-for-disposable-unit-tests!", tenant, store);
            await f.Repository.OpenLocalCashShiftAsync(new LocalCashShift(
                f.ShiftId, tenant, store, terminal, f.UserId, f.Clock.UtcNow,
                5000, "open"));
            var draft = new OfflineSaleDraft(saleId, tenant, store, terminal,
                f.Clock.UtcNow,
                new[] { new OfflineSaleLineDraft(product, null, "SKU-1", "Test product", 2, 4500) },
                new[] { new OfflineSalePaymentDraft("cash", 9000, Guid.NewGuid()) },
                "MXN", f.UserId);
            f.Request = new AtomicCashCheckoutRequest(
                draft, session.SessionId, f.ShiftId, 11000, Guid.NewGuid(), Guid.NewGuid());
            return f;
        }

        public async Task<LocalSession> CreateSupervisorAsync(string[] permissions)
        {
            var userId = Guid.NewGuid();
            string email = "supervisor-" + userId.ToString("N") + "@test.invalid";
            await Repository.SaveLocalUserAsync(new LocalUser(
                userId, Request.Sale.TenantId, Request.Sale.StoreId, email, "Test Supervisor",
                LocalPasswordHasher.Hash("Supervisor-test-password!"),
                "supervisor", true, Clock.UtcNow, 12), permissions);
            return await new LocalAuthService(Repository, Clock).LoginForTerminalAsync(
                email, "Supervisor-test-password!", Request.Sale.TenantId, Request.Sale.StoreId);
        }

        public int Count(string table, string? condition = null)
        {
            // Constants from tests only; no user-provided SQL identifiers.
            using var db = Database.OpenConnection();
            using var cmd = db.CreateCommand();
            if (table.StartsWith("local_checkout_", StringComparison.Ordinal))
            {
                using var exists = db.CreateCommand();
                exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
                exists.Parameters.AddWithValue("$name", table);
                if (Convert.ToInt32(exists.ExecuteScalar()) == 0) return 0;
            }
            cmd.CommandText = "SELECT COUNT(*) FROM " + table +
                (condition is null ? "" : " WHERE " + condition);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public int Amount(string table, string condition)
        {
            using var db = Database.OpenConnection();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(amount_cents),0) FROM " + table + " WHERE " + condition;
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }
}
