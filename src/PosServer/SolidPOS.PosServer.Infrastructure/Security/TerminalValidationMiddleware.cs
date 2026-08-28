using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Terminals;

namespace SolidPOS.PosServer.Infrastructure.Security;

public sealed class TerminalValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TerminalValidationMiddleware> _logger;

    public TerminalValidationMiddleware(RequestDelegate next, ILogger<TerminalValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, ITerminalRepository terminalRepository)
    {
        if (context.User.Identity?.IsAuthenticated == true && tenantContext.TerminalId.HasValue)
        {
            if (!tenantContext.TenantId.HasValue)
            {
                await RejectAsync(context);
                return;
            }

            bool isActive = await terminalRepository.IsTerminalActiveAsync(
                tenantContext.TenantId.Value,
                tenantContext.TerminalId.Value,
                context.RequestAborted);

            if (!isActive)
            {
                _logger.LogWarning(
                    "Terminal request rejected because terminal is not active for tenant {TenantId} terminal {TerminalId}",
                    tenantContext.TenantId.Value,
                    tenantContext.TerminalId.Value);

                await RejectAsync(context);
                return;
            }
        }

        await _next(context);
    }

    private static async Task RejectAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://solidpos.local/problems/inactive-terminal",
            title = "Inactive terminal",
            status = StatusCodes.Status401Unauthorized,
            traceId = context.TraceIdentifier
        });
    }
}
