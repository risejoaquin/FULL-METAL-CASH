using System.Text.Json;
using SolidPOS.PosServer.Contracts.Provisioning;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Provisioning;

public sealed class ProductionProvisioningContractTests
{
    [Fact]
    public void Production_bootstrap_request_round_trips_core_fields()
    {
        var request = new ProductionTenantBootstrapRequest(
            TenantName: "Demo Cafe",
            AdminEmail: "owner@example.com",
            AdminFullName: "Owner User",
            AdminPassword: "StrongAdmin123!",
            StoreCode: "MAIN",
            StoreName: "Main Store",
            IdempotencyKey: "demo-cafe-bootstrap");

        string json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        ProductionTenantBootstrapRequest? parsed = JsonSerializer.Deserialize<ProductionTenantBootstrapRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(parsed);
        Assert.Equal("Demo Cafe", parsed!.TenantName);
        Assert.Equal("owner@example.com", parsed.AdminEmail);
        Assert.Equal("MAIN", parsed.StoreCode);
        Assert.Equal("demo-cafe-bootstrap", parsed.IdempotencyKey);
        Assert.True(parsed.DisableDemoUser);
    }

    [Fact]
    public void Production_bootstrap_response_marks_idempotent_result()
    {
        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid adminUserId = Guid.NewGuid();

        var response = new ProductionTenantBootstrapResponse(
            tenantId,
            storeId,
            adminUserId,
            "Demo Cafe",
            "owner@example.com",
            "MAIN",
            WasExisting: true,
            DemoUserDisabled: false,
            Message: "already completed");

        Assert.True(response.WasExisting);
        Assert.Equal(tenantId, response.TenantId);
        Assert.Equal(storeId, response.StoreId);
        Assert.Equal(adminUserId, response.AdminUserId);
    }
}
