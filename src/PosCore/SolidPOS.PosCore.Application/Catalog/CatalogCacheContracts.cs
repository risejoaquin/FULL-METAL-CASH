using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.Catalog;

public sealed record RemoteCatalogProductSnapshot(
    Guid ProductId,
    Guid? VariantId,
    string Sku,
    string Name,
    int PriceCents,
    string Currency,
    string Status,
    DateTimeOffset UpdatedAtUtc);

public sealed record CatalogCacheRefreshResult(
    int CachedProductCount,
    DateTimeOffset SyncedAtUtc);

public interface IRemoteCatalogClient
{
    Task<IReadOnlyList<RemoteCatalogProductSnapshot>> GetCatalogProductsAsync(
        string accessToken,
        CancellationToken cancellationToken = default);
}

public sealed class CatalogCacheService
{
    private readonly ILocalPosRepository _repository;
    private readonly IRemoteCatalogClient _remoteCatalogClient;
    private readonly IClock _clock;

    public CatalogCacheService(
        ILocalPosRepository repository,
        IRemoteCatalogClient remoteCatalogClient,
        IClock clock)
    {
        _repository = repository;
        _remoteCatalogClient = remoteCatalogClient;
        _clock = clock;
    }

    public async Task<CatalogCacheRefreshResult> RefreshAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("Catalog sync requires a terminal or admin access token.");
        }

        var remoteProducts = await _remoteCatalogClient.GetCatalogProductsAsync(accessToken, cancellationToken).ConfigureAwait(false);
        var syncedAtUtc = _clock.UtcNow;
        var localProducts = remoteProducts
            .Where(product => !string.IsNullOrWhiteSpace(product.Sku) && !string.IsNullOrWhiteSpace(product.Name))
            .Select(product => new LocalCatalogProduct(
                product.ProductId,
                product.VariantId,
                product.Sku,
                product.Name,
                product.PriceCents,
                product.Currency,
                product.Status,
                product.UpdatedAtUtc,
                syncedAtUtc))
            .ToArray();

        await _repository.SaveCatalogProductsAsync(localProducts, syncedAtUtc, cancellationToken).ConfigureAwait(false);
        return new CatalogCacheRefreshResult(localProducts.Length, syncedAtUtc);
    }
}
