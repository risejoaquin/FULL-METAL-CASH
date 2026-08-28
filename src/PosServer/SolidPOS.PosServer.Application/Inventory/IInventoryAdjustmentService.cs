using SolidPOS.PosServer.Contracts.Inventory;

namespace SolidPOS.PosServer.Application.Inventory;

public interface IInventoryAdjustmentService
{
    Task<InventoryAdjustmentResponse?> CreateAsync(CreateInventoryAdjustmentRequest request, CancellationToken cancellationToken);
}
