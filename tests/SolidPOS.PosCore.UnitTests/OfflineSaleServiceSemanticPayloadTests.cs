using System.Text.Json;
using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.OfflineSales;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

public sealed class OfflineSaleServiceSemanticPayloadTests
{
    [Fact]
    public async Task CreateOfflineSaleAsync_generates_server_create_sale_payload_shape()
    {
        var repository = new CapturingRepository();
        var service = new OfflineSaleService(repository, new FixedClock());
        var localSaleId = Guid.NewGuid();
        var cashierUserId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var localPaymentId = Guid.NewGuid();
        var sale = new OfflineSaleDraft(
            localSaleId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero),
            new[] { new OfflineSaleLineDraft(productId, null, "QSR-AMERICANO", "Americano 12oz", 1, 4500) },
            new[] { new OfflineSalePaymentDraft("cash", 4500, localPaymentId) },
            "MXN",
            cashierUserId);

        LocalOutboxEvent outboxEvent = await service.CreateOfflineSaleAsync(sale);

        Assert.Equal("sale.completed", outboxEvent.EventType);
        using var document = JsonDocument.Parse(outboxEvent.PayloadJson);
        JsonElement root = document.RootElement;
        Assert.Equal(localSaleId, root.GetProperty("localSaleId").GetGuid());
        Assert.Equal(cashierUserId, root.GetProperty("cashierUserId").GetGuid());
        Assert.True(root.TryGetProperty("occurredAt", out _));
        Assert.True(root.TryGetProperty("localCreatedAt", out _));
        Assert.Equal(0, root.GetProperty("tipCents").GetInt64());
        Assert.Equal(productId, root.GetProperty("lines")[0].GetProperty("productId").GetGuid());
        Assert.Equal("1", root.GetProperty("lines")[0].GetProperty("quantity").GetString());
        Assert.Equal(localPaymentId, root.GetProperty("payments")[0].GetProperty("localPaymentId").GetGuid());
        Assert.Equal("cash", root.GetProperty("payments")[0].GetProperty("methodCode").GetString());
        Assert.NotNull(repository.LastOutboxEvent);
    }

    [Fact]
    public async Task CreateOfflineSaleAsync_rejects_missing_cashier_for_semantic_sync()
    {
        var service = new OfflineSaleService(new CapturingRepository(), new FixedClock());
        var sale = new OfflineSaleDraft(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            new[] { new OfflineSaleLineDraft(Guid.NewGuid(), null, "QSR-AMERICANO", "Americano 12oz", 1, 4500) },
            new[] { new OfflineSalePaymentDraft("cash", 4500) },
            "MXN");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateOfflineSaleAsync(sale));

        Assert.Contains("cashier", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CapturingRepository : ILocalPosRepository
    {
        public LocalOutboxEvent? LastOutboxEvent { get; private set; }
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveTerminalBindingAsync(TerminalBinding binding, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TerminalBinding?> GetTerminalBindingAsync(CancellationToken cancellationToken = default) => Task.FromResult<TerminalBinding?>(null);
        public Task SaveOfflineSaleAsync(OfflineSaleDraft sale, LocalOutboxEvent outboxEvent, CancellationToken cancellationToken = default)
        {
            LastOutboxEvent = outboxEvent;
            return Task.CompletedTask;
        }
        public Task SaveOutboxEventAsync(LocalOutboxEvent outboxEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveCatalogProductsAsync(IReadOnlyCollection<LocalCatalogProduct> products, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LocalCatalogProduct?> GetCatalogProductBySkuAsync(string sku, CancellationToken cancellationToken = default) => Task.FromResult<LocalCatalogProduct?>(null);

        public Task SaveOfflineSaleWithInventoryAsync(OfflineSaleDraft sale, LocalOutboxEvent outboxEvent, IReadOnlyCollection<LocalInventoryMovement> movements, CancellationToken cancellationToken = default) => SaveOfflineSaleAsync(sale, outboxEvent, cancellationToken);
        public Task SaveInventoryRecipeCacheAsync(IReadOnlyCollection<LocalInventoryRecipe> recipes, IReadOnlyCollection<LocalInventoryRecipeItem> recipeItems, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LocalInventoryRecipe?> GetRecipeForOutputAsync(Guid productId, Guid? variantId, CancellationToken cancellationToken = default) => Task.FromResult<LocalInventoryRecipe?>(null);
        public Task<IReadOnlyList<LocalInventoryRecipeItem>> GetRecipeItemsAsync(Guid recipeId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalInventoryRecipeItem>>(Array.Empty<LocalInventoryRecipeItem>());
        public Task<IReadOnlyList<LocalInventoryMovement>> GetInventoryMovementsByLocalSaleIdAsync(Guid localSaleId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalInventoryMovement>>(Array.Empty<LocalInventoryMovement>());
        public Task<int> CountInventoryRecipesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> CountInventoryRecipeItemsAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> CountCatalogProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<IReadOnlyList<LocalOutboxEvent>> GetPendingOutboxEventsAsync(int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalOutboxEvent>>(Array.Empty<LocalOutboxEvent>());
        public Task<LocalOutboxEvent?> GetLatestOutboxEventByStatusAsync(LocalOutboxStatus status, CancellationToken cancellationToken = default) => Task.FromResult<LocalOutboxEvent?>(null);
        public Task MarkOutboxSyncedAsync(IEnumerable<Guid> eventIds, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MarkOutboxFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResetOutboxEventToPendingAsync(Guid eventId, string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> RetryFailedOutboxEventsAsync(int maxAttempts, string reason, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task SaveSyncAcknowledgementsAsync(IEnumerable<LocalSyncAcknowledgement> acknowledgements, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> CountOutboxByStatusAsync(LocalOutboxStatus status, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);
    }
}
