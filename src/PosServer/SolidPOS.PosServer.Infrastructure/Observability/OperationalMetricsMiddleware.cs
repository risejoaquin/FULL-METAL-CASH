using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Http;

namespace SolidPOS.PosServer.Infrastructure.Observability;

public sealed class OperationalMetricsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly OperationalMetricsRecorder _recorder;

    public OperationalMetricsMiddleware(RequestDelegate next, OperationalMetricsRecorder recorder)
    {
        _next = next;
        _recorder = recorder;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            string route = context.GetEndpoint() is Microsoft.AspNetCore.Routing.RouteEndpoint routeEndpoint
                ? routeEndpoint.RoutePattern.RawText ?? context.Request.Path.Value ?? "unknown"
                : context.Request.Path.Value ?? "unknown";

            string normalizedRoute = NormalizeRoute(route, context);
            double elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
            _recorder.Record(context.Request.Method, normalizedRoute, context.Response.StatusCode, elapsedMs);

            var tags = new TagList
            {
                { "http.request.method", context.Request.Method },
                { "http.route", normalizedRoute },
                { "http.response.status_code", context.Response.StatusCode }
            };
            SolidPosTelemetry.RequestCount.Add(1, tags);
            SolidPosTelemetry.RequestDuration.Record(elapsedMs, tags);
            if (context.Response.StatusCode >= 400)
            {
                SolidPosTelemetry.RequestErrorCount.Add(1, tags);
            }
        }
    }

    private static string NormalizeRoute(string route, HttpContext context)
    {
        string path = context.Request.Path.Value ?? route;
        if (route.Contains("/", StringComparison.Ordinal))
        {
            return route;
        }

        return path;
    }
}
