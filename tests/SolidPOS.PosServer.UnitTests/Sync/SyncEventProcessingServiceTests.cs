using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Cash;
using SolidPOS.PosServer.Application.Inventory;
using SolidPOS.PosServer.Application.Sales;
using SolidPOS.PosServer.Application.Sync;
using SolidPOS.PosServer.Contracts.Cash;
using SolidPOS.PosServer.Contracts.Inventory;
using SolidPOS.PosServer.Contracts.Sales;
using SolidPOS.PosServer.Contracts.Sync;
using SolidPOS.PosServer.Infrastructure.Sync;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Sync;

public sealed class SyncEventProcessingServiceTests
{
    [Fact]
    public async Task ProcessPending_rejects_without_terminal_runtime_context()
    {
        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());

        Mock<ISyncEventRepository> repository = new();
        SyncEventProcessingService service = CreateService(tenantContext.Object, repository.Object);

        SyncProcessResponse? result = await service.ProcessPendingAsync(new SyncProcessRequest(null, 100), CancellationToken.None);

        Assert.Null(result);
        repository.Verify(
            x => x.ReadPendingAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessPending_marks_health_check_as_processed()
    {
        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        SyncInboxEvent syncEvent = CreateEvent(tenantId, storeId, terminalId, "pos.health_check", JsonDocument.Parse("""{"message":"ok"}""").RootElement.Clone());

        Mock<ITenantContext> tenantContext = CreateTerminalContext(tenantId, storeId, terminalId);
        Mock<ISyncEventRepository> repository = new();
        repository
            .Setup(x => x.ReadPendingAsync(tenantId, storeId, terminalId, null, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncInboxEvent> { syncEvent });

        SyncEventProcessingService service = CreateService(tenantContext.Object, repository.Object);

        SyncProcessResponse? result = await service.ProcessPendingAsync(new SyncProcessRequest(null, 100), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result.ProcessedCount);
        Assert.Equal(0, result.RejectedCount);
        repository.Verify(x => x.MarkProcessedAsync(tenantId, syncEvent.Id, It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessPending_routes_sale_completed_to_sales_service()
    {
        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        Guid cashierUserId = Guid.NewGuid();
        Guid localSaleId = Guid.NewGuid();

        CreateSaleRequest saleRequest = new(
            localSaleId,
            cashierUserId,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            [new CreateSaleLineRequest(Guid.NewGuid(), null, "1", 0, null, null)],
            [new CreateSalePaymentRequest(Guid.NewGuid(), "cash", 6500, null)],
            0);

        SyncInboxEvent syncEvent = CreateEvent(
            tenantId,
            storeId,
            terminalId,
            "sale.completed",
            JsonSerializer.SerializeToElement(saleRequest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Mock<ISalesService> salesService = new();
        salesService
            .Setup(x => x.CreateAsync(It.Is<CreateSaleRequest>(request => request.LocalSaleId == localSaleId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSaleResponse(tenantId, storeId, terminalId, cashierUserId, localSaleId));

        Mock<ITenantContext> tenantContext = CreateTerminalContext(tenantId, storeId, terminalId);
        Mock<ISyncEventRepository> repository = new();
        repository
            .Setup(x => x.ReadPendingAsync(tenantId, storeId, terminalId, null, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncInboxEvent> { syncEvent });

        SyncEventProcessingService service = CreateService(tenantContext.Object, repository.Object, salesService.Object);

        SyncProcessResponse? result = await service.ProcessPendingAsync(new SyncProcessRequest(null, 100), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result.ProcessedCount);
        salesService.Verify(x => x.CreateAsync(It.Is<CreateSaleRequest>(request => request.LocalSaleId == localSaleId), It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(x => x.MarkProcessedAsync(tenantId, syncEvent.Id, It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessPending_rejects_unsupported_event_type()
    {
        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        SyncInboxEvent syncEvent = CreateEvent(tenantId, storeId, terminalId, "unknown.event", JsonDocument.Parse("""{"x":1}""").RootElement.Clone());

        Mock<ITenantContext> tenantContext = CreateTerminalContext(tenantId, storeId, terminalId);
        Mock<ISyncEventRepository> repository = new();
        repository
            .Setup(x => x.ReadPendingAsync(tenantId, storeId, terminalId, null, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncInboxEvent> { syncEvent });

        SyncEventProcessingService service = CreateService(tenantContext.Object, repository.Object);

        SyncProcessResponse? result = await service.ProcessPendingAsync(new SyncProcessRequest(null, 100), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(0, result.ProcessedCount);
        Assert.Equal(1, result.RejectedCount);
        repository.Verify(x => x.MarkRejectedAsync(tenantId, syncEvent.Id, "unsupported_event_type", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessPending_routes_sale_voided_by_local_sale_id_to_sales_service()
    {
        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        Guid cashierUserId = Guid.NewGuid();
        Guid localSaleId = Guid.NewGuid();

        object payload = new
        {
            localSaleId,
            voidedByUserId = cashierUserId,
            reason = "Error offline",
            occurredAt = DateTimeOffset.UtcNow
        };

        SyncInboxEvent syncEvent = CreateEvent(
            tenantId,
            storeId,
            terminalId,
            "sale.voided",
            JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Mock<ISalesService> salesService = new();
        salesService
            .Setup(x => x.VoidByLocalSaleIdAsync(localSaleId, It.IsAny<VoidSaleRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSaleResponse(tenantId, storeId, terminalId, cashierUserId, localSaleId) with { Status = "voided" });

        Mock<ITenantContext> tenantContext = CreateTerminalContext(tenantId, storeId, terminalId);
        Mock<ISyncEventRepository> repository = new();
        repository
            .Setup(x => x.ReadPendingAsync(tenantId, storeId, terminalId, null, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncInboxEvent> { syncEvent });

        SyncEventProcessingService service = CreateService(tenantContext.Object, repository.Object, salesService.Object);

        SyncProcessResponse? result = await service.ProcessPendingAsync(new SyncProcessRequest(null, 100), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result.ProcessedCount);
        salesService.Verify(x => x.VoidByLocalSaleIdAsync(localSaleId, It.IsAny<VoidSaleRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(x => x.MarkProcessedAsync(tenantId, syncEvent.Id, It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static SyncEventProcessingService CreateService(
        ITenantContext tenantContext,
        ISyncEventRepository repository,
        ISalesService? salesService = null,
        IInventoryAdjustmentService? inventoryAdjustmentService = null,
        ICashShiftService? cashShiftService = null)
    {
        Mock<ILogger<SyncEventProcessingService>> logger = new();
        return new SyncEventProcessingService(
            tenantContext,
            repository,
            Mock.Of<ISyncConflictRepository>(),
            salesService ?? Mock.Of<ISalesService>(),
            inventoryAdjustmentService ?? Mock.Of<IInventoryAdjustmentService>(),
            cashShiftService ?? Mock.Of<ICashShiftService>(),
            CreateAuditEventWriter().Object,
            logger.Object);
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

    private static Mock<ITenantContext> CreateTerminalContext(Guid tenantId, Guid storeId, Guid terminalId)
    {
        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        tenantContext.SetupGet(x => x.StoreId).Returns(storeId);
        tenantContext.SetupGet(x => x.TerminalId).Returns(terminalId);
        return tenantContext;
    }

    private static SyncInboxEvent CreateEvent(Guid tenantId, Guid storeId, Guid terminalId, string eventType, JsonElement payload)
    {
        return new SyncInboxEvent(
            Guid.NewGuid(),
            tenantId,
            storeId,
            terminalId,
            null,
            Guid.NewGuid(),
            eventType,
            "test",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            1,
            payload);
    }

    private static SaleResponse CreateSaleResponse(Guid tenantId, Guid storeId, Guid terminalId, Guid cashierUserId, Guid localSaleId)
    {
        return new SaleResponse(
            Guid.NewGuid(),
            tenantId,
            storeId,
            terminalId,
            Guid.NewGuid(),
            null,
            cashierUserId,
            localSaleId,
            "completed",
            6500,
            0,
            0,
            0,
            6500,
            6500,
            0,
            "MXN",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            1,
            DateTimeOffset.UtcNow,
            [],
            []);
    }
}
