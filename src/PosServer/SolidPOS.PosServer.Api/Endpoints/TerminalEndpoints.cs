using Microsoft.AspNetCore.Mvc;
using SolidPOS.PosServer.Application.Security;
using SolidPOS.PosServer.Application.Terminals;
using SolidPOS.PosServer.Contracts.Terminals;

namespace SolidPOS.PosServer.Api.Endpoints;

public static class TerminalEndpoints
{
    public static RouteGroupBuilder MapTerminalEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/terminals")
            .WithTags("Terminals");

        group.MapPost("/enrollment-token", async Task<IResult> (
            [FromBody] CreateTerminalEnrollmentTokenRequest request,
            ITerminalEnrollmentService terminalEnrollmentService,
            CancellationToken cancellationToken) =>
        {
            TerminalEnrollmentTokenResponse? response = await terminalEnrollmentService.CreateEnrollmentTokenAsync(request, cancellationToken);

            return response is null
                ? Results.Problem(
                    title: "Invalid terminal enrollment request",
                    detail: "The store is invalid for this tenant or the tenant context is missing.",
                    statusCode: StatusCodes.Status400BadRequest,
                    type: "https://solidpos.local/problems/invalid-terminal-enrollment-request")
                : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.TerminalsRegister)
        .WithName("CreateTerminalEnrollmentToken");

        group.MapGet("", async Task<IResult> (
            ITerminalEnrollmentService terminalEnrollmentService,
            CancellationToken cancellationToken) =>
        {
            IReadOnlyCollection<TerminalResponse> terminals = await terminalEnrollmentService.ListTerminalsAsync(cancellationToken);
            return Results.Ok(terminals);
        })
        .RequireAuthorization(PermissionCodes.TerminalsManage)
        .WithName("ListTerminals");

        group.MapPost("/{terminalId:guid}/revoke", async Task<IResult> (
            [FromRoute] Guid terminalId,
            ITerminalEnrollmentService terminalEnrollmentService,
            CancellationToken cancellationToken) =>
        {
            bool revoked = await terminalEnrollmentService.RevokeTerminalAsync(terminalId, cancellationToken);

            return revoked
                ? Results.NoContent()
                : Results.NotFound(new
                {
                    type = "https://solidpos.local/problems/terminal-not-found",
                    title = "Terminal not found",
                    status = StatusCodes.Status404NotFound
                });
        })
        .RequireAuthorization(PermissionCodes.TerminalsManage)
        .WithName("RevokeTerminal");

        group.MapGet("/{terminalId:guid}", async Task<IResult> (
            [FromRoute] Guid terminalId,
            ITerminalEnrollmentService terminalEnrollmentService,
            CancellationToken cancellationToken) =>
        {
            TerminalDetailResponse? terminal = await terminalEnrollmentService.GetTerminalAsync(terminalId, cancellationToken);

            return terminal is null
                ? Results.NotFound(new
                {
                    type = "https://solidpos.local/problems/terminal-not-found",
                    title = "Terminal not found",
                    status = StatusCodes.Status404NotFound
                })
                : Results.Ok(terminal);
        })
        .RequireAuthorization(PermissionCodes.TerminalsManage)
        .WithName("GetTerminal");

        group.MapPost("/{terminalId:guid}/store", async Task<IResult> (
            [FromRoute] Guid terminalId,
            [FromBody] AssignTerminalStoreRequest request,
            ITerminalEnrollmentService terminalEnrollmentService,
            CancellationToken cancellationToken) =>
        {
            TerminalDetailResponse? terminal = await terminalEnrollmentService.AssignStoreAsync(terminalId, request.StoreId, cancellationToken);

            return terminal is null
                ? Results.Problem(
                    title: "Invalid store assignment",
                    detail: "The store does not exist for this tenant or the terminal was not found.",
                    statusCode: StatusCodes.Status400BadRequest,
                    type: "https://solidpos.local/problems/invalid-store-assignment")
                : Results.Ok(terminal);
        })
        .RequireAuthorization(PermissionCodes.TerminalsManage)
        .WithName("AssignTerminalStore");

        group.MapPost("/{terminalId:guid}/disable", async Task<IResult> (
            [FromRoute] Guid terminalId,
            ITerminalEnrollmentService terminalEnrollmentService,
            CancellationToken cancellationToken) =>
        {
            bool disabled = await terminalEnrollmentService.DisableTerminalAsync(terminalId, cancellationToken);

            return disabled
                ? Results.NoContent()
                : Results.NotFound(new
                {
                    type = "https://solidpos.local/problems/terminal-not-found",
                    title = "Terminal not found or already revoked",
                    status = StatusCodes.Status404NotFound
                });
        })
        .RequireAuthorization(PermissionCodes.TerminalsManage)
        .WithName("DisableTerminal");

        group.MapPost("/{terminalId:guid}/enable", async Task<IResult> (
            [FromRoute] Guid terminalId,
            ITerminalEnrollmentService terminalEnrollmentService,
            CancellationToken cancellationToken) =>
        {
            bool enabled = await terminalEnrollmentService.EnableTerminalAsync(terminalId, cancellationToken);

            return enabled
                ? Results.NoContent()
                : Results.Problem(
                    title: "Unable to enable terminal",
                    detail: "The terminal was not found or has been permanently revoked.",
                    statusCode: StatusCodes.Status400BadRequest,
                    type: "https://solidpos.local/problems/cannot-enable-terminal");
        })
        .RequireAuthorization(PermissionCodes.TerminalsManage)
        .WithName("EnableTerminal");

        group.MapGet("/{terminalId:guid}/health", async Task<IResult> (
            [FromRoute] Guid terminalId,
            ITerminalEnrollmentService terminalEnrollmentService,
            CancellationToken cancellationToken) =>
        {
            TerminalDeviceHealthDto? health = await terminalEnrollmentService.GetDeviceHealthAsync(terminalId, cancellationToken);

            return Results.Ok(health ?? new TerminalDeviceHealthDto());
        })
        .RequireAuthorization(PermissionCodes.TerminalsManage)
        .WithName("GetTerminalDeviceHealth");

        group.MapGet("/{terminalId:guid}/remote-config", async Task<IResult> (
            [FromRoute] Guid terminalId,
            ITerminalEnrollmentService terminalEnrollmentService,
            CancellationToken cancellationToken) =>
        {
            TerminalRemoteConfigMetadata? config = await terminalEnrollmentService.GetRemoteConfigAsync(terminalId, cancellationToken);

            return Results.Ok(config ?? new TerminalRemoteConfigMetadata());
        })
        .RequireAuthorization(PermissionCodes.TerminalsManage)
        .WithName("GetTerminalRemoteConfig");

        group.MapPut("/{terminalId:guid}/remote-config", async Task<IResult> (
            [FromRoute] Guid terminalId,
            [FromBody] UpdateTerminalRemoteConfigRequest request,
            ITerminalEnrollmentService terminalEnrollmentService,
            CancellationToken cancellationToken) =>
        {
            TerminalRemoteConfigMetadata? updated = await terminalEnrollmentService.UpdateRemoteConfigAsync(terminalId, request.RemoteConfig, cancellationToken);

            return updated is null
                ? Results.NotFound(new
                {
                    type = "https://solidpos.local/problems/terminal-not-found",
                    title = "Terminal not found",
                    status = StatusCodes.Status404NotFound
                })
                : Results.Ok(updated);
        })
        .RequireAuthorization(PermissionCodes.TerminalsManage)
        .WithName("UpdateTerminalRemoteConfig");

        return api;
    }
}
