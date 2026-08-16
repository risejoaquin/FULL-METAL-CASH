using System.Text.Json;
using Microsoft.Extensions.Logging;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Cash;
using SolidPOS.PosServer.Application.Inventory;
using SolidPOS.PosServer.Application.Sales;
using SolidPOS.PosServer.Application.Sync;
using SolidPOS.PosServer.Contracts.Cash;
using SolidPOS.PosServer.Contracts.Inventory;
using SolidPOS.PosServer.Contracts.Sales;
using SolidPOS.PosServer.Contracts.Sync;

namespace SolidPOS.PosServer.Infrastructure.Sync;

public sealed class SyncEventProcessingService : ISyncEventProcessingService
{
    private const int DefaultMaxEvents = 100;
    private const int HardMaxEvents = 500;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ITenantContext _tenantContext;
    private readonly ISyncEventRepository _repository;
    private readonly ISyncConflictRepository _conflictRepository;
    private readonly ISalesService _salesService;
    private readonly IInventoryAdjustmentService _inventoryAdjustmentService;
    private readonly ICashShiftService _cashShiftService;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly ILogger<SyncEventProcessingService> _logger;

    public SyncEventProcessingService(
        ITenantContext tenantContext,
        ISyncEventRepository repository,
        ISyncConflictRepository conflictRepository,
        ISalesService salesService,
        IInventoryAdjustmentService inventoryAdjustmentService,
        ICashShiftService cashShiftService,
        IAuditEventWriter auditEventWriter,
        ILogger<SyncEventProcessingService> logger)
    {
        _tenantContext = tenantContext;
        _repository = repository;
        _conflictRepository = conflictRepository;
        _salesService = salesService;
        _inventoryAdjustmentService = inventoryAdjustmentService;
        _cashShiftService = cashShiftService;
        _auditEventWriter = auditEventWriter;
        _logger = logger;
    }

    public async Task<SyncProcessResponse?> ProcessPendingAsync(SyncProcessRequest request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || !_tenantContext.StoreId.HasValue || !_tenantContext.TerminalId.HasValue)
        {
            _logger.LogWarning("Sync processing rejected because terminal runtime context is missing");
            return null;
        }

        int maxEvents = request.MaxEvents <= 0
            ? DefaultMaxEvents
            : Math.Min(request.MaxEvents, HardMaxEvents);

        IReadOnlyCollection<SyncInboxEvent> events = await _repository.ReadPendingAsync(
            _tenantContext.TenantId.Value,
            _tenantContext.StoreId.Value,
            _tenantContext.TerminalId.Value,
            request.BatchId,
            maxEvents,
            cancellationToken);

        List<SyncProcessEventResultResponse> results = [];
        foreach (SyncInboxEvent syncEvent in events)
        {
            SyncProcessEventResultResponse result = await ProcessEventAsync(syncEvent, cancellationToken);
            results.Add(result);
        }

        SyncProcessResponse response = new(
            _tenantContext.TenantId.Value,
            _tenantContext.StoreId.Value,
            _tenantContext.TerminalId.Value,
            request.BatchId,
            events.Count,
            results.Count(x => x.Status == "processed"),
            results.Count(x => x.Status == "rejected"),
            results);

        _logger.LogInformation(
            "Sync processing completed for tenant {TenantId} store {StoreId} terminal {TerminalId} batch {BatchId}: received {ReceivedCount}, processed {ProcessedCount}, rejected {RejectedCount}",
            response.TenantId,
            response.StoreId,
            response.TerminalId,
            response.BatchId,
            response.ReceivedCount,
            response.ProcessedCount,
            response.RejectedCount);

        await _auditEventWriter.AppendAsync(
            response.TenantId,
            "sync.process.completed",
            "sync_batch",
            response.BatchId,
            null,
            JsonSerializer.SerializeToElement(response, JsonOptions),
            cancellationToken);

