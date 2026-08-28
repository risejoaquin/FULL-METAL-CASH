using Microsoft.AspNetCore.Mvc;
using SolidPOS.PosServer.Application.Sales;
using SolidPOS.PosServer.Application.Security;
using SolidPOS.PosServer.Contracts.Sales;

namespace SolidPOS.PosServer.Api.Endpoints;

public static class SalesEndpoints
{
    public static RouteGroupBuilder MapSalesEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/sales")
            .WithTags("Sales");


        group.MapGet("", async Task<IResult> (
            [FromQuery] DateTimeOffset? from,
            [FromQuery] DateTimeOffset? to,
            [FromQuery] string? storeId,
            [FromQuery] string? terminalId,
            [FromQuery] string? status,
            [FromQuery] int limit,
            ISalesService salesService,
            CancellationToken cancellationToken) =>
        {
            if (!TryBuildSaleListFilters(from, to, storeId, terminalId, status, limit, out SaleListFilters? filters, out IResult? invalid))
            {
                return invalid;
            }

            IReadOnlyCollection<SaleListItemResponse>? sales = await salesService.ListAsync(filters, cancellationToken);
            return sales is null
                ? Results.Problem(
                    title: "Sales list unavailable",
                    detail: "The tenant context is missing or one of the requested filters is invalid for this tenant.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://solidpos.local/problems/sales-list-unavailable")
                : Results.Ok(sales);
        })
        .RequireAuthorization(PermissionCodes.SalesRead)
        .WithName("ListSales");

        group.MapGet("/{saleId:guid}", async Task<IResult> (
            [FromRoute] Guid saleId,
            ISalesService salesService,
            CancellationToken cancellationToken) =>
        {
            SaleDetailResponse? sale = await salesService.GetByIdAsync(saleId, cancellationToken);
            return sale is null
                ? Results.NotFound()
                : Results.Ok(sale);
        })
        .RequireAuthorization(PermissionCodes.SalesRead)
        .WithName("GetSaleById");

        group.MapPost("", async Task<IResult> (
            [FromBody] CreateSaleRequest request,
            ISalesService salesService,
            CancellationToken cancellationToken) =>
        {
            SaleResponse? sale = await salesService.CreateAsync(request, cancellationToken);

            return sale is null
                ? Results.Problem(
                    title: "Sale rejected",
                    detail: "The sale failed validation. Check tenant, terminal, open cash shift, catalog, prices and payments.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://solidpos.local/problems/sale-rejected")
                : Results.Created($"/api/v1/sales/{sale.Id}", sale);
        })
        .RequireAuthorization(PermissionCodes.SalesCreate)
        .WithName("CreateSale");

        group.MapPost("/{saleId:guid}/void", async Task<IResult> (
            [FromRoute] Guid saleId,
            [FromBody] VoidSaleRequest request,
            ISalesService salesService,
            CancellationToken cancellationToken) =>
        {
            SaleResponse? sale = await salesService.VoidAsync(saleId, request, cancellationToken);

            return sale is null
                ? Results.Problem(
                    title: "Sale void rejected",
                    detail: "The sale cannot be voided. Check tenant, terminal, open cash shift, sale status and actor permissions.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://solidpos.local/problems/sale-void-rejected")
                : Results.Ok(sale);
        })
        .RequireAuthorization(PermissionCodes.SalesVoid)
        .WithName("VoidSale");

        return api;
    }

    private static bool TryBuildSaleListFilters(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? storeIdValue,
        string? terminalIdValue,
        string? status,
        int limit,
        out SaleListFilters filters,
        out IResult invalid)
    {
        if (!TryParseOptionalGuid(storeIdValue, "storeId", out Guid? storeId, out invalid) ||
            !TryParseOptionalGuid(terminalIdValue, "terminalId", out Guid? terminalId, out invalid))
        {
            filters = null!;
            return false;
        }

        if (from.HasValue && to.HasValue && from > to)
        {
            filters = null!;
            invalid = Results.Problem(
                title: "Invalid sales filter",
                detail: "Query parameter 'from' must be less than or equal to 'to'.",
                statusCode: StatusCodes.Status400BadRequest,
                type: "https://solidpos.local/problems/invalid-sales-filter");
            return false;
        }

        if (limit is < 0 or > 200)
        {
            filters = null!;
            invalid = Results.Problem(
                title: "Invalid sales filter",
                detail: "Query parameter 'limit' must be between 1 and 200 when provided.",
                statusCode: StatusCodes.Status400BadRequest,
                type: "https://solidpos.local/problems/invalid-sales-filter");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            string normalizedStatus = status.Trim();
            if (normalizedStatus is not ("suspended" or "completed" or "voided" or "partially_returned" or "returned"))
            {
                filters = null!;
                invalid = Results.Problem(
                    title: "Invalid sales filter",
                    detail: "Query parameter 'status' must be one of suspended, completed, voided, partially_returned or returned.",
                    statusCode: StatusCodes.Status400BadRequest,
                    type: "https://solidpos.local/problems/invalid-sales-filter");
                return false;
            }
        }

        filters = new SaleListFilters(from, to, storeId, terminalId, status, limit <= 0 ? 50 : limit);
        return true;
    }

    private static bool TryParseOptionalGuid(
        string? value,
        string parameterName,
        out Guid? parsed,
        out IResult invalid)
    {
        parsed = null;
        invalid = null!;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (Guid.TryParse(value, out Guid guid))
        {
            parsed = guid;
            return true;
        }

        invalid = Results.Problem(
            title: "Invalid sales filter",
            detail: $"Query parameter '{parameterName}' must be a valid UUID.",
            statusCode: StatusCodes.Status400BadRequest,
            type: "https://solidpos.local/problems/invalid-sales-filter");
        return false;
    }

}
