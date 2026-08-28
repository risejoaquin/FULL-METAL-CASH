using System.Text.Json;
using SolidPOS.PosServer.Contracts.Tenants;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Tenants;

public sealed class TenantConfigContractTests
{
    [Fact]
    public void Tenant_config_response_can_represent_qsr_mvp_modules()
    {
        JsonElement modules = JsonDocument.Parse("""
            {
              "modifiers": true,
              "recipes_bom": true,
              "barcode": true,
              "cash": true,
              "sync": true
            }
            """).RootElement.Clone();

        TenantConfigResponse response = new(
            Guid.NewGuid(),
            "qsr_cafe",
            "touch_grid",
            modules,
            JsonDocument.Parse("{}").RootElement.Clone(),
            JsonDocument.Parse("{}").RootElement.Clone(),
            JsonDocument.Parse("{}").RootElement.Clone(),
            JsonDocument.Parse("{}").RootElement.Clone(),
            1,
            DateTimeOffset.UtcNow);

        Assert.Equal("qsr_cafe", response.BusinessVertical);
        Assert.True(response.ModulesEnabled.GetProperty("modifiers").GetBoolean());
        Assert.True(response.ModulesEnabled.GetProperty("recipes_bom").GetBoolean());
        Assert.True(response.ModulesEnabled.GetProperty("barcode").GetBoolean());
        Assert.True(response.ModulesEnabled.GetProperty("cash").GetBoolean());
        Assert.True(response.ModulesEnabled.GetProperty("sync").GetBoolean());
    }
}
