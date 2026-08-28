using Microsoft.Extensions.Logging;
using Moq;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Sales;
using SolidPOS.PosServer.Contracts.Sales;
using SolidPOS.PosServer.Infrastructure.Sales;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Sales;

public sealed class SalesServiceTests
{
    [Fact]
    public async Task Create_rejects_sale_without_terminal_runtime_context()
    {
        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());

        Mock<ISalesRepository> repository = new();
        SalesService service = CreateService(tenantContext.Object, repository.Object);

        SaleResponse? result = await service.CreateAsync(CreateValidRequest(), CancellationToken.None);

        Assert.Null(result);
        repository.Verify(
            x => x.CreateAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CreateSaleRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Create_rejects_sale_without_payments()
    {
        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());
        tenantContext.SetupGet(x => x.StoreId).Returns(Guid.NewGuid());
        tenantContext.SetupGet(x => x.TerminalId).Returns(Guid.NewGuid());

        Mock<ISalesRepository> repository = new();
        SalesService service = CreateService(tenantContext.Object, repository.Object);
        CreateSaleRequest request = CreateValidRequest() with { Payments = [] };

        SaleResponse? result = await service.CreateAsync(request, CancellationToken.None);

        Assert.Null(result);
        repository.Verify(
            x => x.CreateAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CreateSaleRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Void_rejects_sale_without_terminal_runtime_context()
    {
        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());

        Mock<ISalesRepository> repository = new();
        SalesService service = CreateService(tenantContext.Object, repository.Object);

        SaleResponse? result = await service.VoidAsync(Guid.NewGuid(), CreateValidVoidRequest(), CancellationToken.None);

        Assert.Null(result);
        repository.Verify(
            x => x.VoidAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<VoidSaleRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Void_calls_repository_with_terminal_runtime_context()
    {
        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        Guid saleId = Guid.NewGuid();

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        tenantContext.SetupGet(x => x.StoreId).Returns(storeId);
        tenantContext.SetupGet(x => x.TerminalId).Returns(terminalId);

        SaleResponse response = CreateSaleResponse(tenantId, storeId, terminalId, saleId, "voided");
        Mock<ISalesRepository> repository = new();
        repository
            .Setup(x => x.VoidAsync(tenantId, storeId, terminalId, saleId, It.IsAny<VoidSaleRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        SalesService service = CreateService(tenantContext.Object, repository.Object);

        SaleResponse? result = await service.VoidAsync(saleId, CreateValidVoidRequest(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("voided", result.Status);
        repository.Verify(x => x.VoidAsync(tenantId, storeId, terminalId, saleId, It.IsAny<VoidSaleRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VoidByLocalSaleId_calls_repository_with_terminal_runtime_context()
    {
        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        Guid localSaleId = Guid.NewGuid();

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        tenantContext.SetupGet(x => x.StoreId).Returns(storeId);
        tenantContext.SetupGet(x => x.TerminalId).Returns(terminalId);

        SaleResponse response = CreateSaleResponse(tenantId, storeId, terminalId, Guid.NewGuid(), "voided") with { LocalSaleId = localSaleId };
        Mock<ISalesRepository> repository = new();
        repository
            .Setup(x => x.VoidByLocalSaleIdAsync(tenantId, storeId, terminalId, localSaleId, It.IsAny<VoidSaleRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        SalesService service = CreateService(tenantContext.Object, repository.Object);

        SaleResponse? result = await service.VoidByLocalSaleIdAsync(localSaleId, CreateValidVoidRequest(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(localSaleId, result.LocalSaleId);
        Assert.Equal("voided", result.Status);
        repository.Verify(x => x.VoidByLocalSaleIdAsync(tenantId, storeId, terminalId, localSaleId, It.IsAny<VoidSaleRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }


    [Fact]
    public async Task GetById_calls_repository_with_tenant_context()
    {
        Guid tenantId = Guid.NewGuid();
        Guid saleId = Guid.NewGuid();

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);

        SaleDetailResponse response = CreateSaleDetailResponse(tenantId, Guid.NewGuid(), Guid.NewGuid(), saleId);
        Mock<ISalesRepository> repository = new();
        repository
            .Setup(x => x.GetByIdAsync(tenantId, saleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        SalesService service = CreateService(tenantContext.Object, repository.Object);

        SaleDetailResponse? result = await service.GetByIdAsync(saleId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(saleId, result.Id);
        repository.Verify(x => x.GetByIdAsync(tenantId, saleId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task List_rejects_invalid_status_before_repository()
    {
        Guid tenantId = Guid.NewGuid();

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);

        Mock<ISalesRepository> repository = new();
        SalesService service = CreateService(tenantContext.Object, repository.Object);

        IReadOnlyCollection<SaleListItemResponse>? result = await service.ListAsync(new SaleListFilters(null, null, null, null, "bad", 50), CancellationToken.None);

        Assert.Null(result);
        repository.Verify(x => x.ListAsync(It.IsAny<Guid>(), It.IsAny<SaleListFilters>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetReceipt_calls_repository_with_tenant_context()
    {
        Guid tenantId = Guid.NewGuid();
        Guid saleId = Guid.NewGuid();

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);

        var receipt = new SolidPOS.PosServer.Contracts.Receipts.ReceiptResponse(
            saleId,
            tenantId,
            Guid.NewGuid(),
            "Tenant",
            "Store",
            null,
            null,
            Guid.NewGuid(),
            "Terminal",
            Guid.NewGuid(),
            "Cashier",
            Guid.NewGuid(),
            "completed",
            "MXN",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            6500,
            0,
            0,
            0,
            6500,
            6500,
            0,
            [],
            []);
        Mock<ISalesRepository> repository = new();
        repository
            .Setup(x => x.GetReceiptAsync(tenantId, saleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(receipt);

        SalesService service = CreateService(tenantContext.Object, repository.Object);

        var result = await service.GetReceiptAsync(saleId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(saleId, result.SaleId);
        repository.Verify(x => x.GetReceiptAsync(tenantId, saleId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static SalesService CreateService(ITenantContext tenantContext, ISalesRepository repository)
    {
        Mock<ILogger<SalesService>> logger = new();
        Mock<IAuditEventWriter> auditEventWriter = CreateAuditEventWriter();
        return new SalesService(tenantContext, repository, auditEventWriter.Object, logger.Object);
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

    private static CreateSaleRequest CreateValidRequest()
    {
        return new CreateSaleRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            [new CreateSaleLineRequest(Guid.NewGuid(), null, "1", 0, null, null)],
            [new CreateSalePaymentRequest(Guid.NewGuid(), "cash", 6500, null)],
            0);
    }

    private static VoidSaleRequest CreateValidVoidRequest()
    {
        return new VoidSaleRequest(Guid.NewGuid(), "Error de captura", DateTimeOffset.UtcNow);
    }


    private static SaleDetailResponse CreateSaleDetailResponse(Guid tenantId, Guid storeId, Guid terminalId, Guid saleId)
    {
        return new SaleDetailResponse(
            saleId,
            tenantId,
            storeId,
            terminalId,
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
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
            [],
            []);
    }

    private static SaleResponse CreateSaleResponse(Guid tenantId, Guid storeId, Guid terminalId, Guid saleId, string status)
    {
        return new SaleResponse(
            saleId,
            tenantId,
            storeId,
            terminalId,
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            status,
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
            2,
            DateTimeOffset.UtcNow,
            [],
            []);
    }
}
