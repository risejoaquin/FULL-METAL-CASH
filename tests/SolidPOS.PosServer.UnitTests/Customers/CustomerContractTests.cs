using SolidPOS.PosServer.Contracts.Customers;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Customers;

public sealed class CustomerContractTests
{
    [Fact]
    public void Customer_list_item_exposes_crm_summary_fields()
    {
        Guid customerId = Guid.NewGuid();
        var item = new CustomerListItemResponse(
            customerId,
            Guid.NewGuid(),
            "Cliente Demo",
            "cliente.demo@solidpos.local",
            "6620000000",
            0,
            "active",
            2,
            14600,
            7300,
            7300,
            3650,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        Assert.Equal(customerId, item.Id);
        Assert.Equal(2, item.SalesCount);
        Assert.Equal(7300, item.NetSpentCents);
        Assert.Equal(3650, item.AverageTicketCents);
    }

    [Fact]
    public void Customer_sales_history_exposes_customer_summary_and_sales()
    {
        var customer = new CustomerResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Cliente Demo",
            "cliente.demo@solidpos.local",
            null,
            0,
            "active",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var summary = new CustomerSalesSummaryResponse(1, 7300, 0, 7300, 7300, DateTimeOffset.UtcNow);
        var sale = new CustomerSaleHistoryItemResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "completed",
            7300,
            10000,
            2700,
            0,
            7300,
            "MXN",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var history = new CustomerSalesHistoryResponse(customer, summary, [sale]);

        Assert.Equal(customer.Id, history.Customer.Id);
        Assert.Equal(1, history.Summary.SalesCount);
        Assert.Single(history.Sales);
        Assert.Equal(7300, history.Sales.Single().NetAfterReturnsCents);
    }
}
