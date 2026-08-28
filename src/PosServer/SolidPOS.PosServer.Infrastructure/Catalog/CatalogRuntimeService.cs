using Microsoft.Extensions.Logging;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Abstractions.Time;
using SolidPOS.PosServer.Application.Catalog;
using SolidPOS.PosServer.Contracts.Catalog;

namespace SolidPOS.PosServer.Infrastructure.Catalog;

public sealed class CatalogRuntimeService : ICatalogRuntimeService
{
    private readonly ITenantContext _tenantContext;
    private readonly ICatalogRuntimeRepository _repository;
    private readonly IClock _clock;
    private readonly ILogger<CatalogRuntimeService> _logger;

    public CatalogRuntimeService(
        ITenantContext tenantContext,
        ICatalogRuntimeRepository repository,
        IClock clock,
        ILogger<CatalogRuntimeService> logger)
    {
        _tenantContext = tenantContext;
        _repository = repository;
        _clock = clock;
        _logger = logger;
    }

    public async Task<CatalogSnapshotResponse?> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        Guid? tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue)
        {
            _logger.LogWarning("Catalog snapshot rejected because tenant context is missing");
            return null;
        }

        CatalogSnapshotResponse snapshot = await _repository.GetSnapshotAsync(tenantId.Value, _clock.UtcNow, cancellationToken);
        _logger.LogInformation(
            "Catalog snapshot read for tenant {TenantId} with {ProductCount} products",
            tenantId.Value,
            snapshot.Products.Count);

        return snapshot;
    }
}
