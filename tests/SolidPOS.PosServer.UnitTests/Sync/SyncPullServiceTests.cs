using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Abstractions.Time;
using SolidPOS.PosServer.Application.Catalog;
using SolidPOS.PosServer.Application.Sync;
using SolidPOS.PosServer.Application.Tenants;
using SolidPOS.PosServer.Contracts.Catalog;
using SolidPOS.PosServer.Contracts.Sync;
using SolidPOS.PosServer.Contracts.Tenants;
using SolidPOS.PosServer.Infrastructure.Sync;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Sync;

public sealed class SyncPullServiceTests
{
    [Fact]
    public async Task Pull_rejects_missing_terminal_runtime_context()
    {
        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());

        Mock<ISyncPullRepository> repository = new();
        SyncPullService service = CreateService(tenantContext.Object, repository.Object);

        SyncPullResponse? result = await service.PullAsync(null, 100, CancellationToken.None);

        Assert.Null(result);
        repository.Verify(
            x => x.ReadChangesAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<DateTimeOffset?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Pull_rejects_invalid_cursor()
    {
        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();

        Mock<ITenantContext> tenantContext = CreateTerminalContext(tenantId, storeId, terminalId);
        Mock<ISyncPullRepository> repository = new();
        SyncPullService service = CreateService(tenantContext.Object, repository.Object);

        SyncPullResponse? result = await service.PullAsync("not-a-cursor", 100, CancellationToken.None);

        Assert.Null(result);
        repository.Verify(
            x => x.ReadChangesAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<DateTimeOffset?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Pull_without_cursor_returns_bootstrap_snapshots_and_deltas()
    {
        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-15T22:30:00Z");

        Mock<ITenantContext> tenantContext = CreateTerminalContext(tenantId, storeId, terminalId);

        Mock<ISyncPullRepository> repository = new();
        repository
            .Setup(x => x.ReadAccessSnapshotAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonDocument.Parse("""{"users":[]}""").RootElement.Clone());
        repository
            .Setup(x => x.ReadChangesAsync(tenantId, storeId, terminalId, null, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncPullChangeResponse>
            {
                new(Guid.NewGuid(), "price.updated", Guid.NewGuid(), "update", 2, now, JsonDocument.Parse("""{"price":6500}""").RootElement.Clone(), storeId, null)
            });

        Mock<ITenantConfigService> tenantConfigService = new();
        tenantConfigService
            .Setup(x => x.GetCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantConfigResponse(
                tenantId,
                "qsr_cafe",
                "touch_grid",
                JsonDocument.Parse("""{"sync":true}""").RootElement.Clone(),
                JsonDocument.Parse("""{"name":"Demo"}""").RootElement.Clone(),
                JsonDocument.Parse("{}").RootElement.Clone(),
                JsonDocument.Parse("{}").RootElement.Clone(),
                JsonDocument.Parse("{}").RootElement.Clone(),
                1,
                now));

        Mock<ICatalogRuntimeService> catalogRuntimeService = new();
        catalogRuntimeService
            .Setup(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogSnapshotResponse(tenantId, now, [], [], [], [], [], [], [], [], [], [], [], []));

        Mock<IClock> clock = new();
        clock.SetupGet(x => x.UtcNow).Returns(now);

        SyncPullService service = CreateService(
            tenantContext.Object,
            repository.Object,
            tenantConfigService.Object,
            catalogRuntimeService.Object,
            clock.Object);

        SyncPullResponse? result = await service.PullAsync(null, 100, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result.Changes, x => x.EntityType == "tenant.config");
        Assert.Contains(result.Changes, x => x.EntityType == "tenant.catalog");
        Assert.Contains(result.Changes, x => x.EntityType == "tenant.access");
        Assert.Contains(result.Changes, x => x.EntityType == "terminal.runtime");
        Assert.Contains(result.Changes, x => x.EntityType == "price.updated");
        Assert.False(result.HasMore);
    }

    private static SyncPullService CreateService(
        ITenantContext tenantContext,
        ISyncPullRepository repository,
        ITenantConfigService? tenantConfigService = null,
        ICatalogRuntimeService? catalogRuntimeService = null,
        IClock? clock = null)
    {
        Mock<ILogger<SyncPullService>> logger = new();
        Mock<IClock> defaultClock = new();
        defaultClock.SetupGet(x => x.UtcNow).Returns(DateTimeOffset.UtcNow);

        return new SyncPullService(
            tenantContext,
            repository,
            tenantConfigService ?? Mock.Of<ITenantConfigService>(),
            catalogRuntimeService ?? Mock.Of<ICatalogRuntimeService>(),
            clock ?? defaultClock.Object,
            logger.Object);
    }

    private static Mock<ITenantContext> CreateTerminalContext(Guid tenantId, Guid storeId, Guid terminalId)
    {
        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        tenantContext.SetupGet(x => x.StoreId).Returns(storeId);
        tenantContext.SetupGet(x => x.TerminalId).Returns(terminalId);
        return tenantContext;
    }
}
