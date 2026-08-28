namespace SolidPOS.PosServer.Contracts.Sales;

public sealed record CreateSaleRequest(
    Guid LocalSaleId,
    Guid CashierUserId,
    Guid? CustomerId,
    DateTimeOffset OccurredAt,
    DateTimeOffset LocalCreatedAt,
    IReadOnlyCollection<CreateSaleLineRequest> Lines,
    IReadOnlyCollection<CreateSalePaymentRequest> Payments,
    long TipCents);

