namespace SolidPOS.PosServer.Contracts.Customers;

public sealed record CustomerSalesHistoryResponse(
    CustomerResponse Customer,
    CustomerSalesSummaryResponse Summary,
    IReadOnlyCollection<CustomerSaleHistoryItemResponse> Sales);
