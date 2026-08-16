using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Abstractions.Time;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Sync;
using SolidPOS.PosServer.Contracts.Sync;

namespace SolidPOS.PosServer.Infrastructure.Sync;

public sealed class SyncConflictService : ISyncConflictService
{
    private const int DefaultLimit = 50;
    private const int HardLimit = 200;
    private static readonly HashSet<string> AllowedStatuses = ["pending", "resolved", "ignored"];
    private static readonly HashSet<string> AllowedStrategies = ["use_server", "use_client", "merge", "compensate"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITenantContext _tenantContext;
    private readonly ISyncConflictRepository _repository;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly IClock _clock;
    private readonly ILogger<SyncConflictService> _logger;

    public SyncConflictService(
        ITenantContext tenantContext,
        ISyncConflictRepository repository,
        IAuditEventWriter auditEventWriter,
        IClock clock,
        ILogger<SyncConflictService> logger)
    {
        _tenantContext = tenantContext;
        _repository = repository;
        _auditEventWriter = auditEventWriter;
        _clock = clock;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<SyncConflictResponse>?> ListAsync(string? status, int limit, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue)
        {
            _logger.LogWarning("Sync conflicts rejected because tenant context is missing");
            return null;
        }

        if (!string.IsNullOrWhiteSpace(status) && !AllowedStatuses.Contains(status.Trim()))
        {
            _logger.LogWarning("Sync conflicts rejected for tenant {TenantId}: status {Status} is invalid", _tenantContext.TenantId.Value, status);
            return null;
        }

        int effectiveLimit = limit <= 0 ? DefaultLimit : Math.Min(limit, HardLimit);
        return await _repository.ListAsync(_tenantContext.TenantId.Value, status?.Trim(), effectiveLimit, cancellationToken);
    }

    public async Task<SyncConflictResponse?> ResolveAsync(Guid conflictId, ResolveSyncConflictRequest request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || conflictId == Guid.Empty || request is null)
        {
            return null;
        }

        string strategy = request.ResolutionStrategy?.Trim() ?? string.Empty;
        if (!AllowedStrategies.Contains(strategy))
        {
            _logger.LogWarning("Sync conflict resolution rejected for tenant {TenantId}: strategy {Strategy} is invalid", _tenantContext.TenantId.Value, strategy);
            return null;
        }

        if ((strategy == "merge" || strategy == "compensate") && !request.ResolvedPayload.HasValue)
        {
            _logger.LogWarning("Sync conflict resolution rejected for tenant {TenantId}: strategy {Strategy} requires resolvedPayload", _tenantContext.TenantId.Value, strategy);
            return null;
        }

        SyncConflictResponse? resolved = await _repository.ResolveAsync(
            _tenantContext.TenantId.Value,
            conflictId,
            _tenantContext.UserId,
            request,
            cancellationToken);

        if (resolved is null)
        {
            return null;
        }

        await _auditEventWriter.AppendAsync(
            _tenantContext.TenantId.Value,
            "sync.conflict.resolved",
            "sync_conflict",
            conflictId,
            null,
            JsonSerializer.SerializeToElement(resolved, JsonOptions),
            cancellationToken);

        return resolved;
    }

    public async Task<SyncBootstrapResponse?> BootstrapAsync(CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || !_tenantContext.StoreId.HasValue || !_tenantContext.TerminalId.HasValue)
        {
            _logger.LogWarning("Sync bootstrap rejected because terminal runtime context is missing");
            return null;
        }

        DateTimeOffset serverTime = _clock.UtcNow;
        string cursor = serverTime.ToString("O", CultureInfo.InvariantCulture);
        SyncBootstrapResponse response = await _repository.ReadBootstrapAsync(
            _tenantContext.TenantId.Value,
            _tenantContext.StoreId.Value,
            _tenantContext.TerminalId.Value,
            serverTime,
            cursor,
            cancellationToken);

        _logger.LogInformation(
            "Sync bootstrap generated for tenant {TenantId} store {StoreId} terminal {TerminalId}",
            response.TenantId,
            response.StoreId,
            response.TerminalId);

        return response;
    }
}
