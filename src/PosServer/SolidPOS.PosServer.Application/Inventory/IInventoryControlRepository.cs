using SolidPOS.PosServer.Contracts.Inventory;

namespace SolidPOS.PosServer.Application.Inventory;

public interface IInventoryControlRepository
{
    Task<InventoryPolicyResponse> GetPolicyAsync(Guid tenantId, Guid? storeId, CancellationToken cancellationToken);

    Task<InventoryPolicyResponse?> UpsertPolicyAsync(Guid tenantId, UpdateInventoryPolicyRequest request, CancellationToken cancellationToken);

    Task<InventoryCountResponse?> CreateCountAsync(Guid tenantId, Guid storeId, Guid? terminalId, CreateInventoryCountRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<InventoryCountListItemResponse>> ListCountsAsync(Guid tenantId, InventoryControlFilters filters, CancellationToken cancellationToken);

    Task<InventoryTransferResponse?> CreateTransferAsync(Guid tenantId, Guid? terminalId, CreateInventoryTransferRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<InventoryTransferListItemResponse>> ListTransfersAsync(Guid tenantId, InventoryControlFilters filters, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<InventoryLowStockItemResponse>> GetLowStockAsync(Guid tenantId, Guid? storeId, int limit, CancellationToken cancellationToken);
}
