namespace SolidPOS.PosServer.Contracts.Receipts;

public sealed record ReceiptModifierResponse(
    Guid Id,
    string Name,
    long PriceDeltaCents,
    string InventoryBehavior);
