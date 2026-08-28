using System.Text.Json;
using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.OfflineSales;

public sealed class OfflineSaleService
{
    private readonly ILocalPosRepository _repository;
    private readonly IClock _clock;

    public OfflineSaleService(ILocalPosRepository repository, IClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<LocalOutboxEvent> CreateOfflineSaleAsync(OfflineSaleDraft sale, CancellationToken cancellationToken = default)
    {
        LocalOutboxEvent outboxEvent = BuildSaleCompletedEvent(sale);
        await _repository.SaveOfflineSaleAsync(sale, outboxEvent, cancellationToken).ConfigureAwait(false);
        return outboxEvent;
    }

    public async Task<LocalOutboxEvent> CreateOfflineSaleWithInventoryAsync(
        OfflineSaleDraft sale,
        IReadOnlyCollection<LocalInventoryMovement> movements,
        CancellationToken cancellationToken = default)
    {
        LocalOutboxEvent outboxEvent = BuildSaleCompletedEvent(sale);
        await _repository.SaveOfflineSaleWithInventoryAsync(sale, outboxEvent, movements, cancellationToken).ConfigureAwait(false);
        return outboxEvent;
    }

    private LocalOutboxEvent BuildSaleCompletedEvent(OfflineSaleDraft sale)
    {
        OfflineSaleCalculator.Validate(sale);

        if (!sale.CashierUserId.HasValue || sale.CashierUserId.Value == Guid.Empty)
        {
            throw new InvalidOperationException("Offline sale requires a cashier user id before it can be synchronized as sale.completed.");
        }

        var payload = new
        {
            localSaleId = sale.LocalSaleId,
            cashierUserId = sale.CashierUserId.Value,
            customerId = sale.CustomerId,
            occurredAt = sale.OccurredAtUtc,
            localCreatedAt = sale.OccurredAtUtc,
            lines = sale.Lines.Select(line => new
            {
                productId = line.ProductId,
                variantId = line.VariantId,
                quantity = line.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                discountCents = line.DiscountCents,
                preparationNote = line.PreparationNote,
                modifierIds = line.ModifierIds,
                discountId = line.DiscountId
            }).ToArray(),
            payments = sale.Payments.Select(payment => new
            {
                localPaymentId = payment.LocalPaymentId ?? Guid.NewGuid(),
                methodCode = payment.MethodCode,
                amountCents = payment.AmountCents,
                reference = payment.Reference
            }).ToArray(),
            tipCents = sale.TipCents
        };

        var outboxEvent = new LocalOutboxEvent(
            Guid.NewGuid(),
            sale.TenantId,
            sale.StoreId,
            sale.TerminalId,
            "sale.completed",
            4,
            _clock.UtcNow.ToUnixTimeMilliseconds(),
            JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            LocalOutboxStatus.Pending,
            _clock.UtcNow);

        return outboxEvent;
    }
}
