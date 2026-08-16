using Microsoft.AspNetCore.Mvc;
using SolidPOS.PosServer.Application.Admin;
using SolidPOS.PosServer.Application.Security;
using SolidPOS.PosServer.Contracts.Admin;

namespace SolidPOS.PosServer.Api.Endpoints;

public static class AdminMutationEndpoints
{
    public static RouteGroupBuilder MapAdminMutationEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder admin = api.MapGroup("/admin")
            .WithTags("Admin Runtime Mutations");

        admin.MapPut("/catalog/categories/{categoryId:guid}", async Task<IResult> (
            Guid categoryId,
            [FromBody] UpdateAdminCategoryRequest request,
            IAdminMutationService adminMutationService,
            CancellationToken cancellationToken) =>
        {
            AdminCategoryResponse? response = await adminMutationService.UpsertCategoryAsync(categoryId, request, cancellationToken);
            return response is null ? Rejected("Category mutation rejected") : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.CatalogManage)
        .WithName("AdminUpsertCategory");

        admin.MapPut("/catalog/products/{productId:guid}", async Task<IResult> (
            Guid productId,
            [FromBody] UpdateAdminProductRequest request,
            IAdminMutationService adminMutationService,
            CancellationToken cancellationToken) =>
        {
            AdminProductResponse? response = await adminMutationService.UpsertProductAsync(productId, request, cancellationToken);
            return response is null ? Rejected("Product mutation rejected") : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.CatalogManage)
        .WithName("AdminUpsertProduct");

        admin.MapPut("/catalog/prices/{priceId:guid}", async Task<IResult> (
            Guid priceId,
            [FromBody] UpdateAdminProductPriceRequest request,
            IAdminMutationService adminMutationService,
            CancellationToken cancellationToken) =>
        {
            AdminProductPriceResponse? response = await adminMutationService.UpsertProductPriceAsync(priceId, request, cancellationToken);
            return response is null ? Rejected("Price mutation rejected") : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.CatalogManage)
        .WithName("AdminUpsertProductPrice");

        admin.MapPut("/catalog/variants/{variantId:guid}", async Task<IResult> (
            Guid variantId,
            [FromBody] UpdateAdminVariantRequest request,
            IAdminMutationService adminMutationService,
            CancellationToken cancellationToken) =>
        {
            AdminVariantResponse? response = await adminMutationService.UpsertVariantAsync(variantId, request, cancellationToken);
            return response is null ? Rejected("Variant mutation rejected") : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.CatalogManage)
        .WithName("AdminUpsertVariant");

        admin.MapPut("/catalog/barcodes/{barcodeId:guid}", async Task<IResult> (
            Guid barcodeId,
            [FromBody] UpdateAdminBarcodeRequest request,
            IAdminMutationService adminMutationService,
            CancellationToken cancellationToken) =>
        {
            AdminBarcodeResponse? response = await adminMutationService.UpsertBarcodeAsync(barcodeId, request, cancellationToken);
            return response is null ? Rejected("Barcode mutation rejected") : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.CatalogManage)
        .WithName("AdminUpsertBarcode");

        admin.MapPut("/catalog/modifier-groups/{modifierGroupId:guid}", async Task<IResult> (
            Guid modifierGroupId,
            [FromBody] UpdateAdminModifierGroupRequest request,
            IAdminMutationService adminMutationService,
            CancellationToken cancellationToken) =>
        {
            AdminModifierGroupResponse? response = await adminMutationService.UpsertModifierGroupAsync(modifierGroupId, request, cancellationToken);
            return response is null ? Rejected("Modifier group mutation rejected") : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.CatalogManage)
        .WithName("AdminUpsertModifierGroup");

        admin.MapPut("/catalog/modifiers/{modifierId:guid}", async Task<IResult> (
            Guid modifierId,
            [FromBody] UpdateAdminModifierRequest request,
            IAdminMutationService adminMutationService,
            CancellationToken cancellationToken) =>
        {
            AdminModifierResponse? response = await adminMutationService.UpsertModifierAsync(modifierId, request, cancellationToken);
            return response is null ? Rejected("Modifier mutation rejected") : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.CatalogManage)
        .WithName("AdminUpsertModifier");

        admin.MapPut("/catalog/recipes/{recipeId:guid}", async Task<IResult> (
            Guid recipeId,
            [FromBody] UpdateAdminRecipeRequest request,
            IAdminMutationService adminMutationService,
            CancellationToken cancellationToken) =>
        {
            AdminRecipeResponse? response = await adminMutationService.UpsertRecipeAsync(recipeId, request, cancellationToken);
            return response is null ? Rejected("Recipe mutation rejected") : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.CatalogManage)
        .WithName("AdminUpsertRecipe");

        admin.MapPut("/catalog/recipes/{recipeId:guid}/items/{recipeItemId:guid}", async Task<IResult> (
            Guid recipeId,
            Guid recipeItemId,
            [FromBody] UpdateAdminRecipeItemRequest request,
            IAdminMutationService adminMutationService,
            CancellationToken cancellationToken) =>
        {
            AdminRecipeItemResponse? response = await adminMutationService.UpsertRecipeItemAsync(recipeId, recipeItemId, request, cancellationToken);
            return response is null ? Rejected("Recipe item mutation rejected") : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.CatalogManage)
        .WithName("AdminUpsertRecipeItem");

        admin.MapDelete("/catalog/{entityType}/{entityId:guid}", async Task<IResult> (
            string entityType,
            Guid entityId,
            IAdminMutationService adminMutationService,
            CancellationToken cancellationToken) =>
        {
            AdminSoftDeleteResponse? response = await adminMutationService.SoftDeleteCatalogEntityAsync(entityType, entityId, cancellationToken);
            return response is null ? Rejected("Catalog soft delete rejected") : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.CatalogManage)
        .WithName("AdminSoftDeleteCatalogEntity");

        admin.MapPut("/access/users/{userId:guid}", async Task<IResult> (
            Guid userId,
            [FromBody] UpdateAdminUserAccessRequest request,
            IAdminMutationService adminMutationService,
            CancellationToken cancellationToken) =>
        {
            AdminUserAccessResponse? response = await adminMutationService.UpdateUserAccessAsync(userId, request, cancellationToken);
            return response is null ? Rejected("User access mutation rejected") : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.UsersManage)
        .WithName("AdminUpdateUserAccess");

        admin.MapPut("/access/roles/{roleId:guid}/permissions", async Task<IResult> (
            Guid roleId,
            [FromBody] UpdateAdminRolePermissionsRequest request,
            IAdminMutationService adminMutationService,
            CancellationToken cancellationToken) =>
        {
            AdminRolePermissionsResponse? response = await adminMutationService.UpdateRolePermissionsAsync(roleId, request, cancellationToken);
            return response is null ? Rejected("Role permissions mutation rejected") : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.RolesManage)
        .WithName("AdminUpdateRolePermissions");

        return api;
    }

    private static IResult Rejected(string title)
    {
        return Results.Problem(
            title: title,
            detail: "The request is invalid, references data outside the tenant, or failed an optimistic version check.",
            statusCode: StatusCodes.Status409Conflict,
            type: "https://solidpos.local/problems/admin-mutation-rejected");
    }
}
