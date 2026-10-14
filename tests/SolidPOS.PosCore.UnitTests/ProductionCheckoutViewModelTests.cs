using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Auth;
using SolidPOS.PosCore.Application.Cash;
using SolidPOS.PosCore.Application.OfflineSales;
using SolidPOS.PosCore.Application.Startup;
using SolidPOS.PosCore.Domain;
using SolidPOS.PosCore.Infrastructure.SQLite;
using SolidPOS.PosCore.Wpf.ViewModels;
using Microsoft.Data.Sqlite;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

/// <summary>Commercial WPF ViewModels, real disposable SQLite; no remote, demo or hardware.</summary>
public sealed class ProductionCheckoutViewModelTests
{
    [Fact]
    public async Task Wpf_login_open_cash_cart_and_checkout_are_persisted_in_SQLite()
    {
        using var f = await Fixture.CreateAsync(new[] { "cash.open", "sales.create" });
        await f.Login.LoginAsync("Test-local-password-2026!");
        Assert.True(f.Login.IsAuthenticated);
        var cart = new ProductionCartViewModel(f.Repository, f.Login) { Sku = "SKU-QA-1" };
        await cart.AddBySkuAsync();
        Assert.Equal(4500, cart.TotalCents);

        var checkout = f.CreateCheckout(cart);
        checkout.OpeningAmountCents = 10000;
        await checkout.OpenShiftAsync();
        Assert.NotNull(await f.Repository.GetOpenLocalCashShiftAsync());
        checkout.TenderedCents = 5000;
        await checkout.CheckoutCashAsync();

        Assert.Empty(cart.Lines);
        Assert.Contains("VENTA GUARDADA", checkout.SaleStatus);
        Assert.Contains("Sin ACK remoto", checkout.SaleStatus);
        Assert.Equal(1, await f.Repository.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
        var shift = await f.Repository.GetOpenLocalCashShiftAsync();
        Assert.NotNull(shift);
        Assert.Equal(4500, (await f.Repository.GetLocalCashShiftSummaryAsync(shift.Id)).CashSalesCents);

        var reopened = new SQLiteLocalPosRepository(new SQLiteLocalDatabase(f.Path));
        Assert.Equal(1, await reopened.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
        Assert.Equal(4500, (await reopened.GetLocalCashShiftSummaryAsync(shift.Id)).CashSalesCents);
    }

    [Fact]
    public async Task Cash_open_is_denied_without_authorized_permission()
    {
        using var f = await Fixture.CreateAsync(new[] { "sales.create" });
        await f.Login.LoginAsync("Test-local-password-2026!");
        var cart = new ProductionCartViewModel(f.Repository, f.Login) { Sku = "SKU-QA-1" };
        await cart.AddBySkuAsync();
        var checkout = f.CreateCheckout(cart);
        await checkout.OpenShiftAsync();

        Assert.Null(await f.Repository.GetOpenLocalCashShiftAsync());
        Assert.False(checkout.CanCheckout);
        Assert.Equal(0, await f.Repository.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
    }

    [Fact]
    public async Task Locked_checkout_cart_rejects_new_sku_without_mutating_total()
    {
        using var f = await Fixture.CreateAsync(new[] { "cash.open", "sales.create" });
        await f.Login.LoginAsync("Test-local-password-2026!");
        var cart = new ProductionCartViewModel(f.Repository, f.Login) { Sku = "SKU-QA-1" };
        await cart.AddBySkuAsync();
        Assert.Equal(4500, cart.TotalCents);
        cart.SetCheckoutLocked(true);
        cart.Sku = "SKU-QA-1";

        await cart.AddBySkuAsync();

        Assert.Single(cart.Lines);
        Assert.Equal(1, cart.Lines[0].Quantity);
        Assert.Equal(4500, cart.TotalCents);
        Assert.False(cart.CanEdit);
        cart.SetCheckoutLocked(false);
        Assert.True(cart.CanEdit);
    }

    [Fact]
    public async Task Wpf_cash_close_reconciles_physical_count_and_blocks_future_checkout()
    {
        using var f = await Fixture.CreateAsync(new[] { "cash.open", "sales.create", "cash.close" });
        await f.Login.LoginAsync("Test-local-password-2026!");
        var cart = new ProductionCartViewModel(f.Repository, f.Login) { Sku = "SKU-QA-1" };
        await cart.AddBySkuAsync();
        var checkout = f.CreateCheckout(cart);
        checkout.OpeningAmountCents = 10000;
        checkout.TenderedCents = 5000;
        await checkout.OpenShiftAsync();
        var originalShift = await f.Repository.GetOpenLocalCashShiftAsync();
        Assert.NotNull(originalShift);
        await checkout.CheckoutCashAsync();
        checkout.CountedCashCents = 14000;
        Assert.True(checkout.CanCloseShift);
        await checkout.CloseShiftAsync();

        Assert.Contains("CIERRE GUARDADO", checkout.CashStatus);
        Assert.False(checkout.CanCheckout);
        Assert.Null(await f.Repository.GetOpenLocalCashShiftAsync());
        var shift = await f.Repository.GetLocalCashShiftAsync(originalShift.Id);
        Assert.NotNull(shift);
        Assert.Equal("closed", shift.Status);
        Assert.Equal(14500, shift.ExpectedCashCents);
        Assert.Equal(14000, shift.CountedCashCents);
        Assert.Equal(-500, shift.DifferenceCents);
        using var db = f.Database.OpenConnection();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM local_audit_events WHERE event_type='local.cash_shift.closed'";
        Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
        var reopened = new SQLiteLocalPosRepository(new SQLiteLocalDatabase(f.Path));
        Assert.Equal("closed", (await reopened.GetLocalCashShiftAsync(originalShift.Id))?.Status);
    }

    [Fact]
    public async Task Wpf_close_without_cash_close_permission_keeps_turn_open()
    {
        using var f = await Fixture.CreateAsync(new[] { "cash.open", "sales.create" });
        await f.Login.LoginAsync("Test-local-password-2026!");
        var cart = new ProductionCartViewModel(f.Repository, f.Login);
        var checkout = f.CreateCheckout(cart);
        checkout.OpeningAmountCents = 5000;
        checkout.CountedCashCents = 5000;
        await checkout.OpenShiftAsync();
        await checkout.CloseShiftAsync();
        Assert.NotNull(await f.Repository.GetOpenLocalCashShiftAsync());
        Assert.DoesNotContain("CIERRE GUARDADO", checkout.CashStatus);
    }

    [Fact]
    public async Task Close_service_replay_is_idempotent_but_changed_physical_count_is_rejected()
    {
        using var f = await Fixture.CreateAsync(new[] { "cash.open", "cash.close" });
        await f.Login.LoginAsync("Test-local-password-2026!");
        var cart = new ProductionCartViewModel(f.Repository, f.Login);
        var checkout = f.CreateCheckout(cart);
        checkout.OpeningAmountCents = 7500;
        await checkout.OpenShiftAsync();
        var original = await f.Repository.GetOpenLocalCashShiftAsync();
        Assert.NotNull(original);
        var session = f.Login.ActiveSession!;
        var request = new AtomicCashShiftCloseRequest(original.Id, session.SessionId, 7400);
        var first = await f.CreateCashCloseService().CloseAsync(request);
        var reopenClose = new AtomicCashShiftCloseService(
            new SQLiteAtomicCashShiftCloseStore(new SQLiteLocalDatabase(f.Path)), new SystemClock());
        var second = await reopenClose.CloseAsync(request);

        Assert.False(first.AlreadyClosed);
        Assert.True(second.AlreadyClosed);
        Assert.Equal(-100, first.DifferenceCents);
        Assert.Equal(first.ExpectedCashCents, second.ExpectedCashCents);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => reopenClose.CloseAsync(request with { CountedCashCents = 7500 }));
        using var db = f.Database.OpenConnection();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM local_audit_events WHERE event_type='local.cash_shift.closed'";
        Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public async Task Ledger_tampering_and_audit_write_failure_prevent_closure()
    {
        using var f = await Fixture.CreateAsync(new[] { "cash.open", "sales.create", "cash.close" });
        await f.Login.LoginAsync("Test-local-password-2026!");
        var cart = new ProductionCartViewModel(f.Repository, f.Login) { Sku = "SKU-QA-1" };
        await cart.AddBySkuAsync();
        var checkout = f.CreateCheckout(cart);
        checkout.OpeningAmountCents = 1000;
        checkout.TenderedCents = 5000;
        await checkout.OpenShiftAsync();
        await checkout.CheckoutCashAsync();
        var shift = await f.Repository.GetOpenLocalCashShiftAsync();
        Assert.NotNull(shift);
        using (var db = f.Database.OpenConnection())
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "UPDATE local_cash_movements SET amount_cents=amount_cents+1 WHERE movement_type='sale_cash'";
            cmd.ExecuteNonQuery();
        }
        var request = new AtomicCashShiftCloseRequest(shift.Id, f.Login.ActiveSession!.SessionId, 5500);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.CreateCashCloseService().CloseAsync(request));
        Assert.Equal("open", (await f.Repository.GetLocalCashShiftAsync(shift.Id))?.Status);

        using (var db = f.Database.OpenConnection())
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
UPDATE local_cash_movements SET amount_cents=amount_cents-1 WHERE movement_type='sale_cash';
CREATE TRIGGER reject_shift_audit BEFORE INSERT ON local_audit_events
WHEN NEW.event_type='local.cash_shift.closed'
BEGIN SELECT RAISE(ABORT,'test audit unavailable'); END;
""";
            cmd.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<SqliteException>(() => f.CreateCashCloseService().CloseAsync(request));
        Assert.Equal("open", (await f.Repository.GetLocalCashShiftAsync(shift.Id))?.Status);
        Assert.Null((await f.Repository.GetLocalCashShiftAsync(shift.Id))?.ClosedAtUtc);
    }

    [Fact]
    public async Task Reopened_commercial_shell_reconciles_persisted_checkout_and_unlocks_cart()
    {
        using var f = await Fixture.CreateAsync(new[] { "cash.open", "sales.create" });
        await f.Login.LoginAsync("Test-local-password-2026!");
        var cart = new ProductionCartViewModel(f.Repository, f.Login) { Sku = "SKU-QA-1" };
        await cart.AddBySkuAsync();
        var initial = f.CreateCheckout(cart);
        await initial.OpenShiftAsync();
        var shift = (await f.Repository.GetOpenLocalCashShiftAsync())!;
        var binding = (await f.Repository.GetTerminalBindingAsync())!;
        var product = (await f.Repository.GetCatalogProductBySkuAsync("SKU-QA-1"))!;
        var sale = new OfflineSaleDraft(Guid.NewGuid(), binding.TenantId, binding.StoreId,
            binding.TerminalId, DateTimeOffset.UtcNow,
            new[] { new OfflineSaleLineDraft(product.ProductId, product.VariantId,
                product.Sku, product.Name, 1, product.PriceCents) },
            new[] { new OfflineSalePaymentDraft("cash", product.PriceCents, Guid.NewGuid()) },
            "MXN", f.Login.ActiveSession!.UserId);
        var request = new AtomicCashCheckoutRequest(sale, f.Login.ActiveSession.SessionId,
            shift.Id, product.PriceCents, Guid.NewGuid(), Guid.NewGuid());
        var journal = new SQLiteCheckoutRecoveryJournal(f.Database);
        journal.Register(request);
        await new AtomicCashCheckoutService(f.Repository, new SQLiteAtomicCashCheckoutStore(f.Database),
            new SystemClock()).CheckoutAsync(request);
        cart.SetCheckoutLocked(true);
        var reopened = f.CreateCheckout(cart,
            new SQLiteCheckoutRecoveryJournal(new SQLiteLocalDatabase(f.Path)));
        await reopened.RefreshShiftAsync();

        Assert.False(reopened.HasUncertainCheckout);
        Assert.True(cart.CanEdit);
        Assert.Contains("verificada", reopened.SaleStatus);
        Assert.Null(journal.FindUnresolved(binding.TenantId, binding.StoreId, binding.TerminalId));
        Assert.Equal(4500, (await f.Repository.GetLocalCashShiftSummaryAsync(shift.Id)).CashSalesCents);
        Assert.Equal(1, await f.Repository.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
    }

    [Fact]
    public async Task Independent_supervisor_can_resolve_uncommitted_intent_via_WPF_without_sale()
    {
        using var f = await Fixture.CreateAsync(new[] { "cash.open", "sales.create" });
        await f.Login.LoginAsync("Test-local-password-2026!");
        var cashier = f.Login.ActiveSession!;
        var binding = (await f.Repository.GetTerminalBindingAsync())!;
        var sale = new OfflineSaleDraft(Guid.NewGuid(), binding.TenantId, binding.StoreId,
            binding.TerminalId, DateTimeOffset.UtcNow,
            new[] { new OfflineSaleLineDraft(Guid.NewGuid(), null, "UNCOMMITTED", "Test", 1, 4500) },
            new[] { new OfflineSalePaymentDraft("cash", 4500, Guid.NewGuid()) },
            "MXN", cashier.UserId);
        var journal = new SQLiteCheckoutRecoveryJournal(f.Database);
        journal.Register(new AtomicCashCheckoutRequest(sale, cashier.SessionId,
            Guid.NewGuid(), 5000, Guid.NewGuid(), Guid.NewGuid()));
        var cart = new ProductionCartViewModel(f.Repository, f.Login);
        var checkout = f.CreateCheckout(cart, journal, new SQLiteSupervisedCheckoutRecoveryService(f.Database));
        await checkout.RefreshShiftAsync();
        Assert.True(checkout.HasUncertainCheckout);
        Assert.False(cart.CanEdit);

        await f.Login.LogoutAsync();
        var supervisor = Guid.NewGuid();
        await f.Repository.SaveLocalUserAsync(new LocalUser(supervisor,
            binding.TenantId, binding.StoreId, "supervisor@qa.invalid", "Supervisor",
            LocalPasswordHasher.Hash("Supervised-recovery-2026!"),
            "supervisor", true, DateTimeOffset.UtcNow, 8),
            new[] { "sales.void", "cash.close" });
        f.Login.Email = "supervisor@qa.invalid";
        await f.Login.LoginAsync("Supervised-recovery-2026!");
        checkout.SessionChanged();
        await checkout.RefreshShiftAsync();
        checkout.RecoveryReason = "Se verificó que no se cobró ni generó venta.";
        checkout.RecoveryEvidence = "INCIDENT-WPF-2026-0001";
        await checkout.ResolveUncommittedAsync();

        Assert.False(checkout.HasUncertainCheckout);
        Assert.True(cart.CanEdit);
        Assert.Contains("resuelto SIN venta", checkout.SaleStatus);
        Assert.Null(journal.FindUnresolved(binding.TenantId, binding.StoreId, binding.TerminalId));
        Assert.Equal(0, await f.Repository.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "solidpos-wpf-commercial-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(directory, "poscore.sqlite");
        public SQLiteLocalDatabase Database { get; }
        public SQLiteLocalPosRepository Repository { get; }
        public LocalAuthService Auth { get; }
        public ProductionLoginViewModel Login { get; private set; } = null!;
        private readonly SystemClock clock = new();

        private Fixture()
        {
            Directory.CreateDirectory(directory);
            Database = new SQLiteLocalDatabase(Path);
            Repository = new SQLiteLocalPosRepository(Database);
            Auth = new LocalAuthService(Repository, clock);
        }

        public static async Task<Fixture> CreateAsync(string[] permissions)
        {
            var f = new Fixture();
            await f.Repository.InitializeAsync();
            Guid tenant = Guid.NewGuid(), store = Guid.NewGuid(), terminal = Guid.NewGuid();
            await f.Repository.SaveTerminalBindingAsync(new TerminalBinding(
                tenant, store, terminal, "wpf-fixture-device", "wpf-fixture-token",
                f.clock.UtcNow, 4));
            await f.Repository.SaveLocalUserAsync(new LocalUser(
                Guid.NewGuid(), tenant, store, "cashier@unit.invalid", "QA Operator",
                LocalPasswordHasher.Hash("Test-local-password-2026!"),
                "cashier", true, f.clock.UtcNow, 8), permissions);
            await f.Repository.SaveCatalogProductsAsync(new[]
            {
                new LocalCatalogProduct(Guid.NewGuid(), null, "SKU-QA-1",
                    "QA Sample", 4500, "MXN", "active", f.clock.UtcNow, f.clock.UtcNow)
            }, f.clock.UtcNow);
            var inspection = await new PosCoreStartupInspectionService(f.Repository).InspectAsync();
            f.Login = new ProductionLoginViewModel(f.Auth, inspection)
            {
                Email = "cashier@unit.invalid"
            };
            return f;
        }

        public ProductionCheckoutViewModel CreateCheckout(ProductionCartViewModel cart,
            SQLiteCheckoutRecoveryJournal? journal = null,
            SQLiteSupervisedCheckoutRecoveryService? supervisorRecovery = null)
            => new(Repository, Auth, Login, cart,
                new AtomicCashCheckoutService(Repository,
                    new SQLiteAtomicCashCheckoutStore(Database), clock), CreateCashCloseService(), clock,
                journal, supervisorRecovery);

        public AtomicCashShiftCloseService CreateCashCloseService()
            => new(new SQLiteAtomicCashShiftCloseStore(Database), clock);

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }
}
