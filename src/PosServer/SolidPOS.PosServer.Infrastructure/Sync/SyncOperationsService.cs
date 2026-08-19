using System.Text.Json;
using Microsoft.Extensions.Logging;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Abstractions.Time;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Sync;
using SolidPOS.PosServer.Contracts.Sync;

namespace SolidPOS.PosServer.Infrastructure.Sync;

public sealed class SyncOperationsService : ISyncOperationsService
{
    private const int DefaultDeadLetterLimit = 50;
    private const int HardDeadLetterLimit = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly ISyncOperationsRepository _repository;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly ILogger<SyncOperationsService> _logger;

    public SyncOperationsService(
        ITenantContext tenantContext,
        IClock clock,
        ISyncOperationsRepository repository,
        IAuditEventWriter auditEventWriter,
        ILogger<SyncOperationsService> logger)
    {
        _tenantContext = tenantContext;
        _clock = clock;
        _repository = repository;
        _auditEventWriter = auditEventWriter;
        _logger = logger;
    }

    public async Task<SyncRuntimeStatusResponse?> GetStatusAsync(Guid? storeId, Guid? terminalId, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue)
        {
            _logger.LogWarning("Sync runtime status rejected because tenant context is missing");
            return null;
        }

        return await _repository.GetStatusAsync(_tenantContext.TenantId.Value, storeId, terminalId, _clock.UtcNow, cancellationToken);
    }

    public async Task<IReadOnlyCollection<SyncDeadLetterEventResponse>?> ListDeadLetterAsync(Guid? terminalId, int limit, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue)
        {
            _logger.LogWarning("Sync dead-letter list rejected because tenant context is missing");
            return null;
        }

        int safeLimit = limit <= 0 ? DefaultDeadLetterLimit : Math.Min(limit, HardDeadLetterLimit);
        return await _repository.ListDeadLetterAsync(_tenantContext.TenantId.Value, terminalId, safeLimit, cancellationToken);
    }

    public async Task<RetrySyncDeadLetterResponse?> RetryDeadLetterAsync(Guid inboxEventId, RetrySyncDeadLetterRequest request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || inboxEventId == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason))
        {
            _logger.LogWarning("Sync dead-letter retry rejected because tenant context, inbox event id or reason is invalid");
            return null;
        }

        RetrySyncDeadLetterResponse? response = await _repository.RetryDeadLetterAsync(_tenantContext.TenantId.Value, inboxEventId, request.Reason.Trim(), cancellationToken);
        if (response is null)
        {
            return null;
        }

        await _auditEventWriter.AppendAsync(
            _tenantContext.TenantId.Value,
            "sync.dead_letter.retry_scheduled",
            "sync_inbox_event",
            inboxEventId,
            null,
            JsonSerializer.SerializeToElement(response, JsonOptions),
            cancellationToken);

        return response;
    }

    public SyncContractResponse GetContract()
    {
        return new SyncContractResponse(
            4,
            500,
            500,
            [
                "pos.health_check",
                "sale.completed",
                "sale.voided",
                "inventory.adjustment.created",
                "cash.shift.opened",
                "cash.movement.created",
                "cash.shift.closed"
            ],
            [
                "received",
                "processing",
                "processed",
                "duplicate",
                "rejected",
                "retry_pending",
                "conflict",
                "dead_letter"
            ],
            ["use_server", "use_client", "merge", "compensate"]);
    }
}
