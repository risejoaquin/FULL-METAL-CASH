using SolidPOS.PosServer.Contracts.AdminManagement;
using SolidPOS.PosServer.Contracts.Tenants;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.AdminManagement;

public sealed class AdminManagementContractTests
{
    [Fact]
    public void User_response_preserves_roles_and_store_access()
    {
        Guid tenantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        Guid roleId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();

        var response = new UserResponse(
            userId,
            tenantId,
            "cashier@solidpos.local",
            "Cashier User",
            "active",
            new[] { roleId },
            new[] { "cashier" },
            new[] { storeId },
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        Assert.Equal(userId, response.Id);
        Assert.Contains(roleId, response.RoleIds);
        Assert.Contains("cashier", response.RoleCodes);
        Assert.Contains(storeId, response.StoreIds);
    }

    [Fact]
    public void Tenant_current_response_includes_settings_snapshot()
    {
        Guid tenantId = Guid.NewGuid();
        TenantConfigResponse settings = new(
            tenantId,
            "qsr_cafe",
            "touch_grid",
            System.Text.Json.JsonDocument.Parse("{\"sync\":true}").RootElement.Clone(),
            System.Text.Json.JsonDocument.Parse("{\"name\":\"Demo\"}").RootElement.Clone(),
            System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone(),
            System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone(),
            System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone(),
            2,
            DateTimeOffset.UtcNow);

        var response = new TenantCurrentResponse(
            tenantId,
            "SolidPOS Demo",
            "SolidPOS Demo Legal",
            "active",
            "America/Hermosillo",
            "MXN",
            settings,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        Assert.Equal(tenantId, response.Id);
        Assert.Equal("qsr_cafe", response.Settings.BusinessVertical);
        Assert.Equal(2, response.Settings.Version);
    }
}
