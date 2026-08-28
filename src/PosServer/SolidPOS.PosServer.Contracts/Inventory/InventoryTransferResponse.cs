namespace SolidPOS.PosServer.Contracts.Inventory;

public sealed record InventoryTransferResponse(
    Guid Id,
    Guid TenantId,
    Guid FromStoreId,
    Guid ToStoreId,
    Guid LocalTransferId,
    string Status,
    string Reason,
    Guid CreatedByUserId,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt,
    IReadOnlyCollection<InventoryTransferLineResponse> Lines);
