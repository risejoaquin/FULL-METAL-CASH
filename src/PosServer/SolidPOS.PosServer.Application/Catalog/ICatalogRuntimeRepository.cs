using SolidPOS.PosServer.Contracts.Catalog;

namespace SolidPOS.PosServer.Application.Catalog;

public interface ICatalogRuntimeRepository
{
    Task<CatalogSnapshotResponse> GetSnapshotAsync(Guid tenantId, DateTimeOffset generatedAt, CancellationToken cancellationToken);
}
