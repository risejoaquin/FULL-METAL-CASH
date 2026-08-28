namespace SolidPOS.PosServer.Contracts.Returns;

public sealed record ReturnInventoryMovementResponse(
    Guid Id,
    Guid StoreId,
    Guid? TerminalId,
    Guid ProductId,
    Guid? VariantId,
    string? Sku,
    string? Name,
    string MovementType,
    string QuantityDelta,
    Guid UnitId,
    string? UnitCode,
    string? Effect,
    Guid? OriginalSaleId,
    Guid? OriginalSaleLineId,
    DateTimeOffset LocalOccurredAt,
    DateTimeOffset CreatedAt);
