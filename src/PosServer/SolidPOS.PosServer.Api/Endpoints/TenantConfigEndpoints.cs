using Microsoft.AspNetCore.Mvc;
using SolidPOS.PosServer.Application.Security;
using SolidPOS.PosServer.Application.Tenants;
using SolidPOS.PosServer.Contracts.Tenants;

namespace SolidPOS.PosServer.Api.Endpoints;

public static class TenantConfigEndpoints
{
    public static RouteGroupBuilder MapTenantConfigEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/tenant")
            .WithTags("Tenant Config");

        group.MapGet("/config", async Task<IResult> (
            ITenantConfigService tenantConfigService,
            CancellationToken cancellationToken) =>
        {
            TenantConfigResponse? config = await tenantConfigService.GetCurrentAsync(cancellationToken);

            return config is null
                ? Results.NotFound(new
                {
                    type = "https://solidpos.local/problems/tenant-config-not-found",
                    title = "Tenant config not found",
                    status = StatusCodes.Status404NotFound
                })
                : Results.Ok(config);
        })
        .RequireAuthorization(PermissionCodes.CatalogRead)
        .WithName("GetTenantConfig");

        group.MapPut("/config", async Task<IResult> (
            [FromBody] UpdateTenantConfigRequest request,
            ITenantConfigService tenantConfigService,
            CancellationToken cancellationToken) =>
        {
            TenantConfigResponse? config = await tenantConfigService.UpdateCurrentAsync(request, cancellationToken);

            return config is null
                ? Results.Problem(
                    title: "Tenant config update rejected",
                    detail: "The request is invalid or the expected version does not match.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://solidpos.local/problems/tenant-config-update-rejected")
                : Results.Ok(config);
        })
        .RequireAuthorization(PermissionCodes.BuilderManage)
        .WithName("UpdateTenantConfig");

        return api;
    }
}
