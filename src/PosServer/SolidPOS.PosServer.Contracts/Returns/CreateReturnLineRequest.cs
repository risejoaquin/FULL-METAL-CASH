namespace SolidPOS.PosServer.Contracts.Returns;

public sealed record CreateReturnLineRequest(
    Guid SaleLineId,
    string Quantity);
