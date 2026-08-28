using Microsoft.AspNetCore.Mvc;
using SolidPOS.PosServer.Application.Observability;
using SolidPOS.PosServer.Application.Security;

namespace SolidPOS.PosServer.Api.Endpoints;

public static class ObservabilityEndpoints
{
    public static RouteGroupBuilder MapObservabilityEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/observability")
            .WithTags("Observability")
            .RequireAuthorization(PermissionCodes.ReportsRead);

        group.MapGet("/metrics", async (
            IOperationalMetricsService metricsService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await metricsService.GetMetricsAsync(cancellationToken));
        })
        .WithName("GetOperationalMetrics")
        .Produces(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")
        .Produces<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")
        .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json");

        return api;
    }
}
