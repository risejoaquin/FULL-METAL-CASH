using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Sync;
using SolidPOS.PosServer.Contracts.Sync;

namespace SolidPOS.PosServer.Infrastructure.Sync;

public sealed class SyncPushService : ISyncPushService
{
    private const int MaxEventsPerBatch = 500;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITenantContext _tenantContext;
    private readonly ISyncPushRepository _repository;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly ILogger<SyncPushService> _logger;

    public SyncPushService(
        ITenantContext tenantContext,
        ISyncPushRepository repository,
        IAuditEventWriter auditEventWriter,
        ILogger<SyncPushService> logger)
    {
        _tenantContext = tenantContext;
        _repository = repository;
        _auditEventWriter = auditEventWriter;
        _logger = logger;
    }

    public async Task<SyncPushResponse?> PushAsync(SyncPushRequest request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || !_tenantContext.StoreId.HasValue || !_tenantContext.TerminalId.HasValue)
        {
            _logger.LogWarning("Sync push rejected because terminal runtime context is missing");
            return null;
        }

        if (request.BatchId == Guid.Empty)
        {
            _logger.LogWarning("Sync push rejected for tenant {TenantId} terminal {TerminalId}: batch id is empty", _tenantContext.TenantId.Value, _tenantContext.TerminalId.Value);
            return null;
        }

        if (request.Events is null)
        {
            _logger.LogWarning("Sync push rejected for tenant {TenantId} terminal {TerminalId}: events payload is missing", _tenantContext.TenantId.Value, _tenantContext.TerminalId.Value);
            return null;
        }

        if (request.Events.Count > MaxEventsPerBatch)
        {
            _logger.LogWarning(
                "Sync push rejected for tenant {TenantId} store {StoreId} terminal {TerminalId}: batch {BatchId} contains {EventCount} events and limit is {MaxEventsPerBatch}",
                _tenantContext.TenantId.Value,
                _tenantContext.StoreId.Value,
                _tenantContext.TerminalId.Value,
                request.BatchId,
                request.Events.Count,
                MaxEventsPerBatch);
            return null;
        }

        List<SyncPushEventEnvelope> validEvents = [];
        List<SyncPushEventResultResponse> rejectedEvents = [];
        HashSet<Guid> eventIdsInBatch = [];
        int sequenceNumber = 1;

        foreach (SyncPushEventRequest syncEvent in request.Events)
        {
            string? rejectionReason = ValidateEvent(syncEvent, eventIdsInBatch);
            if (rejectionReason is not null)
            {
                rejectedEvents.Add(new SyncPushEventResultResponse(syncEvent.EventId, "rejected", rejectionReason));
                sequenceNumber++;
                continue;
            }

            string payload = syncEvent.Payload.GetRawText();
            validEvents.Add(new SyncPushEventEnvelope(
                syncEvent.EventId,
                syncEvent.EventType.Trim(),
                syncEvent.EntityType.Trim(),
                syncEvent.EntityId,
                syncEvent.LocalOccurredAt,
                syncEvent.SchemaVersion,
                syncEvent.Payload.Clone(),
                sequenceNumber,
                ComputeSha256(payload)));
            sequenceNumber++;
        }

        IReadOnlyCollection<SyncPushEventResultResponse> persistedResults = validEvents.Count == 0
            ? []
            : await _repository.IngestAsync(
                _tenantContext.TenantId.Value,
                _tenantContext.StoreId.Value,
                _tenantContext.TerminalId.Value,
                request.BatchId,
                validEvents,
                cancellationToken);

        SyncPushEventResultResponse[] results = persistedResults
            .Concat(rejectedEvents)
            .ToArray();

        SyncPushResponse response = new(
            request.BatchId,
            _tenantContext.TenantId.Value,
            _tenantContext.StoreId.Value,
            _tenantContext.TerminalId.Value,
            request.Events.Count,
            results.Count(x => x.Status == "accepted"),
            results.Count(x => x.Status == "duplicate"),
            results.Count(x => x.Status == "rejected"),
            results);

        _logger.LogInformation(
            "Sync push batch {BatchId} ingested for tenant {TenantId} store {StoreId} terminal {TerminalId}: received {ReceivedCount}, accepted {AcceptedCount}, duplicates {DuplicateCount}, rejected {RejectedCount}",
            response.BatchId,
            response.TenantId,
            response.StoreId,
            response.TerminalId,
            response.ReceivedCount,
            response.AcceptedCount,
            response.DuplicateCount,
            response.RejectedCount);

        await _auditEventWriter.AppendAsync(
            response.TenantId,
            "sync.push.ingested",
            "sync_batch",
            response.BatchId,
            null,
            JsonSerializer.SerializeToElement(response, JsonOptions),
            cancellationToken);

        return response;
    }

    private static string? ValidateEvent(SyncPushEventRequest syncEvent, ISet<Guid> eventIdsInBatch)
    {
        if (syncEvent.EventId == Guid.Empty)
        {
            return "event_id_required";
        }

        if (!eventIdsInBatch.Add(syncEvent.EventId))
        {
            return "duplicate_event_in_batch";
        }

        if (string.IsNullOrWhiteSpace(syncEvent.EventType))
        {
            return "event_type_required";
        }

        if (string.IsNullOrWhiteSpace(syncEvent.EntityType))
        {
            return "entity_type_required";
        }

        if (syncEvent.SchemaVersion <= 0)
        {
            return "schema_version_invalid";
        }

        if (syncEvent.LocalOccurredAt == default)
        {
            return "local_occurred_at_required";
        }

        if (syncEvent.Payload.ValueKind is System.Text.Json.JsonValueKind.Undefined)
        {
            return "payload_required";
        }

        return null;
    }

    private static string ComputeSha256(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
