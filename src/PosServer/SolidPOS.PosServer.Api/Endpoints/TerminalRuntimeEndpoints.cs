using Microsoft.AspNetCore.Mvc;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Security;
using SolidPOS.PosServer.Application.Terminals;
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

        group.MapPost("/heartbeat", async Task<IResult> (
            [FromBody] TerminalHeartbeatRequest request,
            ITerminalEnrollmentService terminalEnrollmentService,
            CancellationToken cancellationToken) =>
        {
            TerminalHeartbeatResponse? response = await terminalEnrollmentService.RecordHeartbeatAsync(request, cancellationToken);

            return response is null
                ? Results.Problem(
                    title: "Heartbeat rejected",
                    detail: "Missing terminal context or inactive terminal.",
                    statusCode: StatusCodes.Status401Unauthorized,
                    type: "https://solidpos.local/problems/heartbeat-rejected")
                : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.SyncPull)
        .WithName("RecordTerminalHeartbeat");

        group.MapGet("/remote-config", async Task<IResult> (
            ITenantContext tenantContext,
            ITerminalEnrollmentService terminalEnrollmentService,
            CancellationToken cancellationToken) =>
        {
            if (!tenantContext.TerminalId.HasValue)
            {
                return Results.Problem(
                    title: "Invalid terminal context",
                    detail: "The request does not contain a terminal identifier.",
                    statusCode: StatusCodes.Status401Unauthorized,
                    type: "https://solidpos.local/problems/invalid-terminal-context");
            }

            TerminalRemoteConfigMetadata? config = await terminalEnrollmentService.GetRemoteConfigAsync(tenantContext.TerminalId.Value, cancellationToken);

            return Results.Ok(config ?? new TerminalRemoteConfigMetadata());
        })
        .RequireAuthorization(PermissionCodes.SyncPull)
        .WithName("GetTerminalRuntimeRemoteConfig");

        return api;
    }
}
