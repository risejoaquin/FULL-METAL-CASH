namespace SolidPOS.PosServer.Application.Customers;

public sealed record CustomerSalesFilters(
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Limit);
