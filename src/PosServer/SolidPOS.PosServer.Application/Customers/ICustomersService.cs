using SolidPOS.PosServer.Contracts.Customers;

namespace SolidPOS.PosServer.Application.Customers;

public interface ICustomersService
{
    Task<CustomerResponse?> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken);

    Task<CustomerResponse?> GetByIdAsync(Guid customerId, CancellationToken cancellationToken);

    Task<CustomerResponse?> UpdateAsync(Guid customerId, UpdateCustomerRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<CustomerListItemResponse>?> ListAsync(CustomerListFilters filters, CancellationToken cancellationToken);

    Task<CustomerSalesHistoryResponse?> GetSalesAsync(Guid customerId, CustomerSalesFilters filters, CancellationToken cancellationToken);
}
