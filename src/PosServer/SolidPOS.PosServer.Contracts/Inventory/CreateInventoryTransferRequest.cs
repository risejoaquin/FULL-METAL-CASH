namespace SolidPOS.PosServer.Contracts.Inventory;

public sealed record CreateInventoryTransferRequest(
    Guid LocalTransferId,
    Guid FromStoreId,
    Guid ToStoreId,
    Guid CreatedByUserId,
    string Reason,
    DateTimeOffset OccurredAt,
    IReadOnlyCollection<CreateInventoryTransferLineRequest> Lines);
