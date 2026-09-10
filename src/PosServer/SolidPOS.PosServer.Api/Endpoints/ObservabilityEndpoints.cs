using Microsoft.AspNetCore.Mvc;
using SolidPOS.PosServer.Application.Observability;
using SolidPOS.PosServer.Application.Security;
using SolidPOS.PosServer.Infrastructure.Observability;

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

        group.MapGet("/prometheus", async (
            IOperationalMetricsService metricsService,
            CancellationToken cancellationToken) =>
        {
            var metrics = await metricsService.GetMetricsAsync(cancellationToken);
            return Results.Text(
                PrometheusMetricsFormatter.Format(metrics),
                "text/plain; version=0.0.4; charset=utf-8");
        })
        .WithName("GetPrometheusOperationalMetrics")
        .Produces(StatusCodes.Status200OK, contentType: "text/plain")
        .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")
        .Produces<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")
        .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json");

        group.MapGet("/alerts", async (
            IOperationalMetricsService metricsService,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            var metrics = await metricsService.GetMetricsAsync(cancellationToken);
            return Results.Ok(ProductionAlertEvaluator.Evaluate(metrics, configuration));
        })
        .WithName("GetProductionAlerts")
        .Produces(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")
        .Produces<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")
        .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json");

        return api;
    }
}
