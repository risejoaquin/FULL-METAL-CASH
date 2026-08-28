using SolidPOS.PosServer.Contracts.Inventory;

namespace SolidPOS.PosServer.Application.Inventory;

public interface IInventoryControlService
{
    Task<InventoryPolicyResponse?> GetPolicyAsync(Guid? storeId, CancellationToken cancellationToken);

    Task<InventoryPolicyResponse?> UpsertPolicyAsync(UpdateInventoryPolicyRequest request, CancellationToken cancellationToken);

    Task<InventoryCountResponse?> CreateCountAsync(CreateInventoryCountRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<InventoryCountListItemResponse>?> ListCountsAsync(InventoryControlFilters filters, CancellationToken cancellationToken);

    Task<InventoryTransferResponse?> CreateTransferAsync(CreateInventoryTransferRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<InventoryTransferListItemResponse>?> ListTransfersAsync(InventoryControlFilters filters, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<InventoryLowStockItemResponse>?> GetLowStockAsync(Guid? storeId, int limit, CancellationToken cancellationToken);
}
