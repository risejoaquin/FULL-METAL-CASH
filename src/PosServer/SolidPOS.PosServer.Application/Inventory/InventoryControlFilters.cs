namespace SolidPOS.PosServer.Application.Inventory;

public sealed record InventoryControlFilters(
    Guid? StoreId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Limit);
