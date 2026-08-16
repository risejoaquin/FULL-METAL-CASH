using Microsoft.Extensions.Configuration;
using Npgsql;
using SolidPOS.PosServer.Application.Sales;
using SolidPOS.PosServer.Contracts.Sales;
using SolidPOS.PosServer.Infrastructure.Sales;
using Xunit;

namespace SolidPOS.PosServer.IntegrationTests.PostgreSql;

public sealed class SaleReadModelIntegrationTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StoreId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TerminalId = Guid.Parse("44444444-4444-4444-4444-444444444446");
    private static readonly Guid LatteProductId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid OatModifierId = Guid.Parse("51000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task Sale_detail_and_receipt_read_models_reconstruct_completed_sale()
    {
        if (!PostgreSqlIntegrationTestHelper.IsEnabled)
        {
            return;
        }

        await PostgreSqlIntegrationTestHelper.ResetAndApplyMigrationsAsync(CancellationToken.None);
        await SeedRuntimeAsync();

        IConfiguration configuration = CreateConfiguration();
        var repository = new PostgreSqlSalesRepository(configuration);
        DateTimeOffset occurredAt = DateTimeOffset.UtcNow;
        var request = new CreateSaleRequest(
            Guid.NewGuid(),
            UserId,
            null,
            occurredAt,
            occurredAt,
            [new CreateSaleLineRequest(LatteProductId, null, "1", 0, "Read model test", [OatModifierId])],
            [new CreateSalePaymentRequest(Guid.NewGuid(), "cash", 10000, "receipt-test")],
            0);

        SaleResponse? sale = await repository.CreateAsync(TenantId, StoreId, TerminalId, request, CancellationToken.None);
        Assert.NotNull(sale);

        SaleDetailResponse? detail = await repository.GetByIdAsync(TenantId, sale!.Id, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.Equal(sale.Id, detail!.Id);
        Assert.Equal(7300, detail.TotalCents);
        Assert.Equal(2700, detail.ChangeCents);
        SaleDetailLineResponse line = Assert.Single(detail.Lines);
        SaleModifierResponse modifier = Assert.Single(line.Modifiers);
        Assert.Equal(OatModifierId, modifier.Id);
        Assert.Equal("substitute", modifier.InventoryBehavior);
        Assert.Contains(detail.InventoryMovements, x => x.Sku == "ING-AVENA-ML" && x.QuantityDelta == "-250.0000" && x.ModifierBehavior == "substitute");
        Assert.DoesNotContain(detail.InventoryMovements, x => x.Sku == "ING-LECHE-ML");

        IReadOnlyCollection<SaleListItemResponse>? sales = await repository.ListAsync(
            TenantId,
            new SaleListFilters(occurredAt.AddMinutes(-1), occurredAt.AddMinutes(1), StoreId, TerminalId, "completed", 10),
            CancellationToken.None);
        Assert.NotNull(sales);
        SaleListItemResponse listed = Assert.Single(sales!);
        Assert.Equal(sale.Id, listed.Id);
        Assert.Equal(1, listed.LineCount);
        Assert.Equal(1, listed.PaymentCount);

        var receipt = await repository.GetReceiptAsync(TenantId, sale.Id, CancellationToken.None);
        Assert.NotNull(receipt);
        Assert.Equal(sale.Id, receipt!.SaleId);
        Assert.Equal("SolidPOS Demo Cafe", receipt.TenantName);
        Assert.Equal("Main Store", receipt.StoreName);
        Assert.Equal(7300, receipt.TotalCents);
        Assert.Equal(2700, receipt.ChangeCents);
        Assert.Single(receipt.Lines);
        Assert.Single(receipt.Lines.First().Modifiers);
        Assert.Single(receipt.Payments);
    }

    private static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = PostgreSqlIntegrationTestHelper.ConnectionString
            })
            .Build();
    }

    private static async Task SeedRuntimeAsync()
    {
        await using var connection = new NpgsqlConnection(PostgreSqlIntegrationTestHelper.ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        string devSeedPath = Path.Combine(PostgreSqlIntegrationTestHelper.RepositoryRoot, "database", "postgresql", "004_seed_dev_auth.sql");
        string devSeedSql = await File.ReadAllTextAsync(devSeedPath, CancellationToken.None);
        await PostgreSqlIntegrationTestHelper.ExecuteSqlAsync(connection, devSeedSql, CancellationToken.None);

        string setupSql = $$"""
            SET search_path TO pos, public;
            SELECT set_config('app.tenant_id', '{{TenantId}}', true);

            INSERT INTO terminals (id, tenant_id, store_id, name, fingerprint, status)
            VALUES ('{{TerminalId}}', '{{TenantId}}', '{{StoreId}}', 'Sale Read Model Terminal', 'sale-read-model-terminal', 'active');

            INSERT INTO cash_shifts (tenant_id, store_id, terminal_id, opened_by_user_id, status, opening_amount_cents, expected_cash_cents)
            VALUES ('{{TenantId}}', '{{StoreId}}', '{{TerminalId}}', '{{UserId}}', 'open', 10000, 10000);
            """;
        await PostgreSqlIntegrationTestHelper.ExecuteSqlAsync(connection, setupSql, CancellationToken.None);
    }
}
