namespace SolidPOS.PosServer.Application.Sales;

public sealed record SaleListFilters(
    DateTimeOffset? From,
    DateTimeOffset? To,
    Guid? StoreId,
    Guid? TerminalId,
    string? Status,
    int Limit);
