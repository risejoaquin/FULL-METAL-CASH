namespace SolidPOS.PosServer.Contracts.Returns;

public sealed record ReturnLineResponse(
    Guid Id,
    Guid SaleLineId,
    int SaleLineNumber,
    string Description,
    string Quantity,
    long TotalCents);
