using Microsoft.AspNetCore.Mvc;
using SolidPOS.PosServer.Application.BuilderUpdates;
using SolidPOS.PosServer.Application.Security;
using SolidPOS.PosServer.Contracts.BuilderUpdates;

namespace SolidPOS.PosServer.Api.Endpoints;

public static class BuilderUpdatesEndpoints
{
    public static RouteGroupBuilder MapBuilderUpdatesEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder builder = api.MapGroup("/builder/projects").WithTags("Builder");

        builder.MapGet("", async Task<IResult> (
            IBuilderUpdatesService service,
            CancellationToken cancellationToken) =>
        {
            IReadOnlyCollection<BuilderProjectResponse> response = await service.ListProjectsAsync(cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.BuilderManage)
        .WithName("ListBuilderProjects");

        builder.MapPost("", async Task<IResult> (
            [FromBody] CreateBuilderProjectRequest request,
            IBuilderUpdatesService service,
            CancellationToken cancellationToken) =>
        {
            BuilderProjectResponse? response = await service.CreateProjectAsync(request, cancellationToken);
            return response is null
                ? Rejected("Builder project creation rejected")
                : Results.Created($"/api/v1/builder/projects/{response.Id}", response);
        })
        .RequireAuthorization(PermissionCodes.BuilderManage)
        .WithName("CreateBuilderProject");

        builder.MapPost("/{projectId:guid}/builds", async Task<IResult> (
            [FromRoute] Guid projectId,
            [FromBody] CreateBuilderBuildRequest request,
            IBuilderUpdatesService service,
            CancellationToken cancellationToken) =>
        {
            BuilderBuildResponse? response = await service.CreateBuildAsync(projectId, request, cancellationToken);
            return response is null
                ? Rejected("Builder build creation rejected")
                : Results.Created($"/api/v1/builder/projects/{projectId}/builds/{response.Id}", response);
        })
        .RequireAuthorization(PermissionCodes.BuilderManage)
        .WithName("CreateBuilderBuild");

        RouteGroupBuilder updates = api.MapGroup("/updates").WithTags("Updates");

        updates.MapGet("/channels", async Task<IResult> (
            IBuilderUpdatesService service,
            CancellationToken cancellationToken) =>
        {
            IReadOnlyCollection<UpdateChannelResponse> response = await service.ListChannelsAsync(cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.UpdatesManage)
        .WithName("ListUpdateChannels");

        updates.MapPost("/releases", async Task<IResult> (
            [FromBody] CreateUpdateReleaseRequest request,
            IBuilderUpdatesService service,
            CancellationToken cancellationToken) =>
        {
            UpdateReleaseResponse? response = await service.CreateReleaseAsync(request, cancellationToken);
            return response is null
                ? Rejected("Update release creation rejected")
                : Results.Created($"/api/v1/updates/releases/{response.Id}", response);
        })
        .RequireAuthorization(PermissionCodes.UpdatesManage)
        .WithName("CreateUpdateRelease");

        updates.MapGet("/check", async Task<IResult> (
            [FromQuery] string? currentVersion,
            [FromQuery] string? channel,
            [FromQuery] string? packageType,
            IBuilderUpdatesService service,
            CancellationToken cancellationToken) =>
        {
            UpdateCheckResponse? response = await service.CheckForUpdateAsync(currentVersion, channel, packageType, cancellationToken);
            return response is null
                ? Rejected("Update check rejected")
                : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.UpdatesManage)
        .WithName("CheckForUpdate");

        return api;
    }

    private static IResult Rejected(string title)
    {
        return Results.Problem(
            title: title,
            detail: "The request is invalid, conflicts with an existing resource, or references entities outside the tenant.",
            statusCode: StatusCodes.Status409Conflict,
            type: "https://solidpos.local/problems/builder-updates-rejected");
    }
}
