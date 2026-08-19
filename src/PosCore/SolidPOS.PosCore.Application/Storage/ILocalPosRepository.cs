using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.Storage;

public interface ILocalPosRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task SaveTerminalBindingAsync(TerminalBinding binding, CancellationToken cancellationToken = default);
    Task<TerminalBinding?> GetTerminalBindingAsync(CancellationToken cancellationToken = default);
    Task SaveOfflineSaleAsync(OfflineSaleDraft sale, LocalOutboxEvent outboxEvent, CancellationToken cancellationToken = default);
    Task SaveOfflineSaleWithInventoryAsync(OfflineSaleDraft sale, LocalOutboxEvent outboxEvent, IReadOnlyCollection<LocalInventoryMovement> movements, CancellationToken cancellationToken = default);
    Task SaveCatalogProductsAsync(IReadOnlyCollection<LocalCatalogProduct> products, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default);
    Task SaveInventoryRecipeCacheAsync(IReadOnlyCollection<LocalInventoryRecipe> recipes, IReadOnlyCollection<LocalInventoryRecipeItem> recipeItems, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default);
    Task<LocalInventoryRecipe?> GetRecipeForOutputAsync(Guid productId, Guid? variantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalInventoryRecipeItem>> GetRecipeItemsAsync(Guid recipeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalInventoryMovement>> GetInventoryMovementsByLocalSaleIdAsync(Guid localSaleId, CancellationToken cancellationToken = default);
    Task<int> CountInventoryRecipesAsync(CancellationToken cancellationToken = default);
    Task<int> CountInventoryRecipeItemsAsync(CancellationToken cancellationToken = default);
    Task<LocalCatalogProduct?> GetCatalogProductBySkuAsync(string sku, CancellationToken cancellationToken = default);
    Task<int> CountCatalogProductsAsync(CancellationToken cancellationToken = default);
    Task SaveOutboxEventAsync(LocalOutboxEvent outboxEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalOutboxEvent>> GetPendingOutboxEventsAsync(int limit, CancellationToken cancellationToken = default);
    Task<LocalOutboxEvent?> GetLatestOutboxEventByStatusAsync(LocalOutboxStatus status, CancellationToken cancellationToken = default);
    Task MarkOutboxSyncedAsync(IEnumerable<Guid> eventIds, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default);
    Task MarkOutboxFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default);
    Task ResetOutboxEventToPendingAsync(Guid eventId, string reason, CancellationToken cancellationToken = default);
    Task<int> RetryFailedOutboxEventsAsync(int maxAttempts, string reason, CancellationToken cancellationToken = default);
    Task SaveSyncAcknowledgementsAsync(IEnumerable<LocalSyncAcknowledgement> acknowledgements, CancellationToken cancellationToken = default);
    Task<int> CountOutboxByStatusAsync(LocalOutboxStatus status, CancellationToken cancellationToken = default);
}
