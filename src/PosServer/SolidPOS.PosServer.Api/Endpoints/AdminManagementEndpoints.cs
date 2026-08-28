using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.AdminManagement;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Security;
using SolidPOS.PosServer.Application.Tenants;
using SolidPOS.PosServer.Contracts.AdminManagement;
using SolidPOS.PosServer.Contracts.Tenants;

namespace SolidPOS.PosServer.Api.Endpoints;

public static class AdminManagementEndpoints
{
    public static RouteGroupBuilder MapAdminManagementEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/tenants/current", async Task<IResult> (
            IAdminManagementService adminManagementService,
            CancellationToken cancellationToken) =>
        {
            TenantCurrentResponse? response = await adminManagementService.GetCurrentTenantAsync(cancellationToken);
            return response is null ? NotFound("tenant-not-found", "Tenant not found") : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.TenantManage)
        .WithTags("Tenant Admin")
        .WithName("GetCurrentTenant");

        api.MapPatch("/tenants/current/settings", async Task<IResult> (
            [FromBody] UpdateTenantConfigRequest request,
            ITenantConfigService tenantConfigService,
            ITenantContext tenantContext,
            IAuditEventWriter auditEventWriter,
            CancellationToken cancellationToken) =>
        {
            TenantConfigResponse? response = await tenantConfigService.UpdateCurrentAsync(request, cancellationToken);
            if (response is null)
            {
                return Results.Problem(
                    title: "Tenant settings update rejected",
                    detail: "The request is invalid or the expected version does not match.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://solidpos.local/problems/tenant-settings-update-rejected");
            }

            if (tenantContext.TenantId is Guid tenantId)
            {
                await auditEventWriter.AppendAsync(
                    tenantId,
                    "tenant.settings.updated",
                    "tenant_settings",
                    tenantId,
                    null,
                    JsonSerializer.SerializeToElement(response),
                    cancellationToken);
            }

            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.TenantManage)
        .WithTags("Tenant Admin")
        .WithName("PatchCurrentTenantSettings");

        RouteGroupBuilder stores = api.MapGroup("/stores").WithTags("Stores");

        stores.MapGet("", async Task<IResult> (
            IAdminManagementService adminManagementService,
            CancellationToken cancellationToken) =>
        {
            IReadOnlyCollection<StoreResponse> response = await adminManagementService.ListStoresAsync(cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.StoresManage)
        .WithName("ListStores");

        stores.MapPost("", async Task<IResult> (
            [FromBody] CreateStoreRequest request,
            IAdminManagementService adminManagementService,
            CancellationToken cancellationToken) =>
        {
            StoreResponse? response = await adminManagementService.CreateStoreAsync(request, cancellationToken);
            return response is null ? Rejected("Store creation rejected") : Results.Created($"/api/v1/stores/{response.Id}", response);
        })
        .RequireAuthorization(PermissionCodes.StoresManage)
        .WithName("CreateStore");

        stores.MapPatch("/{storeId:guid}", async Task<IResult> (
            [FromRoute] Guid storeId,
            [FromBody] UpdateStoreRequest request,
            IAdminManagementService adminManagementService,
            CancellationToken cancellationToken) =>
        {
            StoreResponse? response = await adminManagementService.UpdateStoreAsync(storeId, request, cancellationToken);
            return response is null ? Rejected("Store update rejected") : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.StoresManage)
        .WithName("UpdateStore");

        RouteGroupBuilder users = api.MapGroup("/users").WithTags("Users");

        users.MapGet("", async Task<IResult> (
            IAdminManagementService adminManagementService,
            CancellationToken cancellationToken) =>
        {
            IReadOnlyCollection<UserResponse> response = await adminManagementService.ListUsersAsync(cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.UsersManage)
        .WithName("ListUsers");

        users.MapPost("", async Task<IResult> (
            [FromBody] CreateUserRequest request,
            IAdminManagementService adminManagementService,
            CancellationToken cancellationToken) =>
        {
            UserResponse? response = await adminManagementService.CreateUserAsync(request, cancellationToken);
            return response is null ? Rejected("User creation rejected") : Results.Created($"/api/v1/users/{response.Id}", response);
        })
        .RequireAuthorization(PermissionCodes.UsersManage)
        .WithName("CreateUser");

        users.MapPatch("/{userId:guid}", async Task<IResult> (
            [FromRoute] Guid userId,
            [FromBody] UpdateUserRequest request,
            IAdminManagementService adminManagementService,
            CancellationToken cancellationToken) =>
        {
            UserResponse? response = await adminManagementService.UpdateUserAsync(userId, request, cancellationToken);
            return response is null ? Rejected("User update rejected") : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.UsersManage)
        .WithName("UpdateUser");

        api.MapGet("/roles", async Task<IResult> (
            IAdminManagementService adminManagementService,
            CancellationToken cancellationToken) =>
        {
            IReadOnlyCollection<RoleResponse> response = await adminManagementService.ListRolesAsync(cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.RolesManage)
        .WithTags("Roles")
        .WithName("ListRoles");

        api.MapGet("/permissions", async Task<IResult> (
            IAdminManagementService adminManagementService,
            CancellationToken cancellationToken) =>
        {
            IReadOnlyCollection<PermissionResponse> response = await adminManagementService.ListPermissionsAsync(cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.RolesManage)
        .WithTags("Permissions")
        .WithName("ListPermissions");

        return api;
    }

    private static IResult Rejected(string title)
    {
        return Results.Problem(
            title: title,
            detail: "The request is invalid, conflicts with an existing resource, or references entities outside the tenant.",
            statusCode: StatusCodes.Status409Conflict,
            type: "https://solidpos.local/problems/admin-management-rejected");
    }

    private static IResult NotFound(string code, string title)
    {
        return Results.NotFound(new
        {
            type = $"https://solidpos.local/problems/{code}",
            title,
            status = StatusCodes.Status404NotFound
        });
    }
}
