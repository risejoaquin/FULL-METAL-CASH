namespace SolidPOS.PosServer.Contracts.Inventory;

public sealed record CreateInventoryCountRequest(
    Guid LocalCountId,
    Guid? StoreId,
    Guid CreatedByUserId,
    string Reason,
    DateTimeOffset OccurredAt,
    IReadOnlyCollection<CreateInventoryCountLineRequest> Lines);
