using SolidPOS.PosServer.Contracts.Inventory;

namespace SolidPOS.PosServer.Application.Inventory;

public interface IInventoryStockService
{
    Task<IReadOnlyCollection<InventoryStockItemResponse>?> GetCurrentStockAsync(Guid? storeId, CancellationToken cancellationToken);
}

