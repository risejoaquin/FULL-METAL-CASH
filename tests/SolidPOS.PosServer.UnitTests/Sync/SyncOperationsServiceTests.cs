using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Abstractions.Time;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Sync;
using SolidPOS.PosServer.Contracts.Sync;
using SolidPOS.PosServer.Infrastructure.Sync;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Sync;

public sealed class SyncOperationsServiceTests
{
    [Fact]
    public async Task GetStatus_rejects_without_tenant_context()
    {
        Mock<ITenantContext> tenantContext = new();
        Mock<ISyncOperationsRepository> repository = new();
        SyncOperationsService service = CreateService(tenantContext.Object, repository.Object);

        SyncRuntimeStatusResponse? result = await service.GetStatusAsync(null, null, CancellationToken.None);

        Assert.Null(result);
        repository.Verify(x => x.GetStatusAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RetryDeadLetter_requires_reason()
    {
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        Mock<ISyncOperationsRepository> repository = new();
        SyncOperationsService service = CreateService(tenantContext.Object, repository.Object);

        RetrySyncDeadLetterResponse? result = await service.RetryDeadLetterAsync(Guid.NewGuid(), new RetrySyncDeadLetterRequest(" "), CancellationToken.None);

        Assert.Null(result);
        repository.Verify(x => x.RetryDeadLetterAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void GetContract_returns_supported_sync_schema()
    {
        SyncOperationsService service = CreateService(Mock.Of<ITenantContext>(), Mock.Of<ISyncOperationsRepository>());

        SyncContractResponse contract = service.GetContract();

        Assert.Equal(4, contract.CurrentSchemaVersion);
        Assert.Contains("sale.completed", contract.SupportedInboundEventTypes);
        Assert.Contains("dead_letter", contract.SupportedStatuses);
        Assert.Contains("compensate", contract.ConflictResolutionStrategies);
    }

    private static SyncOperationsService CreateService(ITenantContext tenantContext, ISyncOperationsRepository repository)
    {
        Mock<IClock> clock = new();
        clock.SetupGet(x => x.UtcNow).Returns(DateTimeOffset.UtcNow);

        Mock<IAuditEventWriter> audit = new();
        audit
            .Setup(x => x.AppendAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<JsonElement?>(),
                It.IsAny<JsonElement?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new SyncOperationsService(
            tenantContext,
            clock.Object,
            repository,
            audit.Object,
            Mock.Of<ILogger<SyncOperationsService>>());
    }
}
