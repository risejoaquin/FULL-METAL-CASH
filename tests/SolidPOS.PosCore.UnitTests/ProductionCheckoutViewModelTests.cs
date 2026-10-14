using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Auth;
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

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(
            Path.GetTempPath(), "solidpos-wpf-commercial-" + Guid.NewGuid().ToString("N"));
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

        public ProductionCheckoutViewModel CreateCheckout(ProductionCartViewModel cart)
            => new(Repository, Auth, Login, cart,
                new AtomicCashCheckoutService(Repository,
                    new SQLiteAtomicCashCheckoutStore(Database), clock), clock);

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }
}
