using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Sync;
using SolidPOS.PosServer.Application.Tenants;
using SolidPOS.PosServer.Contracts.Tenants;
using SolidPOS.PosServer.Infrastructure.Tenants;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Tenants;

public sealed class TenantConfigServiceTests
{
    [Fact]
    public async Task Update_writes_tenant_config_sync_change()
    {
        Guid tenantId = Guid.NewGuid();
        Guid actorTerminalId = Guid.NewGuid();
        UpdateTenantConfigRequest request = CreateRequest();
        TenantConfigResponse updated = new(
            tenantId,
            request.BusinessVertical,
            request.UiLayout,
            request.ModulesEnabled,
            request.Branding,
            request.ReceiptSettings,
            request.HardwareProfile,
            request.FeatureFlags,
            2,
            DateTimeOffset.UtcNow);

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        tenantContext.SetupGet(x => x.TerminalId).Returns(actorTerminalId);

        Mock<ITenantConfigRepository> repository = new();
        repository
            .Setup(x => x.UpsertAsync(tenantId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);

        Mock<ISyncChangeWriter> syncChangeWriter = new();

        TenantConfigService service = new(
            tenantContext.Object,
            repository.Object,
            syncChangeWriter.Object,
            Mock.Of<ILogger<TenantConfigService>>());

        TenantConfigResponse? result = await service.UpdateCurrentAsync(request, CancellationToken.None);

        Assert.NotNull(result);
        syncChangeWriter.Verify(
            x => x.AppendAsync(
                tenantId,
                null,
                "tenant.config",
                tenantId,
                "update",
                2,
                It.IsAny<JsonElement>(),
                actorTerminalId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Update_does_not_write_sync_change_when_version_check_fails()
    {
        Guid tenantId = Guid.NewGuid();
        UpdateTenantConfigRequest request = CreateRequest();

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);

        Mock<ITenantConfigRepository> repository = new();
        repository
            .Setup(x => x.UpsertAsync(tenantId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantConfigResponse?)null);

        Mock<ISyncChangeWriter> syncChangeWriter = new();

        TenantConfigService service = new(
            tenantContext.Object,
            repository.Object,
            syncChangeWriter.Object,
            Mock.Of<ILogger<TenantConfigService>>());

        TenantConfigResponse? result = await service.UpdateCurrentAsync(request, CancellationToken.None);

        Assert.Null(result);
        syncChangeWriter.Verify(
            x => x.AppendAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<JsonElement>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static UpdateTenantConfigRequest CreateRequest()
    {
        return new UpdateTenantConfigRequest(
            "qsr_cafe",
            "touch_grid",
            JsonDocument.Parse("""{"sync":true}""").RootElement.Clone(),
            JsonDocument.Parse("""{"name":"Demo"}""").RootElement.Clone(),
            JsonDocument.Parse("{}").RootElement.Clone(),
            JsonDocument.Parse("{}").RootElement.Clone(),
            JsonDocument.Parse("{}").RootElement.Clone(),
            1);
    }
}
