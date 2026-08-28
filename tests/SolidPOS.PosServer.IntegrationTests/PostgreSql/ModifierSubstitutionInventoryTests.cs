using Microsoft.Extensions.Configuration;
using Npgsql;
using SolidPOS.PosServer.Contracts.Sales;
using SolidPOS.PosServer.Infrastructure.Sales;
using Xunit;

namespace SolidPOS.PosServer.IntegrationTests.PostgreSql;

public sealed class ModifierSubstitutionInventoryTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StoreId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TerminalId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid LatteProductId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid OatModifierId = Guid.Parse("51000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task Latte_with_oat_milk_replaces_base_milk_instead_of_double_consuming()
    {
        if (!PostgreSqlIntegrationTestHelper.IsEnabled)
        {
            return;
        }

        await PostgreSqlIntegrationTestHelper.ResetAndApplyMigrationsAsync(CancellationToken.None);

        await using (var connection = new NpgsqlConnection(PostgreSqlIntegrationTestHelper.ConnectionString))
        {
            await connection.OpenAsync(CancellationToken.None);
            string devSeedPath = Path.Combine(PostgreSqlIntegrationTestHelper.RepositoryRoot, "database", "postgresql", "004_seed_dev_auth.sql");
            string devSeedSql = await File.ReadAllTextAsync(devSeedPath, CancellationToken.None);
            await PostgreSqlIntegrationTestHelper.ExecuteSqlAsync(connection, devSeedSql, CancellationToken.None);

            string setupSql = $$"""
                SET search_path TO pos, public;
                SELECT set_config('app.tenant_id', '{{TenantId}}', true);

                INSERT INTO terminals (id, tenant_id, store_id, name, fingerprint, status)
                VALUES ('{{TerminalId}}', '{{TenantId}}', '{{StoreId}}', 'Inventory Test Terminal', 'inventory-test-terminal', 'active');

                INSERT INTO cash_shifts (tenant_id, store_id, terminal_id, opened_by_user_id, status, opening_amount_cents, expected_cash_cents)
                VALUES ('{{TenantId}}', '{{StoreId}}', '{{TerminalId}}', '{{UserId}}', 'open', 10000, 10000);
                """;
            await PostgreSqlIntegrationTestHelper.ExecuteSqlAsync(connection, setupSql, CancellationToken.None);
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = PostgreSqlIntegrationTestHelper.ConnectionString
            })
            .Build();

        var repository = new PostgreSqlSalesRepository(configuration);
        Guid localSaleId = Guid.NewGuid();
        DateTimeOffset occurredAt = DateTimeOffset.UtcNow;
        var request = new CreateSaleRequest(
            localSaleId,
            UserId,
            null,
            occurredAt,
            occurredAt,
            [new CreateSaleLineRequest(LatteProductId, null, "1", 0, null, [OatModifierId])],
            [new CreateSalePaymentRequest(Guid.NewGuid(), "cash", 7300, null)],
            0);

        SaleResponse? sale = await repository.CreateAsync(TenantId, StoreId, TerminalId, request, CancellationToken.None);
        Assert.NotNull(sale);

        await using var verifyConnection = new NpgsqlConnection(PostgreSqlIntegrationTestHelper.ConnectionString);
        await verifyConnection.OpenAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand($$"""
            SET search_path TO pos, public;
            SELECT set_config('app.tenant_id', '{{TenantId}}', true);

            SELECT p.sku, SUM(il.quantity_delta) AS quantity_delta
            FROM inventory_ledger il
            JOIN products p ON p.tenant_id = il.tenant_id AND p.id = il.product_id
            WHERE il.tenant_id = '{{TenantId}}'
              AND il.reference_type = 'sale'
              AND il.reference_id = '{{sale!.Id}}'
            GROUP BY p.sku
            ORDER BY p.sku;
            """, verifyConnection);

        Dictionary<string, decimal> effects = new(StringComparer.OrdinalIgnoreCase);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken.None);
        do
        {
            while (await reader.ReadAsync(CancellationToken.None))
            {
                effects[reader.GetString(0)] = reader.GetDecimal(1);
            }
        }
        while (await reader.NextResultAsync(CancellationToken.None));

        Assert.Equal(-18m, effects["ING-CAFE-G"]);
        Assert.Equal(-250m, effects["ING-AVENA-ML"]);
        Assert.Equal(-1m, effects["ING-VASO-12"]);
        Assert.False(effects.ContainsKey("ING-LECHE-ML"));
    }
}
