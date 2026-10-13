using Microsoft.Extensions.Configuration;
using Npgsql;
using SolidPOS.DiscountEngine;
using SolidPOS.PosServer.Contracts.Sales;
using SolidPOS.PosServer.Infrastructure.Sales;
using Xunit;

namespace SolidPOS.PosServer.IntegrationTests.PostgreSql;

/// <summary>
/// Runs ONLY when SOLIDPOS_TEST_POSTGRES points at an isolated disposable DB.
/// Never uses production data. Its CI job provisions a fresh PostgreSQL 16
/// service and filters to this class to avoid concurrent schema resets.
/// </summary>
public sealed class PostgreSqlDiscountCartIntegrationTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Store = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Cashier = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Terminal = Guid.Parse("44444444-4444-4444-4444-444444444447");
    private static readonly Guid Latte = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid Americano = Guid.Parse("30000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task Persisted_cart_discount_uses_verified_prices_and_is_idempotent_across_replay()
    {
        if (!PostgreSqlIntegrationTestHelper.IsEnabled) return;

        await PostgreSqlIntegrationTestHelper.ResetAndApplyMigrationsAsync(CancellationToken.None);
        await using var connection = new NpgsqlConnection(PostgreSqlIntegrationTestHelper.ConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        string seed = await File.ReadAllTextAsync(Path.Combine(
            PostgreSqlIntegrationTestHelper.RepositoryRoot, "database", "postgresql",
            "004_seed_dev_auth.sql"));
        await PostgreSqlIntegrationTestHelper.ExecuteSqlAsync(connection, seed, CancellationToken.None);

        Guid discountId = Guid.NewGuid();
        string setup = $$"""
SET search_path TO pos, public;
SELECT set_config('app.tenant_id', '{{Tenant}}', false);

INSERT INTO terminals (id, tenant_id, store_id, name, fingerprint, status)
VALUES ('{{Terminal}}', '{{Tenant}}', '{{Store}}', 'Cart discount fixture', 'cart-discount-fixture', 'active');

INSERT INTO cash_shifts
 (tenant_id, store_id, terminal_id, opened_by_user_id, status, opening_amount_cents, expected_cash_cents)
VALUES ('{{Tenant}}', '{{Store}}', '{{Terminal}}', '{{Cashier}}', 'open', 10000, 10000);

INSERT INTO discounts (id, tenant_id, code, name, discount_type, value, store_id, status)
VALUES ('{{discountId}}', '{{Tenant}}', 'CART-1000', 'Cart fixed fixture',
        'fixed_amount', 1000, '{{Store}}', 'active');
""";
        await PostgreSqlIntegrationTestHelper.ExecuteSqlAsync(connection, setup, CancellationToken.None);

        Guid saleId = Guid.NewGuid();
        var allocation = DiscountCalculator.Quote(new DiscountQuoteRequest(
            Tenant, Store, Cashier,
            [new DiscountLine(DiscountCalculator.GetStableLineId(saleId, 0), 6500),
             new DiscountLine(DiscountCalculator.GetStableLineId(saleId, 1), 4500)],
            [new DiscountRule(discountId, "cart", "fixed_amount", 1000)], []));
        Assert.Equal(1000, allocation.DiscountCents);
        Assert.Equal(10000, allocation.NetCents);

        var occurred = DateTimeOffset.UtcNow;
        var lines = new[]
        {
            new CreateSaleLineRequest(Latte, null, "1",
                allocation.Lines[0].CartDiscountCents, "discount fixture", null, discountId),
            new CreateSaleLineRequest(Americano, null, "1",
                allocation.Lines[1].CartDiscountCents, "discount fixture", null, discountId)
        };
        var request = new CreateSaleRequest(saleId, Cashier, null, occurred, occurred,
            lines, [new CreateSalePaymentRequest(Guid.NewGuid(), "cash", 10000, null)], 0);

        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = PostgreSqlIntegrationTestHelper.ConnectionString
            }).Build();
        var sales = new PostgreSqlSalesRepository(config);
        SaleResponse? created = await sales.CreateAsync(Tenant, Store, Terminal, request, CancellationToken.None);
        Assert.NotNull(created);
        Assert.Equal(11000, created.SubtotalCents);
        Assert.Equal(1000, created.DiscountCents);
        Assert.Equal(10000, created.TotalCents);
        Assert.Equal(0, created.ChangeCents);

        var detail = await sales.GetByIdAsync(Tenant, created.Id, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.Equal(10000, detail.TotalCents);
        Assert.Equal(1000, detail.Lines.Sum(x => x.DiscountCents));

        SaleResponse? replay = await sales.CreateAsync(Tenant, Store, Terminal, request, CancellationToken.None);
        Assert.NotNull(replay);
        Assert.Equal(created.Id, replay.Id);

        using (var count = new NpgsqlCommand(
            "SELECT COUNT(*) FROM pos.sales WHERE tenant_id = @tenant AND local_sale_id = @sale;",
            connection))
        {
            count.Parameters.AddWithValue("tenant", Tenant);
            count.Parameters.AddWithValue("sale", saleId);
            Assert.Equal(1L, Convert.ToInt64(await count.ExecuteScalarAsync()));
        }

        // Same total reduction, altered per-line allocations: must reject
        // before any payment, inventory movement, or receipt is committed.
        var tampered = request with
        {
            LocalSaleId = Guid.NewGuid(),
            Lines =
            [
                lines[0] with { DiscountCents = lines[0].DiscountCents + 1 },
                lines[1] with { DiscountCents = lines[1].DiscountCents - 1 }
            ]
        };
        Assert.Null(await sales.CreateAsync(
            Tenant, Store, Terminal, tampered, CancellationToken.None));

        // An unrelated tenant cannot reuse this terminal's open cash shift.
        Assert.Null(await sales.CreateAsync(
            Guid.NewGuid(), Store, Terminal, request with { LocalSaleId = Guid.NewGuid() },
            CancellationToken.None));
    }
}
