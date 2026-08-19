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
        OfflineSaleCalculator.Validate(sale);

        var payload = new
        {
            saleId = sale.LocalSaleId,
            sale.TenantId,
            sale.StoreId,
            sale.TerminalId,
            sale.OccurredAtUtc,
            sale.Currency,
            subtotalCents = sale.SubtotalCents,
            discountCents = sale.DiscountCents,
            totalCents = sale.TotalCents,
            payments = sale.Payments,
            lines = sale.Lines
        };

        var outboxEvent = new LocalOutboxEvent(
            Guid.NewGuid(),
            sale.TenantId,
            sale.StoreId,
            sale.TerminalId,
            "sale.completed",
            4,
            _clock.UtcNow.ToUnixTimeMilliseconds(),
            JsonSerializer.Serialize(payload),
            LocalOutboxStatus.Pending,
            _clock.UtcNow);

        await _repository.SaveOfflineSaleAsync(sale, outboxEvent, cancellationToken).ConfigureAwait(false);
        return outboxEvent;
    }
}
