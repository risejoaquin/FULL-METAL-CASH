using Microsoft.Extensions.Logging;
using Moq;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Inventory;
using SolidPOS.PosServer.Contracts.Inventory;
using SolidPOS.PosServer.Infrastructure.Inventory;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Inventory;

public sealed class InventoryAdjustmentServiceTests
{
    [Fact]
    public async Task Create_rejects_admin_adjustment_without_store()
    {
        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());

        Mock<IInventoryAdjustmentRepository> repository = new();
        InventoryAdjustmentService service = CreateService(tenantContext.Object, repository.Object);

        InventoryAdjustmentResponse? result = await service.CreateAsync(CreateValidRequest() with { StoreId = null }, CancellationToken.None);

        Assert.Null(result);
        repository.Verify(
            x => x.CreateAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CreateInventoryAdjustmentRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Create_rejects_stock_in_with_negative_quantity()
    {
        Guid storeId = Guid.NewGuid();
        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());

        Mock<IInventoryAdjustmentRepository> repository = new();
        InventoryAdjustmentService service = CreateService(tenantContext.Object, repository.Object);

        CreateInventoryAdjustmentRequest request = CreateValidRequest() with
        {
            StoreId = storeId,
            AdjustmentType = "stock_in",
            Lines = [new CreateInventoryAdjustmentLineRequest(Guid.NewGuid(), null, "-1", Guid.NewGuid(), null)]
        };

        InventoryAdjustmentResponse? result = await service.CreateAsync(request, CancellationToken.None);

        Assert.Null(result);
        repository.Verify(
            x => x.CreateAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CreateInventoryAdjustmentRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Create_for_terminal_uses_terminal_store()
    {
        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        tenantContext.SetupGet(x => x.StoreId).Returns(storeId);
        tenantContext.SetupGet(x => x.TerminalId).Returns(terminalId);

        CreateInventoryAdjustmentRequest request = CreateValidRequest() with { StoreId = null };
        InventoryAdjustmentResponse response = CreateResponse(tenantId, storeId, terminalId, request);

        Mock<IInventoryAdjustmentRepository> repository = new();
        repository
            .Setup(x => x.CreateAsync(tenantId, storeId, terminalId, It.IsAny<CreateInventoryAdjustmentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        InventoryAdjustmentService service = CreateService(tenantContext.Object, repository.Object);

        InventoryAdjustmentResponse? result = await service.CreateAsync(request, CancellationToken.None);

        Assert.NotNull(result);
        repository.Verify(x => x.CreateAsync(tenantId, storeId, terminalId, It.IsAny<CreateInventoryAdjustmentRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static InventoryAdjustmentService CreateService(ITenantContext tenantContext, IInventoryAdjustmentRepository repository)
    {
        Mock<ILogger<InventoryAdjustmentService>> logger = new();
        Mock<IAuditEventWriter> auditEventWriter = CreateAuditEventWriter();
        return new InventoryAdjustmentService(tenantContext, repository, auditEventWriter.Object, logger.Object);
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
                It.IsAny<System.Text.Json.JsonElement?>(),
                It.IsAny<System.Text.Json.JsonElement?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return auditEventWriter;
    }

    private static CreateInventoryAdjustmentRequest CreateValidRequest()
    {
        return new CreateInventoryAdjustmentRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "stock_in",
            "Conteo fisico",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            [new CreateInventoryAdjustmentLineRequest(Guid.NewGuid(), null, "1", Guid.NewGuid(), null)]);
    }

    private static InventoryAdjustmentResponse CreateResponse(Guid tenantId, Guid storeId, Guid? terminalId, CreateInventoryAdjustmentRequest request)
    {
        return new InventoryAdjustmentResponse(
            Guid.NewGuid(),
            tenantId,
            storeId,
            terminalId,
            request.LocalAdjustmentId,
            request.AdjustmentType,
            request.Reason,
            request.CreatedByUserId,
            request.OccurredAt,
            DateTimeOffset.UtcNow,
            [new InventoryAdjustmentLineResponse(Guid.NewGuid(), request.Lines.First().ProductId, null, "adjustment", "1.0000", request.Lines.First().UnitId, null)]);
    }
}
