using Microsoft.Extensions.Logging;
using Moq;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Inventory;
using SolidPOS.PosServer.Contracts.Inventory;
using SolidPOS.PosServer.Infrastructure.Inventory;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Inventory;

public sealed class InventoryStockServiceTests
{
    [Fact]
    public async Task GetCurrentStock_rejects_terminal_request_for_different_store()
    {
        Guid tenantId = Guid.NewGuid();
        Guid terminalStoreId = Guid.NewGuid();
        Guid otherStoreId = Guid.NewGuid();

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        tenantContext.SetupGet(x => x.StoreId).Returns(terminalStoreId);
        tenantContext.SetupGet(x => x.TerminalId).Returns(Guid.NewGuid());

        Mock<IInventoryStockRepository> repository = new();
        InventoryStockService service = CreateService(tenantContext.Object, repository.Object);

        IReadOnlyCollection<InventoryStockItemResponse>? result = await service.GetCurrentStockAsync(otherStoreId, CancellationToken.None);

        Assert.Null(result);
        repository.Verify(
            x => x.GetCurrentStockAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetCurrentStock_for_terminal_defaults_to_terminal_store()
    {
        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        tenantContext.SetupGet(x => x.StoreId).Returns(storeId);
        tenantContext.SetupGet(x => x.TerminalId).Returns(Guid.NewGuid());

        Mock<IInventoryStockRepository> repository = new();
        repository
            .Setup(x => x.GetCurrentStockAsync(tenantId, storeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<InventoryStockItemResponse>());

        InventoryStockService service = CreateService(tenantContext.Object, repository.Object);

        IReadOnlyCollection<InventoryStockItemResponse>? result = await service.GetCurrentStockAsync(null, CancellationToken.None);

        Assert.NotNull(result);
        repository.Verify(x => x.GetCurrentStockAsync(tenantId, storeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static InventoryStockService CreateService(ITenantContext tenantContext, IInventoryStockRepository repository)
    {
        Mock<ILogger<InventoryStockService>> logger = new();
        return new InventoryStockService(tenantContext, repository, logger.Object);
    }
}
