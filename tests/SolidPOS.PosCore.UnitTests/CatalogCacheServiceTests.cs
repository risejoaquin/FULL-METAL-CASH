using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Catalog;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

public sealed class CatalogCacheServiceTests
{
    [Fact]
    public async Task RefreshAsync_PersistsSellableProductsWithPriceSnapshot()
    {
        var repository = new CapturingRepository();
        var remote = new StubRemoteCatalogClient(new[]
        {
            new RemoteCatalogProductSnapshot(
                Guid.Parse("dd272b64-d450-4dd5-ace2-b17fc04ecc62"),
                null,
                "QSR-AMERICANO",
                "Americano 12oz",
                4500,
                "MXN",
                "active",
                new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero))
        });
        var service = new CatalogCacheService(repository, remote, new FixedClock());

        var result = await service.RefreshAsync("token");

        Assert.Equal(1, result.CachedProductCount);
        Assert.Single(repository.Products);
        Assert.Equal("QSR-AMERICANO", repository.Products[0].Sku);
        Assert.Equal("Americano 12oz", repository.Products[0].Name);
        Assert.Equal(4500, repository.Products[0].PriceCents);
        Assert.Equal("MXN", repository.Products[0].Currency);
    }

    [Fact]
    public async Task RefreshAsync_RejectsMissingAccessToken()
    {
        var service = new CatalogCacheService(new CapturingRepository(), new StubRemoteCatalogClient(Array.Empty<RemoteCatalogProductSnapshot>()), new FixedClock());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RefreshAsync(""));

        Assert.Contains("access token", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubRemoteCatalogClient : IRemoteCatalogClient
    {
        private readonly IReadOnlyList<RemoteCatalogProductSnapshot> _products;
        public StubRemoteCatalogClient(IReadOnlyList<RemoteCatalogProductSnapshot> products) => _products = products;
        public Task<IReadOnlyList<RemoteCatalogProductSnapshot>> GetCatalogProductsAsync(string accessToken, CancellationToken cancellationToken = default) => Task.FromResult(_products);
    }

    private sealed class CapturingRepository : ILocalPosRepository
    {
        public IReadOnlyList<LocalCatalogProduct> Products { get; private set; } = Array.Empty<LocalCatalogProduct>();
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveTerminalBindingAsync(TerminalBinding binding, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TerminalBinding?> GetTerminalBindingAsync(CancellationToken cancellationToken = default) => Task.FromResult<TerminalBinding?>(null);
        public Task SaveOfflineSaleAsync(OfflineSaleDraft sale, LocalOutboxEvent outboxEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveCatalogProductsAsync(IReadOnlyCollection<LocalCatalogProduct> products, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default)
        {
            Products = products.ToArray();
            return Task.CompletedTask;
        }
        public Task<LocalCatalogProduct?> GetCatalogProductBySkuAsync(string sku, CancellationToken cancellationToken = default) => Task.FromResult(Products.FirstOrDefault(product => product.Sku == sku));
        public Task<int> CountCatalogProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Products.Count);
        public Task SaveOutboxEventAsync(LocalOutboxEvent outboxEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
        public DateTimeOffset UtcNow { get; } = new(2026, 8, 19, 10, 30, 0, TimeSpan.Zero);
    }
}
