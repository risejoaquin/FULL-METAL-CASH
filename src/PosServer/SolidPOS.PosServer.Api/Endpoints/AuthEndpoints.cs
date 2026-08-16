using Microsoft.AspNetCore.Mvc;
using SolidPOS.PosServer.Application.Auth;
using SolidPOS.PosServer.Application.Terminals;
using SolidPOS.PosServer.Contracts.Auth;
using SolidPOS.PosServer.Contracts.Terminals;

namespace SolidPOS.PosServer.Api.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/auth")
            .WithTags("Auth");

        group.MapPost("/login", async Task<IResult> (
            [FromBody] LoginRequest request,
            IAuthService authService,
            CancellationToken cancellationToken) =>
        {
            AuthSessionResponse? session = await authService.LoginAsync(request, cancellationToken);

            return session is null
                ? Results.Problem(
                    title: "Invalid credentials",
                    detail: "Invalid credentials",
                    statusCode: StatusCodes.Status401Unauthorized,
                    type: "https://solidpos.local/problems/invalid-credentials")
                : Results.Ok(session);
        })
        .AllowAnonymous()
        .WithName("Login");

        group.MapPost("/refresh", async Task<IResult> (
            [FromBody] RefreshTokenRequest request,
            IAuthService authService,
            CancellationToken cancellationToken) =>
        {
            AuthSessionResponse? session = await authService.RefreshAsync(request, cancellationToken);

            return session is null
                ? Results.Problem(
                    title: "Invalid refresh token",
                    detail: "Invalid refresh token",
                    statusCode: StatusCodes.Status401Unauthorized,
                    type: "https://solidpos.local/problems/invalid-refresh-token")
                : Results.Ok(session);
        })
        .AllowAnonymous()
        .WithName("RefreshToken");

        group.MapPost("/logout", async Task<IResult> (
            [FromBody] LogoutRequest request,
            IAuthService authService,
            CancellationToken cancellationToken) =>
        {
            await authService.LogoutAsync(request, cancellationToken);
            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithName("Logout");

        group.MapPost("/terminal/register", async Task<IResult> (
            [FromBody] RegisterTerminalRequest request,
            ITerminalEnrollmentService terminalEnrollmentService,
            CancellationToken cancellationToken) =>
        {
            TerminalSessionResponse? session = await terminalEnrollmentService.RegisterTerminalAsync(request, cancellationToken);

            return session is null
                ? Results.Problem(
                    title: "Invalid terminal enrollment token",
                    detail: "Invalid terminal enrollment token",
                    statusCode: StatusCodes.Status401Unauthorized,
                    type: "https://solidpos.local/problems/invalid-terminal-enrollment")
                : Results.Ok(session);
        })
        .AllowAnonymous()
        .WithName("RegisterTerminal");

        return api;
    }
}