        return response;
    }

    private async Task<SyncProcessEventResultResponse> ProcessEventAsync(SyncInboxEvent syncEvent, CancellationToken cancellationToken)
    {
        try
        {
            JsonElement result = syncEvent.EventType switch
            {
                "pos.health_check" => CreateResult("health_check_acknowledged", syncEvent.EventId),
                "sale.completed" => await ProcessSaleCompletedAsync(syncEvent, cancellationToken),
                "sale.voided" => await ProcessSaleVoidedAsync(syncEvent, cancellationToken),
                "inventory.adjustment.created" => await ProcessInventoryAdjustmentAsync(syncEvent, cancellationToken),
                "cash.shift.opened" => await ProcessCashShiftOpenedAsync(syncEvent, cancellationToken),
                "cash.movement.created" => await ProcessCashMovementCreatedAsync(syncEvent, cancellationToken),
                "cash.shift.closed" => await ProcessCashShiftClosedAsync(syncEvent, cancellationToken),
                _ => throw new SyncEventProcessingException("unsupported_event_type", $"Unsupported sync event type '{syncEvent.EventType}'.")
            };

            await _repository.MarkProcessedAsync(syncEvent.TenantId, syncEvent.Id, result, cancellationToken);
            _logger.LogInformation("Sync event {EventId} processed as {EventType}", syncEvent.EventId, syncEvent.EventType);
            return new SyncProcessEventResultResponse(syncEvent.Id, syncEvent.EventId, syncEvent.EventType, "processed", null, null);
        }
        catch (SyncEventProcessingException exception)
        {
            if (ShouldCreateConflict(exception.ErrorCode))
            {
                JsonElement serverPayload = JsonSerializer.SerializeToElement(new
                {
                    errorCode = exception.ErrorCode,
                    errorMessage = exception.Message,
                    serverStatus = "requires_resolution"
                }, JsonOptions);

                Guid conflictId = await _conflictRepository.CreateAsync(
                    syncEvent.TenantId,
                    syncEvent.TerminalId,
                    syncEvent.Id,
                    syncEvent.EventId,
                    syncEvent.EntityType,
                    syncEvent.EntityId ?? syncEvent.EventId,
                    null,
                    null,
                    syncEvent.Payload,
                    serverPayload,
                    cancellationToken);

                await _repository.MarkConflictAsync(syncEvent.TenantId, syncEvent.Id, conflictId, exception.ErrorCode, exception.Message, cancellationToken);
                _logger.LogWarning(
                    "Sync event {EventId} created conflict {ConflictId} as {EventType}: {ErrorCode} {ErrorMessage}",
                    syncEvent.EventId,
                    conflictId,
                    syncEvent.EventType,
                    exception.ErrorCode,
                    exception.Message);
                return new SyncProcessEventResultResponse(syncEvent.Id, syncEvent.EventId, syncEvent.EventType, "conflict", exception.ErrorCode, exception.Message);
            }

            await _repository.MarkRejectedAsync(syncEvent.TenantId, syncEvent.Id, exception.ErrorCode, exception.Message, cancellationToken);
            _logger.LogWarning(
                "Sync event {EventId} rejected as {EventType}: {ErrorCode} {ErrorMessage}",
                syncEvent.EventId,
                syncEvent.EventType,
                exception.ErrorCode,
                exception.Message);
            return new SyncProcessEventResultResponse(syncEvent.Id, syncEvent.EventId, syncEvent.EventType, "rejected", exception.ErrorCode, exception.Message);
        }
        catch (Exception exception)
        {
            await _repository.MarkRejectedAsync(syncEvent.TenantId, syncEvent.Id, "processing_exception", exception.Message, cancellationToken);
            _logger.LogError(exception, "Sync event {EventId} failed while processing as {EventType}", syncEvent.EventId, syncEvent.EventType);
            return new SyncProcessEventResultResponse(syncEvent.Id, syncEvent.EventId, syncEvent.EventType, "rejected", "processing_exception", exception.Message);
        }
    }

    private async Task<JsonElement> ProcessSaleCompletedAsync(SyncInboxEvent syncEvent, CancellationToken cancellationToken)
    {
        CreateSaleRequest request = DeserializePayload<CreateSaleRequest>(syncEvent);
        SaleResponse? sale = await _salesService.CreateAsync(request, cancellationToken);
        if (sale is null)
        {
            throw new SyncEventProcessingException("sale_rejected", "Sale event failed domain validation.");
        }

        return CreateResult("sale_completed", sale.Id);
    }

    private async Task<JsonElement> ProcessSaleVoidedAsync(SyncInboxEvent syncEvent, CancellationToken cancellationToken)
    {
        SyncSaleVoidedPayload payload = DeserializePayload<SyncSaleVoidedPayload>(syncEvent);
        VoidSaleRequest request = new(payload.VoidedByUserId, payload.Reason, payload.OccurredAt);
        SaleResponse? sale = payload.SaleId.HasValue
            ? await _salesService.VoidAsync(payload.SaleId.Value, request, cancellationToken)
            : payload.LocalSaleId.HasValue
                ? await _salesService.VoidByLocalSaleIdAsync(payload.LocalSaleId.Value, request, cancellationToken)
                : throw new SyncEventProcessingException("sale_void_identifier_required", "Sale void event requires saleId or localSaleId.");
        if (sale is null)
        {
            throw new SyncEventProcessingException("sale_void_rejected", "Sale void event failed domain validation.");
        }

        return CreateResult("sale_voided", sale.Id);
    }

    private async Task<JsonElement> ProcessInventoryAdjustmentAsync(SyncInboxEvent syncEvent, CancellationToken cancellationToken)
    {
        CreateInventoryAdjustmentRequest request = DeserializePayload<CreateInventoryAdjustmentRequest>(syncEvent);
        InventoryAdjustmentResponse? adjustment = await _inventoryAdjustmentService.CreateAsync(request, cancellationToken);
        if (adjustment is null)
        {
            throw new SyncEventProcessingException("inventory_adjustment_rejected", "Inventory adjustment event failed domain validation.");
        }

        return CreateResult("inventory_adjustment_created", adjustment.Id);
    }

    private async Task<JsonElement> ProcessCashShiftOpenedAsync(SyncInboxEvent syncEvent, CancellationToken cancellationToken)
    {
        OpenCashShiftRequest request = DeserializePayload<OpenCashShiftRequest>(syncEvent);
        CashShiftResponse? shift = await _cashShiftService.OpenAsync(request, cancellationToken);
        if (shift is null)
        {
            throw new SyncEventProcessingException("cash_shift_open_rejected", "Cash shift open event failed domain validation.");
        }

        return CreateResult("cash_shift_opened", shift.Id);
    }

    private async Task<JsonElement> ProcessCashMovementCreatedAsync(SyncInboxEvent syncEvent, CancellationToken cancellationToken)
    {
        SyncCashMovementPayload payload = DeserializePayload<SyncCashMovementPayload>(syncEvent);
        CashMovementResponse? movement = await _cashShiftService.CreateMovementAsync(
            payload.CashShiftId,
            new CreateCashMovementRequest(payload.MovementType, payload.AmountCents, payload.Reason, payload.CreatedByUserId, payload.AuthorizedByUserId),
            cancellationToken);
        if (movement is null)
        {
            throw new SyncEventProcessingException("cash_movement_rejected", "Cash movement event failed domain validation.");
        }

        return CreateResult("cash_movement_created", movement.Id);
    }

    private async Task<JsonElement> ProcessCashShiftClosedAsync(SyncInboxEvent syncEvent, CancellationToken cancellationToken)
    {
        SyncCashShiftClosedPayload payload = DeserializePayload<SyncCashShiftClosedPayload>(syncEvent);
        CashShiftResponse? shift = await _cashShiftService.CloseAsync(
            payload.CashShiftId,
            new CloseCashShiftRequest(payload.ClosedByUserId, payload.CountedCashCents),
            cancellationToken);
        if (shift is null)
        {
            throw new SyncEventProcessingException("cash_shift_close_rejected", "Cash shift close event failed domain validation.");
        }

        return CreateResult("cash_shift_closed", shift.Id);
    }

    private static bool ShouldCreateConflict(string errorCode)
    {
        return errorCode is
            "sale_rejected" or
            "sale_void_rejected" or
            "inventory_adjustment_rejected" or
            "cash_shift_open_rejected" or
            "cash_movement_rejected" or
            "cash_shift_close_rejected";
    }

    private static T DeserializePayload<T>(SyncInboxEvent syncEvent)
    {
        T? request = syncEvent.Payload.Deserialize<T>(JsonOptions);
        return request ?? throw new SyncEventProcessingException("payload_invalid", $"Payload for event '{syncEvent.EventType}' is invalid.");
    }

    private static JsonElement CreateResult(string status, Guid id)
    {
        return JsonSerializer.SerializeToElement(new { status, id }, JsonOptions);
    }

    private sealed record SyncSaleVoidedPayload(Guid? SaleId, Guid? LocalSaleId, Guid VoidedByUserId, string Reason, DateTimeOffset OccurredAt);

    private sealed record SyncCashMovementPayload(Guid CashShiftId, string MovementType, long AmountCents, string Reason, Guid CreatedByUserId, Guid? AuthorizedByUserId);

    private sealed record SyncCashShiftClosedPayload(Guid CashShiftId, Guid ClosedByUserId, long CountedCashCents);

    private sealed class SyncEventProcessingException : Exception
    {
        public SyncEventProcessingException(string errorCode, string message)
            : base(message)
        {
            ErrorCode = errorCode;
        }

        public string ErrorCode { get; }
    }
}
