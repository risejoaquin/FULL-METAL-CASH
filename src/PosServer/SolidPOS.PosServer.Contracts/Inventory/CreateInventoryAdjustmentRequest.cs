namespace SolidPOS.PosServer.Contracts.Inventory;

public sealed record CreateInventoryAdjustmentRequest(
    Guid LocalAdjustmentId,
    Guid? StoreId,
    string AdjustmentType,
    string Reason,
    Guid CreatedByUserId,
    DateTimeOffset OccurredAt,
    IReadOnlyCollection<CreateInventoryAdjustmentLineRequest> Lines);
