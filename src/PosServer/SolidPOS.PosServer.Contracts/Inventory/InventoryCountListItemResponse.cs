namespace SolidPOS.PosServer.Contracts.Inventory;

public sealed record InventoryCountListItemResponse(
    Guid Id,
    Guid StoreId,
    Guid LocalCountId,
    string Status,
    string Reason,
    int LineCount,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt);
