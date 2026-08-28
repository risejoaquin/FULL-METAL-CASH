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

        return api;
    }
}
