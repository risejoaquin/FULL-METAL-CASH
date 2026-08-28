namespace SolidPOS.PosServer.Application.Returns;

public sealed record ReturnListFilters(
    Guid? SaleId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Limit);
