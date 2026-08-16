namespace SolidPOS.PosServer.Contracts.Inventory;

public sealed record InventoryTransferListItemResponse(
    Guid Id,
    Guid FromStoreId,
    Guid ToStoreId,
    Guid LocalTransferId,
    string Status,
    string Reason,
    int LineCount,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt);
