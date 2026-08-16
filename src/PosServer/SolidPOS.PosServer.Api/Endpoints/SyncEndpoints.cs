using Microsoft.AspNetCore.Mvc;
using SolidPOS.PosServer.Application.Security;
using SolidPOS.PosServer.Application.Sync;
using SolidPOS.PosServer.Contracts.Sync;

namespace SolidPOS.PosServer.Api.Endpoints;

public static class SyncEndpoints
{
    public static RouteGroupBuilder MapSyncEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/sync")
            .WithTags("Sync");

        group.MapPost("/push", async Task<IResult> (
            [FromBody] SyncPushRequest request,
            ISyncPushService syncPushService,
            CancellationToken cancellationToken) =>
        {
            SyncPushResponse? response = await syncPushService.PushAsync(request, cancellationToken);

            return response is null
                ? Results.Problem(
                    title: "Sync push rejected",
                    detail: "The sync batch failed validation. Check terminal token, tenant, store, batch id and event limits.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://solidpos.local/problems/sync-push-rejected")
                : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.SyncPush)
        .WithName("PushSyncEvents");

        group.MapPost("/process", async Task<IResult> (
            [FromBody] SyncProcessRequest request,
            ISyncEventProcessingService syncEventProcessingService,
            CancellationToken cancellationToken) =>
        {
            SyncProcessResponse? response = await syncEventProcessingService.ProcessPendingAsync(request, cancellationToken);

            return response is null
                ? Results.Problem(
                    title: "Sync processing rejected",
                    detail: "The sync processor requires a valid terminal runtime context.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://solidpos.local/problems/sync-processing-rejected")
                : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.SyncPush)
        .WithName("ProcessSyncEvents");

        group.MapGet("/pull", async Task<IResult> (
            [AsParameters] SyncPullQuery query,
            ISyncPullService syncPullService,
            CancellationToken cancellationToken) =>
        {
            SyncPullResponse? response = await syncPullService.PullAsync(query.Cursor, query.Limit, cancellationToken);

            return response is null
                ? Results.Problem(
                    title: "Sync pull rejected",
                    detail: "The sync pull requires a valid terminal runtime context and cursor.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://solidpos.local/problems/sync-pull-rejected")
                : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.SyncPull)
        .WithName("PullSyncChanges");

        group.MapGet("/conflicts", async Task<IResult> (
            [AsParameters] SyncConflictQuery query,
            ISyncConflictService syncConflictService,
            CancellationToken cancellationToken) =>
        {
            IReadOnlyCollection<SyncConflictResponse>? response = await syncConflictService.ListAsync(query.Status, query.Limit, cancellationToken);

            return response is null
                ? Results.Problem(
                    title: "Sync conflict list rejected",
                    detail: "The sync conflict list requires a valid tenant context and valid filters.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://solidpos.local/problems/sync-conflict-list-rejected")
                : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.SyncConflictsRead)
        .WithName("ListSyncConflicts");

        group.MapPost("/conflicts/{conflictId:guid}/resolve", async Task<IResult> (
            Guid conflictId,
            [FromBody] ResolveSyncConflictRequest request,
            ISyncConflictService syncConflictService,
            CancellationToken cancellationToken) =>
        {
            SyncConflictResponse? response = await syncConflictService.ResolveAsync(conflictId, request, cancellationToken);

            return response is null
                ? Results.Problem(
                    title: "Sync conflict resolution rejected",
                    detail: "The conflict was not found, is already closed, or the resolution strategy is invalid.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://solidpos.local/problems/sync-conflict-resolution-rejected")
                : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.SyncConflictsResolve)
        .WithName("ResolveSyncConflict");

        group.MapGet("/bootstrap", async Task<IResult> (
            ISyncConflictService syncConflictService,
            CancellationToken cancellationToken) =>
        {
            SyncBootstrapResponse? response = await syncConflictService.BootstrapAsync(cancellationToken);

            return response is null
                ? Results.Problem(
                    title: "Sync bootstrap rejected",
                    detail: "Bootstrap requires a valid terminal runtime context.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://solidpos.local/problems/sync-bootstrap-rejected")
                : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.SyncPull)
        .WithName("SyncBootstrap");

        return api;
    }

    public sealed record SyncPullQuery(string? Cursor, int Limit);

    public sealed record SyncConflictQuery(string? Status, int Limit);
}
