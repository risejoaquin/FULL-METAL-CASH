namespace SolidPOS.PosServer.Contracts.Inventory;

public sealed record InventoryAdjustmentResponse(
    Guid Id,
    Guid TenantId,
    Guid StoreId,
    Guid? TerminalId,
    Guid LocalAdjustmentId,
    string AdjustmentType,
    string Reason,
    Guid CreatedByUserId,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt,
    IReadOnlyCollection<InventoryAdjustmentLineResponse> Lines);
