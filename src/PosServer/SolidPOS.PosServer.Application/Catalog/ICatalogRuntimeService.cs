using SolidPOS.PosServer.Contracts.Catalog;

namespace SolidPOS.PosServer.Application.Catalog;

public interface ICatalogRuntimeService
{
    Task<CatalogSnapshotResponse?> GetSnapshotAsync(CancellationToken cancellationToken);
}
