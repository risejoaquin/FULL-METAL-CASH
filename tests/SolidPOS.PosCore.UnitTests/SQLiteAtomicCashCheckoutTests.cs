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
    public async Task Unbound_terminal_or_wrong_permission_never_writes_a_sale()
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

        public int Count(string table, string? condition = null)
        {
            // Constants from tests only; no user-provided SQL identifiers.
            using var db = Database.OpenConnection();
            using var cmd = db.CreateCommand();
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
