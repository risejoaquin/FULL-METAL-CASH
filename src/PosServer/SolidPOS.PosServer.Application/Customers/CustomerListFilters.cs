namespace SolidPOS.PosServer.Application.Customers;

public sealed record CustomerListFilters(
    string? Search,
    string? Status,
    int Limit);
