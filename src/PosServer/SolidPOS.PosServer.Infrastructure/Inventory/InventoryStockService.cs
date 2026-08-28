using Microsoft.Extensions.Logging;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Inventory;
using SolidPOS.PosServer.Contracts.Inventory;

namespace SolidPOS.PosServer.Infrastructure.Inventory;

public sealed class InventoryStockService : IInventoryStockService
{
    private readonly ITenantContext _tenantContext;
    private readonly IInventoryStockRepository _repository;
    private readonly ILogger<InventoryStockService> _logger;

    public InventoryStockService(
        ITenantContext tenantContext,
        IInventoryStockRepository repository,
        ILogger<InventoryStockService> logger)
    {
        _tenantContext = tenantContext;
        _repository = repository;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<InventoryStockItemResponse>?> GetCurrentStockAsync(Guid? storeId, CancellationToken cancellationToken)
    {
        Guid? tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue)
        {
            _logger.LogWarning("Inventory stock read rejected because tenant context is missing");
            return null;
        }

        Guid? effectiveStoreId = storeId;
        if (_tenantContext.TerminalId.HasValue && _tenantContext.StoreId.HasValue)
        {
            if (storeId.HasValue && storeId.Value != _tenantContext.StoreId.Value)
            {
                _logger.LogWarning(
                    "Inventory stock read rejected for tenant {TenantId} terminal {TerminalId}: requested store {RequestedStoreId} differs from terminal store {TerminalStoreId}",
                    tenantId.Value,
                    _tenantContext.TerminalId.Value,
                    storeId.Value,
                    _tenantContext.StoreId.Value);
                return null;
            }

            effectiveStoreId = _tenantContext.StoreId.Value;
        }

        IReadOnlyCollection<InventoryStockItemResponse> stock = await _repository.GetCurrentStockAsync(tenantId.Value, effectiveStoreId, cancellationToken);
        _logger.LogInformation("Inventory stock read for tenant {TenantId} store {StoreId} returned {ItemCount} items", tenantId.Value, effectiveStoreId, stock.Count);
        return stock;
    }
}
