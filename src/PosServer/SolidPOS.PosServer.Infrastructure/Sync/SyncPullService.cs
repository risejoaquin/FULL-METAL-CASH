using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Abstractions.Time;
using SolidPOS.PosServer.Application.Catalog;
using SolidPOS.PosServer.Application.Sync;
using SolidPOS.PosServer.Application.Tenants;
using SolidPOS.PosServer.Contracts.Catalog;
using SolidPOS.PosServer.Contracts.Sync;
using SolidPOS.PosServer.Contracts.Tenants;
using SolidPOS.PosServer.Contracts.Terminals;

namespace SolidPOS.PosServer.Infrastructure.Sync;

public sealed class SyncPullService : ISyncPullService
{
    private const int DefaultLimit = 100;
    private const int HardLimit = 500;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITenantContext _tenantContext;
    private readonly ISyncPullRepository _repository;
    private readonly ITenantConfigService _tenantConfigService;
    private readonly ICatalogRuntimeService _catalogRuntimeService;
    private readonly IClock _clock;
    private readonly ILogger<SyncPullService> _logger;

    public SyncPullService(
        ITenantContext tenantContext,
        ISyncPullRepository repository,
        ITenantConfigService tenantConfigService,
        ICatalogRuntimeService catalogRuntimeService,
        IClock clock,
        ILogger<SyncPullService> logger)
    {
        _tenantContext = tenantContext;
        _repository = repository;
        _tenantConfigService = tenantConfigService;
        _catalogRuntimeService = catalogRuntimeService;
        _clock = clock;
        _logger = logger;
    }

    public async Task<SyncPullResponse?> PullAsync(string? cursor, int limit, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || !_tenantContext.StoreId.HasValue || !_tenantContext.TerminalId.HasValue)
        {
            _logger.LogWarning("Sync pull rejected because terminal runtime context is missing");
            return null;
        }

        DateTimeOffset? changedAfter = ParseCursor(cursor);
        if (!string.IsNullOrWhiteSpace(cursor) && !changedAfter.HasValue)
        {
            _logger.LogWarning("Sync pull rejected for tenant {TenantId} terminal {TerminalId}: cursor is invalid", _tenantContext.TenantId.Value, _tenantContext.TerminalId.Value);
            return null;
        }

        int effectiveLimit = limit <= 0 ? DefaultLimit : Math.Min(limit, HardLimit);
        List<SyncPullChangeResponse> changes = [];

        if (!changedAfter.HasValue)
        {
            changes.AddRange(await CreateBootstrapChangesAsync(cancellationToken));
        }

        IReadOnlyCollection<SyncPullChangeResponse> deltaChanges = await _repository.ReadChangesAsync(
            _tenantContext.TenantId.Value,
            _tenantContext.StoreId.Value,
            _tenantContext.TerminalId.Value,
            changedAfter,
            effectiveLimit,
            cancellationToken);
        changes.AddRange(deltaChanges);

        DateTimeOffset serverTime = _clock.UtcNow;
        string nextCursor = serverTime.ToString("O", CultureInfo.InvariantCulture);

        SyncPullResponse response = new(
            _tenantContext.TenantId.Value,
            _tenantContext.StoreId.Value,
            _tenantContext.TerminalId.Value,
            serverTime,
            cursor,
            nextCursor,
            deltaChanges.Count >= effectiveLimit,
            new TerminalRuntimeContextResponse(_tenantContext.TenantId.Value, _tenantContext.StoreId.Value, _tenantContext.TerminalId.Value),
            changes.OrderBy(x => x.ChangedAt).ThenBy(x => x.Id).ToArray());

        _logger.LogInformation(
            "Sync pull completed for tenant {TenantId} store {StoreId} terminal {TerminalId}: cursor {Cursor}, changes {ChangeCount}, hasMore {HasMore}",
            response.TenantId,
            response.StoreId,
            response.TerminalId,
            cursor,
            response.Changes.Count,
            response.HasMore);

        return response;
    }

    private async Task<IReadOnlyCollection<SyncPullChangeResponse>> CreateBootstrapChangesAsync(CancellationToken cancellationToken)
    {
        List<SyncPullChangeResponse> changes = [];

        TenantConfigResponse? config = await _tenantConfigService.GetCurrentAsync(cancellationToken);
        if (config is not null)
        {
            changes.Add(CreateBootstrapChange("tenant.config", config.TenantId, config.Version, config.UpdatedAt, config));
        }

        CatalogSnapshotResponse? catalog = await _catalogRuntimeService.GetSnapshotAsync(cancellationToken);
        if (catalog is not null)
        {
            changes.Add(CreateBootstrapChange("tenant.catalog", catalog.TenantId, 1, catalog.GeneratedAt, catalog));
        }

        JsonElement accessSnapshot = await _repository.ReadAccessSnapshotAsync(_tenantContext.TenantId!.Value, cancellationToken);
        changes.Add(CreateBootstrapChange("tenant.access", _tenantContext.TenantId.Value, 1, _clock.UtcNow, accessSnapshot));

        TerminalRuntimeContextResponse terminal = new(_tenantContext.TenantId.Value, _tenantContext.StoreId!.Value, _tenantContext.TerminalId!.Value);
        changes.Add(CreateBootstrapChange("terminal.runtime", terminal.TerminalId, 1, _clock.UtcNow, terminal, _tenantContext.StoreId.Value));

        return changes;
    }

    private static SyncPullChangeResponse CreateBootstrapChange<T>(
        string entityType,
        Guid entityId,
        long entityVersion,
        DateTimeOffset changedAt,
        T payload,
        Guid? storeId = null)
    {
        return new SyncPullChangeResponse(
            Guid.NewGuid(),
            entityType,
            entityId,
            "snapshot",
            entityVersion,
            changedAt,
            JsonSerializer.SerializeToElement(payload, JsonOptions),
            storeId,
            null);
    }

    private static DateTimeOffset? ParseCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        return DateTimeOffset.TryParse(cursor, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
            ? parsed
            : null;
    }
}
