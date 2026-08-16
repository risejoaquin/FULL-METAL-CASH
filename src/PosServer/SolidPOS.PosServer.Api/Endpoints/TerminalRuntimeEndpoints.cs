using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Security;
using SolidPOS.PosServer.Contracts.Terminals;

namespace SolidPOS.PosServer.Api.Endpoints;

public static class TerminalRuntimeEndpoints
{
    public static RouteGroupBuilder MapTerminalRuntimeEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/terminal")
            .WithTags("Terminals");

        group.MapGet("/session", IResult (ITenantContext tenantContext) =>
        {
            if (!tenantContext.TenantId.HasValue || !tenantContext.StoreId.HasValue || !tenantContext.TerminalId.HasValue)
            {
                return Results.Problem(
                    title: "Invalid terminal context",
                    detail: "The request does not contain a complete terminal context.",
                    statusCode: StatusCodes.Status401Unauthorized,
                    type: "https://solidpos.local/problems/invalid-terminal-context");
            }

            return Results.Ok(new TerminalRuntimeContextResponse(
                tenantContext.TenantId.Value,
                tenantContext.StoreId.Value,
                tenantContext.TerminalId.Value));
        })
        .RequireAuthorization(PermissionCodes.SyncPull)
        .WithName("GetTerminalRuntimeSession");

        return api;
    }
}
