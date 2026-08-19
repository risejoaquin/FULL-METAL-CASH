using System.Text.Json;
using Microsoft.Extensions.Logging;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Cash;
using SolidPOS.PosServer.Contracts.Cash;

namespace SolidPOS.PosServer.Infrastructure.Cash;

public sealed class CashShiftService : ICashShiftService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlySet<string> AllowedMovementTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "cash_in",
        "cash_out",
        "drawer_open_no_sale"
    };

    private readonly ITenantContext _tenantContext;
    private readonly ICashShiftRepository _repository;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly ILogger<CashShiftService> _logger;

    public CashShiftService(
        ITenantContext tenantContext,
        ICashShiftRepository repository,
        IAuditEventWriter auditEventWriter,
        ILogger<CashShiftService> logger)
    {
        _tenantContext = tenantContext;
        _repository = repository;
        _auditEventWriter = auditEventWriter;
        _logger = logger;
    }

    public async Task<CashShiftResponse?> GetCurrentOpenShiftAsync(CancellationToken cancellationToken)
    {
        if (!TryResolveTerminalRuntime(null, null, out Guid tenantId, out Guid storeId, out Guid terminalId))
        {
            _logger.LogWarning("Current cash shift read rejected because terminal runtime context is missing");
            return null;
        }

        CashShiftResponse? shift = await _repository.GetCurrentOpenShiftAsync(tenantId, storeId, terminalId, cancellationToken);
        _logger.LogInformation(
            "Current cash shift read for tenant {TenantId} store {StoreId} terminal {TerminalId} found {HasShift}",
            tenantId,
            storeId,
            terminalId,
            shift is not null);

        return shift;
    }


    public async Task<CashShiftOperationalSummaryResponse?> GetOperationalSummaryAsync(Guid shiftId, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || shiftId == Guid.Empty)
        {
            _logger.LogWarning("Cash shift operational summary rejected because tenant context or shift id is missing");
            return null;
        }

        CashShiftOperationalSummaryResponse? summary = await _repository.GetOperationalSummaryAsync(
            _tenantContext.TenantId.Value,
            shiftId,
            cancellationToken);

        if (summary is null)
        {
            _logger.LogWarning("Cash shift operational summary {ShiftId} not found for tenant {TenantId}", shiftId, _tenantContext.TenantId.Value);
        }

        return summary;
    }

    public async Task<CashShiftResponse?> OpenAsync(OpenCashShiftRequest request, CancellationToken cancellationToken)
    {
        if (request.OpeningAmountCents < 0)
        {
            _logger.LogWarning("Cash shift open rejected because opening amount is negative for tenant {TenantId}", _tenantContext.TenantId);
            return null;
        }

        if (!TryResolveTerminalRuntime(request.StoreId, request.TerminalId, out Guid tenantId, out Guid storeId, out Guid terminalId))
        {
            _logger.LogWarning("Cash shift open rejected because terminal runtime context is missing");
            return null;
        }

        CashShiftResponse? shift = await _repository.OpenAsync(
            tenantId,
            storeId,
            terminalId,
            request.OpenedByUserId,
            request.OpeningAmountCents,
            cancellationToken);

        if (shift is null)
        {
            _logger.LogWarning(
                "Cash shift open rejected for tenant {TenantId} store {StoreId} terminal {TerminalId} actor {ActorUserId}",
                tenantId,
                storeId,
                terminalId,
                request.OpenedByUserId);
            return null;
        }

        _logger.LogInformation(
            "Cash shift {ShiftId} opened for tenant {TenantId} store {StoreId} terminal {TerminalId} actor {ActorUserId}",
            shift.Id,
            tenantId,
            storeId,
            terminalId,
            request.OpenedByUserId);

        await WriteAuditAsync(shift.TenantId, "cash.shift.opened", "cash_shift", shift.Id, shift, cancellationToken);

        return shift;
    }

    public async Task<CashMovementResponse?> CreateMovementAsync(Guid shiftId, CreateCashMovementRequest request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue)
        {
            _logger.LogWarning("Cash movement rejected because tenant context is missing");
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.MovementType))
        {
            _logger.LogWarning("Cash movement rejected because movement type is missing for tenant {TenantId}", _tenantContext.TenantId);
            return null;
        }

        string movementType = request.MovementType.Trim();
        if (!AllowedMovementTypes.Contains(movementType))
        {
            _logger.LogWarning("Cash movement rejected because movement type {MovementType} is invalid for tenant {TenantId}", movementType, _tenantContext.TenantId);
            return null;
        }

        if (request.AmountCents < 0 || (movementType != "drawer_open_no_sale" && request.AmountCents == 0))
        {
            _logger.LogWarning("Cash movement rejected because amount {AmountCents} is invalid for tenant {TenantId}", request.AmountCents, _tenantContext.TenantId);
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            _logger.LogWarning("Cash movement rejected because reason is missing for tenant {TenantId}", _tenantContext.TenantId);
            return null;
        }

        CashMovementResponse? movement = await _repository.CreateMovementAsync(
            _tenantContext.TenantId.Value,
            shiftId,
            movementType,
            request.AmountCents,
            request.Reason.Trim(),
            request.CreatedByUserId,
            request.AuthorizedByUserId,
            cancellationToken);

        if (movement is null)
        {
            _logger.LogWarning(
                "Cash movement rejected for tenant {TenantId} shift {ShiftId} actor {ActorUserId}",
                _tenantContext.TenantId.Value,
                shiftId,
                request.CreatedByUserId);
            return null;
        }

        _logger.LogInformation(
            "Cash movement {MovementId} created for tenant {TenantId} shift {ShiftId} actor {ActorUserId}",
            movement.Id,
            movement.TenantId,
            shiftId,
            request.CreatedByUserId);

        await WriteAuditAsync(movement.TenantId, "cash.movement.created", "cash_movement", movement.Id, movement, cancellationToken);

        return movement;
    }

    public async Task<CashShiftResponse?> CloseAsync(Guid shiftId, CloseCashShiftRequest request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue)
        {
            _logger.LogWarning("Cash shift close rejected because tenant context is missing");
            return null;
        }

        if (request.CountedCashCents < 0)
        {
            _logger.LogWarning("Cash shift close rejected because counted cash is negative for tenant {TenantId}", _tenantContext.TenantId.Value);
            return null;
        }

        CashShiftResponse? shift = await _repository.CloseAsync(
            _tenantContext.TenantId.Value,
            shiftId,
            request.ClosedByUserId,
            request.CountedCashCents,
            cancellationToken);

        if (shift is null)
        {
            _logger.LogWarning(
                "Cash shift close rejected for tenant {TenantId} shift {ShiftId} actor {ActorUserId}",
                _tenantContext.TenantId.Value,
                shiftId,
                request.ClosedByUserId);
            return null;
        }

        _logger.LogInformation(
            "Cash shift {ShiftId} closed for tenant {TenantId} actor {ActorUserId} difference {DifferenceCents}",
            shift.Id,
            shift.TenantId,
            request.ClosedByUserId,
            shift.DifferenceCents);

        await WriteAuditAsync(shift.TenantId, "cash.shift.closed", "cash_shift", shift.Id, shift, cancellationToken);

        return shift;
    }

    private Task WriteAuditAsync<T>(Guid tenantId, string action, string entityType, Guid entityId, T afterData, CancellationToken cancellationToken)
    {
        return _auditEventWriter.AppendAsync(
            tenantId,
            action,
            entityType,
            entityId,
            null,
            JsonSerializer.SerializeToElement(afterData, JsonOptions),
            cancellationToken);
    }

    private bool TryResolveTerminalRuntime(Guid? requestStoreId, Guid? requestTerminalId, out Guid tenantId, out Guid storeId, out Guid terminalId)
    {
        tenantId = _tenantContext.TenantId ?? Guid.Empty;
        storeId = _tenantContext.StoreId ?? requestStoreId ?? Guid.Empty;
        terminalId = _tenantContext.TerminalId ?? requestTerminalId ?? Guid.Empty;

        return tenantId != Guid.Empty && storeId != Guid.Empty && terminalId != Guid.Empty;
    }
}
