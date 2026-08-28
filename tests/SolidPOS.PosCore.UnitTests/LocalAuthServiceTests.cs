using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Auth;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

public sealed class LocalAuthServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("0ce5bbd0-528b-4aee-9fe3-93df001a4fde");
    private static readonly Guid StoreId = Guid.Parse("8e446c29-e9ad-41ed-a738-125aff7608b6");
    private static readonly Guid UserId = Guid.Parse("dd304a2f-d1e1-4c53-8cc3-1978d6c2aabe");

    [Fact]
    public async Task LoginAsync_WithCachedUserWithinOfflineWindow_CreatesLocalSession()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 20, 4, 0, 0, TimeSpan.Zero));
        var repository = new AuthRepository();
        await repository.SaveLocalUserAsync(new LocalUser(UserId, TenantId, StoreId, "admin@micafeteria.com", "Admin", LocalPasswordHasher.Hash("AdminSeguro123!"), "owner", true, clock.UtcNow.AddHours(-1), 72), new[] { "sales.create" });
        var service = new LocalAuthService(repository, clock);

        var session = await service.LoginAsync("admin@micafeteria.com", "AdminSeguro123!");

        Assert.Equal(UserId, session.UserId);
        Assert.Equal("active", session.Status);
        Assert.True(await repository.LocalUserHasPermissionAsync(UserId, "sales.create"));
    }

    [Fact]
    public async Task LoginAsync_BlocksWhenOfflineWindowExpired()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 20, 4, 0, 0, TimeSpan.Zero));
        var repository = new AuthRepository();
        await repository.SaveLocalUserAsync(new LocalUser(UserId, TenantId, StoreId, "admin@micafeteria.com", "Admin", LocalPasswordHasher.Hash("AdminSeguro123!"), "owner", true, clock.UtcNow.AddHours(-73), 72), new[] { "sales.create" });
        var service = new LocalAuthService(repository, clock);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoginAsync("admin@micafeteria.com", "AdminSeguro123!"));

        Assert.Contains("offline window", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(repository.AuditEvents, x => x.EventType == "local.auth.blocked_offline_window");
    }

    [Fact]
    public async Task RequirePermissionAsync_RejectsMissingLocalPermission()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 20, 4, 0, 0, TimeSpan.Zero));
        var repository = new AuthRepository();
        await repository.SaveLocalUserAsync(new LocalUser(UserId, TenantId, StoreId, "admin@micafeteria.com", "Admin", LocalPasswordHasher.Hash("AdminSeguro123!"), "owner", true, clock.UtcNow, 72), new[] { "sales.read" });
        var service = new LocalAuthService(repository, clock);
        var session = await service.LoginAsync("admin@micafeteria.com", "AdminSeguro123!");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequirePermissionAsync(session.SessionId, "sales.create"));

        Assert.Contains("permission denied", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class AuthRepository : ILocalPosRepository
    {
        private readonly Dictionary<string, LocalUser> _usersByEmail = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Guid, HashSet<string>> _permissions = new();
        private readonly Dictionary<Guid, LocalSession> _sessions = new();
        public List<LocalAuditEvent> AuditEvents { get; } = new();

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveTerminalBindingAsync(TerminalBinding binding, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TerminalBinding?> GetTerminalBindingAsync(CancellationToken cancellationToken = default) => Task.FromResult<TerminalBinding?>(null);
        public Task SaveOfflineSaleAsync(OfflineSaleDraft sale, LocalOutboxEvent outboxEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveOfflineSaleWithInventoryAsync(OfflineSaleDraft sale, LocalOutboxEvent outboxEvent, IReadOnlyCollection<LocalInventoryMovement> movements, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveCatalogProductsAsync(IReadOnlyCollection<LocalCatalogProduct> products, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveInventoryRecipeCacheAsync(IReadOnlyCollection<LocalInventoryRecipe> recipes, IReadOnlyCollection<LocalInventoryRecipeItem> recipeItems, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LocalInventoryRecipe?> GetRecipeForOutputAsync(Guid productId, Guid? variantId, CancellationToken cancellationToken = default) => Task.FromResult<LocalInventoryRecipe?>(null);
        public Task<IReadOnlyList<LocalInventoryRecipeItem>> GetRecipeItemsAsync(Guid recipeId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalInventoryRecipeItem>>(Array.Empty<LocalInventoryRecipeItem>());
        public Task<IReadOnlyList<LocalInventoryMovement>> GetInventoryMovementsByLocalSaleIdAsync(Guid localSaleId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalInventoryMovement>>(Array.Empty<LocalInventoryMovement>());
        public Task<int> CountInventoryRecipesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> CountInventoryRecipeItemsAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<LocalCatalogProduct?> GetCatalogProductBySkuAsync(string sku, CancellationToken cancellationToken = default) => Task.FromResult<LocalCatalogProduct?>(null);
        public Task<int> CountCatalogProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task SaveOutboxEventAsync(LocalOutboxEvent outboxEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<LocalOutboxEvent>> GetPendingOutboxEventsAsync(int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalOutboxEvent>>(Array.Empty<LocalOutboxEvent>());
        public Task<LocalOutboxEvent?> GetLatestOutboxEventByStatusAsync(LocalOutboxStatus status, CancellationToken cancellationToken = default) => Task.FromResult<LocalOutboxEvent?>(null);
        public Task MarkOutboxSyncedAsync(IEnumerable<Guid> eventIds, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MarkOutboxFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResetOutboxEventToPendingAsync(Guid eventId, string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> RetryFailedOutboxEventsAsync(int maxAttempts, string reason, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task SaveSyncAcknowledgementsAsync(IEnumerable<LocalSyncAcknowledgement> acknowledgements, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> CountOutboxByStatusAsync(LocalOutboxStatus status, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task SaveLocalUserAsync(LocalUser user, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default)
        {
            _usersByEmail[user.Email] = user;
            _permissions[user.UserId] = permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return Task.CompletedTask;
        }
        public Task<LocalUser?> GetLocalUserByEmailAsync(string email, CancellationToken cancellationToken = default) => Task.FromResult(_usersByEmail.TryGetValue(email, out var user) ? user : null);
        public Task CreateLocalSessionAsync(LocalSession session, CancellationToken cancellationToken = default) { _sessions[session.SessionId] = session; return Task.CompletedTask; }
        public Task<LocalSession?> GetLocalSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) => Task.FromResult(_sessions.TryGetValue(sessionId, out var session) ? session : null);
        public Task<bool> LocalUserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken = default) => Task.FromResult(_permissions.TryGetValue(userId, out var set) && set.Contains(permissionCode));
        public Task LogLocalAuditEventAsync(LocalAuditEvent auditEvent, CancellationToken cancellationToken = default) { AuditEvents.Add(auditEvent); return Task.CompletedTask; }
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }
}
