using SolidPOS.PosServer.Contracts.Customers;

namespace SolidPOS.PosServer.Application.Customers;

public interface ICustomersRepository
{
    Task<CustomerResponse?> CreateAsync(Guid tenantId, CreateCustomerRequest request, CancellationToken cancellationToken);

    Task<CustomerResponse?> GetByIdAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken);

    Task<CustomerResponse?> UpdateAsync(Guid tenantId, Guid customerId, UpdateCustomerRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<CustomerListItemResponse>> ListAsync(Guid tenantId, CustomerListFilters filters, CancellationToken cancellationToken);

    Task<CustomerSalesHistoryResponse?> GetSalesAsync(Guid tenantId, Guid customerId, CustomerSalesFilters filters, CancellationToken cancellationToken);
}
