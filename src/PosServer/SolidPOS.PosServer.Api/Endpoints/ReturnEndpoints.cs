using Microsoft.AspNetCore.Mvc;
using SolidPOS.PosServer.Application.Returns;
using SolidPOS.PosServer.Application.Security;
using SolidPOS.PosServer.Contracts.Returns;

namespace SolidPOS.PosServer.Api.Endpoints;

public static class ReturnEndpoints
{
    public static RouteGroupBuilder MapReturnEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/returns")
            .WithTags("Returns");

        group.MapPost("", async Task<IResult> (
            [FromBody] CreateReturnRequest request,
            IReturnsService returnsService,
            CancellationToken cancellationToken) =>
        {
            ReturnResponse? created = await returnsService.CreateAsync(request, cancellationToken);
            return created is null
                ? Results.Problem(
                    title: "Return rejected",
                    detail: "The return failed validation. Check sale status, terminal/store, open cash shift, sold quantities, refund amount and payment method.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://solidpos.local/problems/return-rejected")
                : Results.Created($"/api/v1/returns/{created.Id}", created);
        })
        .RequireAuthorization(PermissionCodes.ReturnsCreate)
        .WithName("CreateReturn");

        group.MapGet("/{returnId:guid}", async Task<IResult> (
            [FromRoute] Guid returnId,
            IReturnsService returnsService,
            CancellationToken cancellationToken) =>
        {
            ReturnResponse? item = await returnsService.GetByIdAsync(returnId, cancellationToken);
            return item is null ? Results.NotFound() : Results.Ok(item);
        })
        .RequireAuthorization(PermissionCodes.ReturnsRead)
        .WithName("GetReturnById");

        group.MapGet("", async Task<IResult> (
            [FromQuery] string? saleId,
            [FromQuery] DateTimeOffset? from,
            [FromQuery] DateTimeOffset? to,
            [FromQuery] int? limit,
            IReturnsService returnsService,
            CancellationToken cancellationToken) =>
        {
            if (!TryBuildFilters(saleId, from, to, limit, out ReturnListFilters? filters, out IResult? invalid))
            {
                return invalid;
            }

            IReadOnlyCollection<ReturnListItemResponse>? items = await returnsService.ListAsync(filters, cancellationToken);
            return items is null
                ? Results.Problem(
                    title: "Returns list unavailable",
                    detail: "The tenant context is missing or one of the requested filters is invalid.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://solidpos.local/problems/returns-list-unavailable")
                : Results.Ok(items);
        })
        .RequireAuthorization(PermissionCodes.ReturnsRead)
        .WithName("ListReturns");

        return api;
    }

    private static bool TryBuildFilters(string? saleIdValue, DateTimeOffset? from, DateTimeOffset? to, int? limit, out ReturnListFilters filters, out IResult invalid)
    {
        filters = null!;
        invalid = null!;

        Guid? saleId = null;
        if (!string.IsNullOrWhiteSpace(saleIdValue))
        {
            if (!Guid.TryParse(saleIdValue, out Guid parsed))
            {
                invalid = Results.Problem(
                    title: "Invalid returns filter",
                    detail: "Query parameter 'saleId' must be a valid UUID.",
                    statusCode: StatusCodes.Status400BadRequest,
                    type: "https://solidpos.local/problems/invalid-returns-filter");
                return false;
            }
            saleId = parsed;
        }

        if (from.HasValue && to.HasValue && from > to)
        {
            invalid = Results.Problem(
                title: "Invalid returns filter",
                detail: "Query parameter 'from' must be less than or equal to 'to'.",
                statusCode: StatusCodes.Status400BadRequest,
                type: "https://solidpos.local/problems/invalid-returns-filter");
            return false;
        }

        int normalizedLimit = limit ?? 50;
        if (normalizedLimit is < 1 or > 200)
        {
            invalid = Results.Problem(
                title: "Invalid returns filter",
                detail: "Query parameter 'limit' must be between 1 and 200.",
                statusCode: StatusCodes.Status400BadRequest,
                type: "https://solidpos.local/problems/invalid-returns-filter");
            return false;
        }

        filters = new ReturnListFilters(saleId, from, to, normalizedLimit);
        return true;
    }
}
