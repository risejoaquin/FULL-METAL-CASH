using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.Catalog;

public sealed record RemoteCatalogRecipeSnapshot(
    Guid RecipeId,
    Guid OutputProductId,
    Guid? OutputVariantId,
    decimal YieldQuantity,
    Guid YieldUnitId,
    decimal WastePercent,
    string Status);

public sealed record RemoteCatalogRecipeItemSnapshot(
    Guid RecipeItemId,
    Guid RecipeId,
    Guid IngredientProductId,
    Guid? IngredientVariantId,
    decimal Quantity,
    Guid UnitId,
    bool Optional);

public sealed record RemoteInventoryCacheSnapshot(
    IReadOnlyList<RemoteCatalogRecipeSnapshot> Recipes,
    IReadOnlyList<RemoteCatalogRecipeItemSnapshot> RecipeItems);

public sealed record InventoryCacheRefreshResult(
    int RecipeCount,
    int RecipeItemCount,
    DateTimeOffset SyncedAtUtc);

public interface IRemoteInventoryCatalogClient
{
    Task<RemoteInventoryCacheSnapshot> GetInventoryCacheAsync(string accessToken, CancellationToken cancellationToken = default);
}

public sealed class InventoryCacheService
{
    private readonly Storage.ILocalPosRepository _repository;
    private readonly IRemoteInventoryCatalogClient _remoteClient;
    private readonly Abstractions.IClock _clock;

    public InventoryCacheService(
        Storage.ILocalPosRepository repository,
        IRemoteInventoryCatalogClient remoteClient,
        Abstractions.IClock clock)
    {
        _repository = repository;
        _remoteClient = remoteClient;
        _clock = clock;
    }

    public async Task<InventoryCacheRefreshResult> RefreshAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) throw new ArgumentException("Access token is required.", nameof(accessToken));

        RemoteInventoryCacheSnapshot snapshot = await _remoteClient.GetInventoryCacheAsync(accessToken, cancellationToken).ConfigureAwait(false);
        DateTimeOffset syncedAtUtc = _clock.UtcNow;

        var recipes = snapshot.Recipes
            .Where(x => x.Status.Equals("active", StringComparison.OrdinalIgnoreCase))
            .Select(x => new LocalInventoryRecipe(
                x.RecipeId,
                x.OutputProductId,
                x.OutputVariantId,
                x.YieldQuantity,
                x.YieldUnitId,
                x.WastePercent,
                x.Status,
                syncedAtUtc))
            .ToArray();

        var recipeIds = recipes.Select(x => x.RecipeId).ToHashSet();
        var recipeItems = snapshot.RecipeItems
            .Where(x => recipeIds.Contains(x.RecipeId))
            .Select(x => new LocalInventoryRecipeItem(
                x.RecipeItemId,
                x.RecipeId,
                x.IngredientProductId,
                x.IngredientVariantId,
                x.Quantity,
                x.UnitId,
                x.Optional,
                syncedAtUtc))
            .ToArray();

        await _repository.SaveInventoryRecipeCacheAsync(recipes, recipeItems, syncedAtUtc, cancellationToken).ConfigureAwait(false);
        return new InventoryCacheRefreshResult(recipes.Length, recipeItems.Length, syncedAtUtc);
    }
}
