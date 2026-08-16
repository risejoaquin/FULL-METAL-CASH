using SolidPOS.PosServer.Contracts.Inventory;

namespace SolidPOS.PosServer.Application.Inventory;

public interface IInventoryStockRepository
{
    Task<IReadOnlyCollection<InventoryStockItemResponse>> GetCurrentStockAsync(Guid tenantId, Guid? storeId, CancellationToken cancellationToken);
}

