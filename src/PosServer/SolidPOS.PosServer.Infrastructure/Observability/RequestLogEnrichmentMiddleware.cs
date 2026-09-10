using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;

namespace SolidPOS.PosServer.Infrastructure.Observability;

public sealed class RequestLogEnrichmentMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLogEnrichmentMiddleware> _logger;

    public RequestLogEnrichmentMiddleware(RequestDelegate next, ILogger<RequestLogEnrichmentMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        bool tenantScoped = tenantContext.TenantId.HasValue;
        bool terminalScoped = tenantContext.TerminalId.HasValue;
        string route = context.GetEndpoint() is Microsoft.AspNetCore.Routing.RouteEndpoint routeEndpoint
            ? routeEndpoint.RoutePattern.RawText ?? context.Request.Path.Value ?? "unknown"
            : context.Request.Path.Value ?? "unknown";

        Activity.Current?.SetTag("solidpos.tenant_scoped", tenantScoped);
        Activity.Current?.SetTag("solidpos.terminal_scoped", terminalScoped);
        Activity.Current?.SetTag("solidpos.route", route);

        using (LogContext.PushProperty("tenant_scoped", tenantScoped))
        using (LogContext.PushProperty("terminal_scoped", terminalScoped))
        using (LogContext.PushProperty("endpoint", $"{context.Request.Method} {route}"))
        {
            _logger.LogDebug("Request diagnostic context enriched");
            await _next(context);
        }
    }
}
