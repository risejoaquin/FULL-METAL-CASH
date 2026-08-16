using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Sync;
using SolidPOS.PosServer.Contracts.Sync;
using SolidPOS.PosServer.Infrastructure.Sync;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Sync;

public sealed class SyncPushServiceTests
{
    [Fact]
    public async Task Push_rejects_batch_without_terminal_runtime_context()
    {
        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());

        Mock<ISyncPushRepository> repository = new();
        SyncPushService service = CreateService(tenantContext.Object, repository.Object);

        SyncPushResponse? result = await service.PushAsync(CreateValidRequest(), CancellationToken.None);

        Assert.Null(result);
        repository.Verify(
            x => x.IngestAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyCollection<SyncPushEventEnvelope>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Push_marks_duplicate_events_inside_same_batch_as_rejected()
    {
        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        Guid eventId = Guid.NewGuid();

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        tenantContext.SetupGet(x => x.StoreId).Returns(storeId);
        tenantContext.SetupGet(x => x.TerminalId).Returns(terminalId);

        Mock<ISyncPushRepository> repository = new();
        repository
            .Setup(x => x.IngestAsync(
                tenantId,
                storeId,
                terminalId,
                It.IsAny<Guid>(),
                It.Is<IReadOnlyCollection<SyncPushEventEnvelope>>(events => events.Count == 1),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncPushEventResultResponse> { new(eventId, "accepted", null) });

        SyncPushRequest request = CreateValidRequest(eventId) with
        {
            Events =
            [
                CreateValidEvent(eventId),
                CreateValidEvent(eventId)
            ]
        };

        SyncPushService service = CreateService(tenantContext.Object, repository.Object);

        SyncPushResponse? result = await service.PushAsync(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result.ReceivedCount);
        Assert.Equal(1, result.AcceptedCount);
        Assert.Equal(1, result.RejectedCount);
        Assert.Contains(result.Results, x => x.Status == "rejected" && x.Reason == "duplicate_event_in_batch");
    }

    [Fact]
    public async Task Push_returns_repository_duplicate_as_successful_acknowledgement()
    {
        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        Guid eventId = Guid.NewGuid();

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        tenantContext.SetupGet(x => x.StoreId).Returns(storeId);
        tenantContext.SetupGet(x => x.TerminalId).Returns(terminalId);

        Mock<ISyncPushRepository> repository = new();
        repository
            .Setup(x => x.IngestAsync(
                tenantId,
                storeId,
                terminalId,
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyCollection<SyncPushEventEnvelope>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncPushEventResultResponse> { new(eventId, "duplicate", "event_already_received") });

        SyncPushService service = CreateService(tenantContext.Object, repository.Object);

        SyncPushResponse? result = await service.PushAsync(CreateValidRequest(eventId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result.ReceivedCount);
        Assert.Equal(0, result.AcceptedCount);
        Assert.Equal(1, result.DuplicateCount);
        Assert.Equal("duplicate", result.Results.Single().Status);
    }

    private static SyncPushService CreateService(ITenantContext tenantContext, ISyncPushRepository repository)
    {
        Mock<ILogger<SyncPushService>> logger = new();
        Mock<IAuditEventWriter> auditEventWriter = CreateAuditEventWriter();
        return new SyncPushService(tenantContext, repository, auditEventWriter.Object, logger.Object);
    }

    private static Mock<IAuditEventWriter> CreateAuditEventWriter()
    {
        Mock<IAuditEventWriter> auditEventWriter = new();
        auditEventWriter
            .Setup(x => x.AppendAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<JsonElement?>(),
                It.IsAny<JsonElement?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return auditEventWriter;
    }

    private static SyncPushRequest CreateValidRequest(Guid? eventId = null)
    {
        return new SyncPushRequest(Guid.NewGuid(), [CreateValidEvent(eventId ?? Guid.NewGuid())]);
    }

    private static SyncPushEventRequest CreateValidEvent(Guid eventId)
    {
        return new SyncPushEventRequest(
            eventId,
            "sale.completed",
            "sale",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            1,
            JsonDocument.Parse("""{"total_cents":6500}""").RootElement.Clone());
    }
}
