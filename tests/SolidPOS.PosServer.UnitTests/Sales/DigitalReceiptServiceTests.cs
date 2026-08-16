using Microsoft.Extensions.Logging;
using Moq;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Receipts;
using SolidPOS.PosServer.Application.Sales;
using SolidPOS.PosServer.Contracts.Receipts;
using SolidPOS.PosServer.Infrastructure.Receipts;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Sales;

public sealed class DigitalReceiptServiceTests
{
    [Fact]
    public async Task Issue_rejects_without_tenant_context()
    {
        Mock<ITenantContext> tenantContext = new();
        Mock<IDigitalReceiptRepository> repository = new();
        Mock<ISalesRepository> salesRepository = new();
        DigitalReceiptService service = CreateService(tenantContext.Object, repository.Object, salesRepository.Object);

        DigitalReceiptResponse? result = await service.IssueAsync(Guid.NewGuid(), new IssueDigitalReceiptRequest(), "http://localhost:5000", CancellationToken.None);

        Assert.Null(result);
        repository.Verify(x => x.IssueAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Issue_persists_receipt_and_returns_public_url()
    {
        Guid tenantId = Guid.NewGuid();
        Guid saleId = Guid.NewGuid();
        Guid digitalReceiptId = Guid.NewGuid();
        ReceiptResponse receipt = CreateReceipt(tenantId, saleId);

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);

        Mock<ISalesRepository> salesRepository = new();
        salesRepository
            .Setup(x => x.GetReceiptAsync(tenantId, saleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(receipt);

        Mock<IDigitalReceiptRepository> repository = new();
        repository
            .Setup(x => x.IssueAsync(
                tenantId,
                saleId,
                It.Is<string>(value => value.StartsWith("SP-", StringComparison.Ordinal)),
                It.Is<string>(value => value.Length == 64),
                It.Is<string>(value => value.StartsWith("http://localhost:5000/api/v1/receipts/public/", StringComparison.Ordinal)),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenantArg, Guid saleArg, string receiptNumber, string hashArg, string publicUrl, DateTimeOffset? expiresArg, CancellationToken ctArg) =>
                new DigitalReceiptRecord(digitalReceiptId, tenantId, saleId, receiptNumber, publicUrl, "active", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null, 0));

        DigitalReceiptService service = CreateService(tenantContext.Object, repository.Object, salesRepository.Object);

        DigitalReceiptResponse? result = await service.IssueAsync(saleId, new IssueDigitalReceiptRequest(), "http://localhost:5000", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(digitalReceiptId, result.Id);
        Assert.Equal(saleId, result.SaleId);
        Assert.Equal("active", result.Status);
        Assert.StartsWith("http://localhost:5000/api/v1/receipts/public/", result.PublicUrl, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(result.PublicToken));
        Assert.Equal(receipt, result.Receipt);
    }

    [Fact]
    public async Task EmailStub_marks_existing_receipt_as_sent()
    {
        Guid tenantId = Guid.NewGuid();
        Guid saleId = Guid.NewGuid();
        Guid digitalReceiptId = Guid.NewGuid();
        ReceiptResponse receipt = CreateReceipt(tenantId, saleId);

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);

        Mock<ISalesRepository> salesRepository = new();
        salesRepository
            .Setup(x => x.GetReceiptAsync(tenantId, saleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(receipt);

        Mock<IDigitalReceiptRepository> repository = new();
        repository
            .Setup(x => x.IssueAsync(
                tenantId,
                saleId,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenantArg, Guid saleArg, string receiptNumber, string hashArg, string publicUrl, DateTimeOffset? expiresArg, CancellationToken ctArg) =>
                new DigitalReceiptRecord(digitalReceiptId, tenantId, saleId, receiptNumber, publicUrl, "active", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null, 0));
        repository
            .Setup(x => x.MarkEmailStubSentAsync(tenantId, saleId, "customer@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DigitalReceiptRecord(digitalReceiptId, tenantId, saleId, "SP-20260816-ABCDEF12", "http://localhost:5000/api/v1/receipts/public/token", "active", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, "customer@example.com", 1));

        DigitalReceiptService service = CreateService(tenantContext.Object, repository.Object, salesRepository.Object);

        EmailReceiptResponse? result = await service.EmailStubAsync(saleId, new EmailReceiptRequest(" customer@example.com "), "http://localhost:5000", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("queued_stub", result.Status);
        Assert.Equal("customer@example.com", result.RecipientEmail);
        Assert.Equal(digitalReceiptId, result.DigitalReceiptId);
    }

    private static DigitalReceiptService CreateService(
        ITenantContext tenantContext,
        IDigitalReceiptRepository repository,
        ISalesRepository salesRepository)
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
        Mock<ILogger<DigitalReceiptService>> logger = new();
        return new DigitalReceiptService(tenantContext, repository, salesRepository, auditEventWriter.Object, logger.Object);
    }

    private static ReceiptResponse CreateReceipt(Guid tenantId, Guid saleId)
    {
        return new ReceiptResponse(
            saleId,
            tenantId,
            Guid.NewGuid(),
            "SolidPOS Demo Cafe",
            "Main Store",
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
            7300,
            0,
            0,
            0,
            7300,
            10000,
            2700,
            [],
            []);
    }
}
