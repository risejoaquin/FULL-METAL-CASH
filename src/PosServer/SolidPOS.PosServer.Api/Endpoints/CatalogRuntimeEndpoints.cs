using SolidPOS.PosServer.Application.Catalog;
using SolidPOS.PosServer.Application.Security;
using SolidPOS.PosServer.Contracts.Catalog;

namespace SolidPOS.PosServer.Api.Endpoints;

public static class CatalogRuntimeEndpoints
{
    public static RouteGroupBuilder MapCatalogRuntimeEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/tenant")
            .WithTags("Catalog");

        group.MapGet("/catalog", async Task<IResult> (
            ICatalogRuntimeService catalogRuntimeService,
            CancellationToken cancellationToken) =>
        {
            CatalogSnapshotResponse? snapshot = await catalogRuntimeService.GetSnapshotAsync(cancellationToken);

            return snapshot is null
                ? Results.Problem(
                    title: "Catalog snapshot unavailable",
                    detail: "The tenant context is missing or invalid.",
                    statusCode: StatusCodes.Status401Unauthorized,
                    type: "https://solidpos.local/problems/catalog-snapshot-unavailable")
                : Results.Ok(snapshot);
        })
        .RequireAuthorization(PermissionCodes.CatalogRead)
        .WithName("GetTenantCatalog");

        return api;
    }
}
