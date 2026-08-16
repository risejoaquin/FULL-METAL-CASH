using Microsoft.AspNetCore.Mvc;
using SolidPOS.PosServer.Application.Discounts;
using SolidPOS.PosServer.Application.Security;
using SolidPOS.PosServer.Contracts.Discounts;

namespace SolidPOS.PosServer.Api.Endpoints;

public static class DiscountEndpoints
{
    public static RouteGroupBuilder MapDiscountEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/discounts")
            .WithTags("Discounts");

        group.MapGet("", async Task<IResult> (
            [FromQuery] string? search,
            [FromQuery] string? status,
            [FromQuery] Guid? storeId,
            [FromQuery] Guid? categoryId,
            [FromQuery] Guid? productId,
            [FromQuery] int? limit,
            IDiscountsService discountsService,
            CancellationToken cancellationToken) =>
        {
            if (!TryBuildListFilters(search, status, storeId, categoryId, productId, limit, out DiscountListFilters? filters, out IResult? invalid))
            {
                return invalid;
            }

            IReadOnlyCollection<DiscountListItemResponse>? discounts = await discountsService.ListAsync(filters, cancellationToken);
            return discounts is null
                ? Results.Problem(
                    title: "Discounts list unavailable",
                    detail: "The tenant context is missing or one of the requested filters is invalid.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://solidpos.local/problems/discounts-list-unavailable")
                : Results.Ok(discounts);
        })
        .RequireAuthorization(PermissionCodes.DiscountsRead)
        .WithName("ListDiscounts");

        group.MapPost("", async Task<IResult> (
            [FromBody] CreateDiscountRequest request,
            IDiscountsService discountsService,
            CancellationToken cancellationToken) =>
        {
            DiscountResponse? created = await discountsService.CreateAsync(request, cancellationToken);
            return created is null
                ? Results.Problem(
                    title: "Discount rejected",
                    detail: "The discount failed validation. Check type, value, scope, dates and code uniqueness.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://solidpos.local/problems/discount-rejected")
                : Results.Created($"/api/v1/discounts/{created.Id}", created);
        })
        .RequireAuthorization(PermissionCodes.DiscountsManage)
        .WithName("CreateDiscount");

        group.MapPost("/validate", async Task<IResult> (
            [FromBody] ValidateDiscountRequest request,
            IDiscountsService discountsService,
            CancellationToken cancellationToken) =>
        {
            ValidateDiscountResponse? result = await discountsService.ValidateAsync(request, cancellationToken);
            return result is null
                ? Results.Problem(
                    title: "Discount validation rejected",
                    detail: "The validation request failed basic validation. Check discountId, productId, quantity and unitPriceCents.",
                    statusCode: StatusCodes.Status400BadRequest,
                    type: "https://solidpos.local/problems/discount-validation-rejected")
                : Results.Ok(result);
        })
        .RequireAuthorization(PermissionCodes.DiscountsValidate)
        .WithName("ValidateDiscount");

        group.MapPatch("/{discountId:guid}", async Task<IResult> (
            [FromRoute] Guid discountId,
            [FromBody] UpdateDiscountRequest request,
            IDiscountsService discountsService,
            CancellationToken cancellationToken) =>
        {
            DiscountResponse? updated = await discountsService.UpdateAsync(discountId, request, cancellationToken);
            return updated is null
                ? Results.Problem(
                    title: "Discount update rejected",
                    detail: "The discount update failed validation. Check id, type, value, dates, scope and code uniqueness.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://solidpos.local/problems/discount-update-rejected")
                : Results.Ok(updated);
        })
        .RequireAuthorization(PermissionCodes.DiscountsManage)
        .WithName("UpdateDiscount");

        return api;
    }

    private static bool TryBuildListFilters(
        string? search,
        string? status,
        Guid? storeId,
        Guid? categoryId,
        Guid? productId,
        int? limit,
        out DiscountListFilters filters,
        out IResult invalid)
    {
        filters = null!;
        invalid = null!;

        if (!string.IsNullOrWhiteSpace(status) && status.Trim() is not ("active" or "inactive" or "archived"))
        {
            invalid = Results.Problem(
                title: "Invalid discounts filter",
                detail: "Query parameter 'status' must be one of active, inactive or archived.",
                statusCode: StatusCodes.Status400BadRequest,
                type: "https://solidpos.local/problems/invalid-discounts-filter");
            return false;
        }

        int normalizedLimit = limit ?? 50;
        if (normalizedLimit is < 1 or > 200)
        {
            invalid = Results.Problem(
                title: "Invalid discounts filter",
                detail: "Query parameter 'limit' must be between 1 and 200.",
                statusCode: StatusCodes.Status400BadRequest,
                type: "https://solidpos.local/problems/invalid-discounts-filter");
            return false;
        }

        filters = new DiscountListFilters(search, status, storeId, categoryId, productId, normalizedLimit);
        return true;
    }
}
