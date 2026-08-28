using Microsoft.Extensions.Configuration;
using Npgsql;
using SolidPOS.PosServer.Contracts.Provisioning;
using SolidPOS.PosServer.Infrastructure.Provisioning;
using Xunit;

namespace SolidPOS.PosServer.IntegrationTests.PostgreSql;

public sealed class ProductionProvisioningIsolationTests
{
    [Fact]
    public async Task Bootstrap_idempotency_key_rejects_changed_payload_without_creating_second_tenant()
    {
        if (!PostgreSqlIntegrationTestHelper.IsEnabled)
        {
            return;
        }

        await PostgreSqlIntegrationTestHelper.ResetAndApplyMigrationsAsync(CancellationToken.None);

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = PostgreSqlIntegrationTestHelper.ConnectionString
            })
            .Build();

        var repository = new PostgreSqlProductionProvisioningRepository(configuration);
        string idempotencyKey = $"beta02-{Guid.NewGuid():N}";
        Guid tenantId = Guid.NewGuid();

        var original = new ProductionTenantBootstrapRequest(
            TenantName: "BETA-02 Isolation Tenant",
            AdminEmail: "owner@beta02.local",
            AdminFullName: "Beta Owner",
            AdminPassword: "not-used-by-repository",
            StoreCode: "MAIN",
            StoreName: "Main Store",
            TenantId: tenantId,
            IdempotencyKey: idempotencyKey,
            DisableDemoUser: false);

        ProductionTenantBootstrapResponse? first = await repository.BootstrapTenantAsync(
            original,
            adminPasswordHash: "integration-test-password-hash",
            disableDemoUser: false,
            CancellationToken.None);

        Assert.NotNull(first);
        Assert.False(first!.WasExisting);
        Assert.Equal(tenantId, first.TenantId);

        var changedPayload = original with { StoreName = "Changed Store Name" };
        ProductionTenantBootstrapResponse? changed = await repository.BootstrapTenantAsync(
            changedPayload,
            adminPasswordHash: "another-password-hash",
            disableDemoUser: false,
            CancellationToken.None);

        Assert.Null(changed);

        await using var connection = new NpgsqlConnection(PostgreSqlIntegrationTestHelper.ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        await using var command = new NpgsqlCommand(
            "SELECT (SELECT count(*) FROM pos.tenants WHERE id = @tenant_id), (SELECT count(*) FROM pos.production_bootstrap_runs WHERE idempotency_key = @key);",
            connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("key", idempotencyKey);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken.None);
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
    }
}
