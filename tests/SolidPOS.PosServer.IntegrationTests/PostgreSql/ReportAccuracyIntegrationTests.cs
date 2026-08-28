using Microsoft.Extensions.Configuration;
using Npgsql;
using SolidPOS.PosServer.Application.Reports;
using SolidPOS.PosServer.Contracts.Sales;
using SolidPOS.PosServer.Infrastructure.Reports;
using SolidPOS.PosServer.Infrastructure.Sales;
using Xunit;

namespace SolidPOS.PosServer.IntegrationTests.PostgreSql;

public sealed class ReportAccuracyIntegrationTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StoreId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TerminalId = Guid.Parse("44444444-4444-4444-4444-444444444445");
    private static readonly Guid LatteProductId = Guid.Parse("30000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Cash_payment_report_subtracts_change_and_sales_range_separates_net_from_total()
    {
        if (!PostgreSqlIntegrationTestHelper.IsEnabled)
        {
            return;
        }

        await PostgreSqlIntegrationTestHelper.ResetAndApplyMigrationsAsync(CancellationToken.None);
        await SeedRuntimeAsync();

        IConfiguration configuration = CreateConfiguration();
        var salesRepository = new PostgreSqlSalesRepository(configuration);
        DateTimeOffset occurredAt = DateTimeOffset.UtcNow;
        var request = new CreateSaleRequest(
            Guid.NewGuid(),
            UserId,
            null,
            occurredAt,
            occurredAt,
            [new CreateSaleLineRequest(LatteProductId, null, "1", 0, null, null)],
            [new CreateSalePaymentRequest(Guid.NewGuid(), "cash", 10000, null)],
            0);

        SaleResponse? sale = await salesRepository.CreateAsync(TenantId, StoreId, TerminalId, request, CancellationToken.None);
        Assert.NotNull(sale);
        Assert.Equal(6500, sale.TotalCents);
        Assert.Equal(3500, sale.ChangeCents);

        var reportsRepository = new PostgreSqlReportsRepository(configuration);
        var filters = new ReportDateRangeFilters(StoreId, occurredAt.AddMinutes(-1), occurredAt.AddMinutes(1));

        var paymentMethods = await reportsRepository.GetSalesByPaymentMethodAsync(TenantId, filters, CancellationToken.None);
        var cash = Assert.Single(paymentMethods);
        Assert.Equal("cash", cash.MethodType);
        Assert.Equal(6500, cash.TotalCents);

        var salesRange = await reportsRepository.GetSalesRangeAsync(TenantId, filters, CancellationToken.None);
        Assert.Equal(6500, salesRange.GrossSalesCents);
        Assert.Equal(6500, salesRange.NetSalesCents);
        Assert.Equal(6500, salesRange.TotalSalesCents);
        Assert.Equal(6500, salesRange.AverageTicketCents);
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
            VALUES ('{{TerminalId}}', '{{TenantId}}', '{{StoreId}}', 'Reports Test Terminal', 'reports-test-terminal', 'active');

            INSERT INTO cash_shifts (tenant_id, store_id, terminal_id, opened_by_user_id, status, opening_amount_cents, expected_cash_cents)
            VALUES ('{{TenantId}}', '{{StoreId}}', '{{TerminalId}}', '{{UserId}}', 'open', 10000, 10000);
            """;
        await PostgreSqlIntegrationTestHelper.ExecuteSqlAsync(connection, setupSql, CancellationToken.None);
    }
}
