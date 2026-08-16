using System.Text.Json;
using Microsoft.Extensions.Logging;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Customers;
using SolidPOS.PosServer.Contracts.Customers;

namespace SolidPOS.PosServer.Infrastructure.Customers;

public sealed class CustomersService : ICustomersService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITenantContext _tenantContext;
    private readonly ICustomersRepository _repository;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly ILogger<CustomersService> _logger;

    public CustomersService(
        ITenantContext tenantContext,
        ICustomersRepository repository,
        IAuditEventWriter auditEventWriter,
        ILogger<CustomersService> logger)
    {
        _tenantContext = tenantContext;
        _repository = repository;
        _auditEventWriter = auditEventWriter;
        _logger = logger;
    }

    public async Task<CustomerResponse?> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || !CreateRequestIsValid(request))
        {
            _logger.LogWarning("Customer create rejected by validation");
            return null;
        }

        CreateCustomerRequest normalized = request with
        {
            Name = request.Name.Trim(),
            Email = NormalizeNullable(request.Email),
            Phone = NormalizeNullable(request.Phone)
        };

        CustomerResponse? created = await _repository.CreateAsync(_tenantContext.TenantId.Value, normalized, cancellationToken);
        if (created is null)
        {
            _logger.LogWarning("Customer create rejected by repository for tenant {TenantId}", _tenantContext.TenantId.Value);
            return null;
        }

        await _auditEventWriter.AppendAsync(
            created.TenantId,
            "customer.created",
            "customer",
            created.Id,
            null,
            JsonSerializer.SerializeToElement(created, JsonOptions),
            cancellationToken);

        return created;
    }

    public async Task<CustomerResponse?> GetByIdAsync(Guid customerId, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || customerId == Guid.Empty)
        {
            return null;
        }

        return await _repository.GetByIdAsync(_tenantContext.TenantId.Value, customerId, cancellationToken);
    }

    public async Task<CustomerResponse?> UpdateAsync(Guid customerId, UpdateCustomerRequest request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || customerId == Guid.Empty || !UpdateRequestIsValid(request))
        {
            _logger.LogWarning("Customer update rejected by validation for customer {CustomerId}", customerId);
            return null;
        }

        CustomerResponse? before = await _repository.GetByIdAsync(_tenantContext.TenantId.Value, customerId, cancellationToken);
        if (before is null)
        {
            return null;
        }

        UpdateCustomerRequest normalized = request with
        {
            Name = NormalizeNullable(request.Name),
            Email = NormalizeNullable(request.Email),
            Phone = NormalizeNullable(request.Phone),
            Status = NormalizeNullable(request.Status)
        };

        CustomerResponse? updated = await _repository.UpdateAsync(_tenantContext.TenantId.Value, customerId, normalized, cancellationToken);
        if (updated is null)
        {
            _logger.LogWarning("Customer update rejected by repository for tenant {TenantId} customer {CustomerId}", _tenantContext.TenantId.Value, customerId);
            return null;
        }

        await _auditEventWriter.AppendAsync(
            updated.TenantId,
            "customer.updated",
            "customer",
            updated.Id,
            JsonSerializer.SerializeToElement(before, JsonOptions),
            JsonSerializer.SerializeToElement(updated, JsonOptions),
            cancellationToken);

        return updated;
    }

    public async Task<IReadOnlyCollection<CustomerListItemResponse>?> ListAsync(CustomerListFilters filters, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || !FiltersAreValid(filters))
        {
            return null;
        }

        CustomerListFilters normalized = filters with
        {
            Search = NormalizeNullable(filters.Search),
            Status = NormalizeNullable(filters.Status),
            Limit = filters.Limit <= 0 ? 50 : filters.Limit
        };

        return await _repository.ListAsync(_tenantContext.TenantId.Value, normalized, cancellationToken);
    }

    public async Task<CustomerSalesHistoryResponse?> GetSalesAsync(Guid customerId, CustomerSalesFilters filters, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || customerId == Guid.Empty || !SalesFiltersAreValid(filters))
        {
            return null;
        }

        CustomerSalesFilters normalized = filters with { Limit = filters.Limit <= 0 ? 50 : filters.Limit };
        return await _repository.GetSalesAsync(_tenantContext.TenantId.Value, customerId, normalized, cancellationToken);
    }

    private static bool CreateRequestIsValid(CreateCustomerRequest request)
    {
        return !string.IsNullOrWhiteSpace(request.Name)
            && request.Name.Trim().Length <= 200
            && NullableStringLengthIsValid(request.Email, 320)
            && NullableStringLengthIsValid(request.Phone, 50)
            && request.CreditLimitCents >= 0;
    }

    private static bool UpdateRequestIsValid(UpdateCustomerRequest request)
    {
        if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
        {
            return false;
        }

        if (!NullableStringLengthIsValid(request.Name, 200) || !NullableStringLengthIsValid(request.Email, 320) || !NullableStringLengthIsValid(request.Phone, 50))
        {
            return false;
        }

        if (request.CreditLimitCents is < 0)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(request.Status) || request.Status.Trim() is "active" or "inactive" or "archived";
    }

    private static bool FiltersAreValid(CustomerListFilters filters)
    {
        if (!string.IsNullOrWhiteSpace(filters.Status) && filters.Status.Trim() is not ("active" or "inactive" or "archived"))
        {
            return false;
        }

        return filters.Limit is >= 0 and <= 200;
    }

    private static bool SalesFiltersAreValid(CustomerSalesFilters filters)
    {
        if (filters.From.HasValue && filters.To.HasValue && filters.From > filters.To)
        {
            return false;
        }

        return filters.Limit is >= 0 and <= 200;
    }

    private static bool NullableStringLengthIsValid(string? value, int maxLength)
    {
        return value is null || value.Trim().Length <= maxLength;
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
