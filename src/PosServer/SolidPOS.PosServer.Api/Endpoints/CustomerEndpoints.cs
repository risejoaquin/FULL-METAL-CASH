using Microsoft.AspNetCore.Mvc;
using SolidPOS.PosServer.Application.Customers;
using SolidPOS.PosServer.Application.Security;
using SolidPOS.PosServer.Contracts.Customers;

namespace SolidPOS.PosServer.Api.Endpoints;

public static class CustomerEndpoints
{
    public static RouteGroupBuilder MapCustomerEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/customers")
            .WithTags("Customers");

        group.MapGet("", async Task<IResult> (
            [FromQuery] string? search,
            [FromQuery] string? status,
            [FromQuery] int? limit,
            ICustomersService customersService,
            CancellationToken cancellationToken) =>
        {
            if (!TryBuildListFilters(search, status, limit, out CustomerListFilters? filters, out IResult? invalid))
            {
                return invalid;
            }

            IReadOnlyCollection<CustomerListItemResponse>? customers = await customersService.ListAsync(filters, cancellationToken);
            return customers is null
                ? Results.Problem(
                    title: "Customers list unavailable",
                    detail: "The tenant context is missing or one of the requested filters is invalid.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://solidpos.local/problems/customers-list-unavailable")
                : Results.Ok(customers);
        })
        .RequireAuthorization(PermissionCodes.CustomersRead)
        .WithName("ListCustomers");

        group.MapPost("", async Task<IResult> (
            [FromBody] CreateCustomerRequest request,
            ICustomersService customersService,
            CancellationToken cancellationToken) =>
        {
            CustomerResponse? created = await customersService.CreateAsync(request, cancellationToken);
            return created is null
                ? Results.Problem(
                    title: "Customer rejected",
                    detail: "The customer failed validation. Check tenant context, name, email uniqueness, phone and credit limit.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://solidpos.local/problems/customer-rejected")
                : Results.Created($"/api/v1/customers/{created.Id}", created);
        })
        .RequireAuthorization(PermissionCodes.CustomersManage)
        .WithName("CreateCustomer");

        group.MapGet("/{customerId:guid}", async Task<IResult> (
            [FromRoute] Guid customerId,
            ICustomersService customersService,
            CancellationToken cancellationToken) =>
        {
            CustomerResponse? customer = await customersService.GetByIdAsync(customerId, cancellationToken);
            return customer is null ? Results.NotFound() : Results.Ok(customer);
        })
        .RequireAuthorization(PermissionCodes.CustomersRead)
        .WithName("GetCustomerById");

        group.MapPatch("/{customerId:guid}", async Task<IResult> (
            [FromRoute] Guid customerId,
            [FromBody] UpdateCustomerRequest request,
            ICustomersService customersService,
            CancellationToken cancellationToken) =>
        {
            CustomerResponse? updated = await customersService.UpdateAsync(customerId, request, cancellationToken);
            return updated is null
                ? Results.Problem(
                    title: "Customer update rejected",
                    detail: "The customer update failed validation. Check customer id, status, email uniqueness and fields.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://solidpos.local/problems/customer-update-rejected")
                : Results.Ok(updated);
        })
        .RequireAuthorization(PermissionCodes.CustomersManage)
        .WithName("UpdateCustomer");

        group.MapGet("/{customerId:guid}/sales", async Task<IResult> (
            [FromRoute] Guid customerId,
            [FromQuery] DateTimeOffset? from,
            [FromQuery] DateTimeOffset? to,
            [FromQuery] int? limit,
            ICustomersService customersService,
            CancellationToken cancellationToken) =>
        {
            if (!TryBuildSalesFilters(from, to, limit, out CustomerSalesFilters? filters, out IResult? invalid))
            {
                return invalid;
            }

            CustomerSalesHistoryResponse? history = await customersService.GetSalesAsync(customerId, filters, cancellationToken);
            return history is null ? Results.NotFound() : Results.Ok(history);
        })
        .RequireAuthorization(PermissionCodes.CustomersRead)
        .WithName("GetCustomerSales");

        return api;
    }

    private static bool TryBuildListFilters(string? search, string? status, int? limit, out CustomerListFilters filters, out IResult invalid)
    {
        filters = null!;
        invalid = null!;

        if (!string.IsNullOrWhiteSpace(status) && status.Trim() is not ("active" or "inactive" or "archived"))
        {
            invalid = Results.Problem(
                title: "Invalid customers filter",
                detail: "Query parameter 'status' must be one of active, inactive or archived.",
                statusCode: StatusCodes.Status400BadRequest,
                type: "https://solidpos.local/problems/invalid-customers-filter");
            return false;
        }

        int normalizedLimit = limit ?? 50;
        if (normalizedLimit is < 1 or > 200)
        {
            invalid = Results.Problem(
                title: "Invalid customers filter",
                detail: "Query parameter 'limit' must be between 1 and 200.",
                statusCode: StatusCodes.Status400BadRequest,
                type: "https://solidpos.local/problems/invalid-customers-filter");
            return false;
        }

        filters = new CustomerListFilters(search, status, normalizedLimit);
        return true;
    }

    private static bool TryBuildSalesFilters(DateTimeOffset? from, DateTimeOffset? to, int? limit, out CustomerSalesFilters filters, out IResult invalid)
    {
        filters = null!;
        invalid = null!;

        if (from.HasValue && to.HasValue && from > to)
        {
            invalid = Results.Problem(
                title: "Invalid customer sales filter",
                detail: "Query parameter 'from' must be less than or equal to 'to'.",
                statusCode: StatusCodes.Status400BadRequest,
                type: "https://solidpos.local/problems/invalid-customer-sales-filter");
            return false;
        }

        int normalizedLimit = limit ?? 50;
        if (normalizedLimit is < 1 or > 200)
        {
            invalid = Results.Problem(
                title: "Invalid customer sales filter",
                detail: "Query parameter 'limit' must be between 1 and 200.",
                statusCode: StatusCodes.Status400BadRequest,
                type: "https://solidpos.local/problems/invalid-customer-sales-filter");
            return false;
        }

        filters = new CustomerSalesFilters(from, to, normalizedLimit);
        return true;
    }
}
