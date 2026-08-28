using System.Diagnostics;
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

            _recorder.Record(context.Request.Method, NormalizeRoute(route, context), context.Response.StatusCode, stopwatch.Elapsed.TotalMilliseconds);
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
