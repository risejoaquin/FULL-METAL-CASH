namespace SolidPOS.PosServer.Contracts.Inventory;

public sealed record InventoryCountResponse(
    Guid Id,
    Guid TenantId,
    Guid StoreId,
    Guid? TerminalId,
    Guid LocalCountId,
    string Status,
    string Reason,
    Guid CreatedByUserId,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt,
    IReadOnlyCollection<InventoryCountLineResponse> Lines);
