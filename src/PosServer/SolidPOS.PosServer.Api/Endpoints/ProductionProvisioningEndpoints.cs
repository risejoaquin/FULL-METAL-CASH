using Microsoft.AspNetCore.Mvc;
using SolidPOS.PosServer.Application.Provisioning;
using SolidPOS.PosServer.Contracts.Provisioning;

namespace SolidPOS.PosServer.Api.Endpoints;

public static class ProductionProvisioningEndpoints
{
    private const string ProvisionKeyHeader = "X-SolidPOS-Provision-Key";

    public static RouteGroupBuilder MapProductionProvisioningEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder provisioning = api.MapGroup("/provisioning")
            .WithTags("Production Provisioning");

        provisioning.MapGet("/status", async Task<IResult> (
            IProductionProvisioningService provisioningService,
            CancellationToken cancellationToken) =>
        {
            ProductionBootstrapStatusResponse response = await provisioningService.GetStatusAsync(cancellationToken);
            return Results.Ok(response);
        })
        .AllowAnonymous()
        .WithName("GetProductionProvisioningStatus");

        provisioning.MapPost("/tenants/bootstrap", async Task<IResult> (
            [FromBody] ProductionTenantBootstrapRequest request,
            HttpContext httpContext,
            IProductionProvisioningService provisioningService,
            CancellationToken cancellationToken) =>
        {
            string? provisionKey = httpContext.Request.Headers.TryGetValue(ProvisionKeyHeader, out var values)
                ? values.ToString()
                : null;

            ProductionTenantBootstrapResponse? response = await provisioningService.BootstrapTenantAsync(request, provisionKey, cancellationToken);
            if (response is null)
            {
                return Results.Problem(
                    title: "Production tenant bootstrap rejected",
                    detail: "The bootstrap key, request payload, password policy, or idempotency state is invalid.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://solidpos.local/problems/production-bootstrap-rejected");
            }

            return response.WasExisting
                ? Results.Ok(response)
                : Results.Created($"/api/v1/tenants/{response.TenantId}", response);
        })
        .AllowAnonymous()
        .WithName("BootstrapProductionTenant");

        return api;
    }
}
