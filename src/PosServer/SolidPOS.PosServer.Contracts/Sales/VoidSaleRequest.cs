namespace SolidPOS.PosServer.Contracts.Sales;

public sealed record VoidSaleRequest(
    Guid VoidedByUserId,
    string Reason,
    DateTimeOffset OccurredAt);
