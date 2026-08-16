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
        using (LogContext.PushProperty("tenant_id", tenantContext.TenantId))
        using (LogContext.PushProperty("user_id", tenantContext.UserId))
        using (LogContext.PushProperty("terminal_id", tenantContext.TerminalId))
        using (LogContext.PushProperty("store_id", tenantContext.StoreId))
        using (LogContext.PushProperty("endpoint", $"{context.Request.Method} {context.Request.Path}"))
        {
            _logger.LogDebug("Request log context enriched");
            await _next(context);
        }
    }
}
