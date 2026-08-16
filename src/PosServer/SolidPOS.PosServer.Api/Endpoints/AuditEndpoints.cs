using Microsoft.AspNetCore.Mvc;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Security;
using SolidPOS.PosServer.Contracts.Audit;

namespace SolidPOS.PosServer.Api.Endpoints;

public static class AuditEndpoints
{
    public static RouteGroupBuilder MapAuditEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/audit")
            .WithTags("Audit");

        group.MapGet("/events", async Task<IResult> (
            [AsParameters] AuditEventsQuery query,
            IAuditEventService auditEventService,
            CancellationToken cancellationToken) =>
        {
            AuditEventPageResponse? response = await auditEventService.ListAsync(
                new AuditEventFilters(
                    query.Action,
                    query.EntityType,
                    query.EntityId,
                    query.ActorUserId,
                    query.TerminalId,
                    query.From,
                    query.To,
                    query.Page.GetValueOrDefault(1),
                    ResolvePageSize(query.PageSize, query.Limit)),
                cancellationToken);

            return response is null
                ? Results.Problem(
                    title: "Audit events unavailable",
                    detail: "The tenant context is missing or the filter range is invalid.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://solidpos.local/problems/audit-events-unavailable")
                : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.AuditRead)
        .WithName("ListAuditEvents");

        return api;
    }

    private static int ResolvePageSize(int? pageSize, int? limit)
    {
        if (pageSize.HasValue && pageSize.Value > 0)
        {
            return pageSize.Value;
        }

        if (limit.HasValue && limit.Value > 0)
        {
            return limit.Value;
        }

        return 50;
    }

    public sealed record AuditEventsQuery(
        [property: FromQuery] string? Action,
        [property: FromQuery] string? EntityType,
        [property: FromQuery] Guid? EntityId,
        [property: FromQuery] Guid? ActorUserId,
        [property: FromQuery] Guid? TerminalId,
        [property: FromQuery] DateTimeOffset? From,
        [property: FromQuery] DateTimeOffset? To,
        [property: FromQuery] int? Page,
        [property: FromQuery] int? PageSize,
        [property: FromQuery] int? Limit);
}
