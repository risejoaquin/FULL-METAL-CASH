using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace SolidPOS.PosServer.Infrastructure.Security;

public sealed class RequiredClaimsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequiredClaimsMiddleware> _logger;

    public RequiredClaimsMiddleware(RequestDelegate next, ILogger<RequiredClaimsMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            bool hasTenant = context.User.HasClaim(claim => claim.Type == "tenant_id" && !string.IsNullOrWhiteSpace(claim.Value));
            bool hasActor = context.User.HasClaim(claim =>
                (claim.Type == "user_id" || claim.Type == "terminal_id")
                && !string.IsNullOrWhiteSpace(claim.Value));
            bool hasTerminal = context.User.HasClaim(claim => claim.Type == "terminal_id" && !string.IsNullOrWhiteSpace(claim.Value));
            bool hasStoreForTerminal = !hasTerminal || context.User.HasClaim(claim => claim.Type == "store_id" && !string.IsNullOrWhiteSpace(claim.Value));

            if (!hasTenant || !hasActor || !hasStoreForTerminal)
            {
                _logger.LogWarning("Authenticated request rejected because required claims are missing");
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    type = "https://solidpos.local/problems/invalid-token-claims",
                    title = "Invalid token claims",
                    status = StatusCodes.Status401Unauthorized,
                    traceId = context.TraceIdentifier
                });
                return;
            }
        }

        await _next(context);
    }
}
