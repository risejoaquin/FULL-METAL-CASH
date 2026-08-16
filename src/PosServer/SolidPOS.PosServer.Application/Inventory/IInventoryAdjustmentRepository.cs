using SolidPOS.PosServer.Contracts.Inventory;

namespace SolidPOS.PosServer.Application.Inventory;

public interface IInventoryAdjustmentRepository
{
    Task<InventoryAdjustmentResponse?> CreateAsync(
        Guid tenantId,
        Guid storeId,
        Guid? terminalId,
        CreateInventoryAdjustmentRequest request,
        CancellationToken cancellationToken);
}
