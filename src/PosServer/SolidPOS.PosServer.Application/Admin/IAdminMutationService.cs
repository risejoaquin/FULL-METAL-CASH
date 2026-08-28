using SolidPOS.PosServer.Contracts.Admin;

namespace SolidPOS.PosServer.Application.Admin;

public interface IAdminMutationService
{
    Task<AdminCategoryResponse?> UpsertCategoryAsync(Guid categoryId, UpdateAdminCategoryRequest request, CancellationToken cancellationToken);

    Task<AdminProductResponse?> UpsertProductAsync(Guid productId, UpdateAdminProductRequest request, CancellationToken cancellationToken);

    Task<AdminProductPriceResponse?> UpsertProductPriceAsync(Guid priceId, UpdateAdminProductPriceRequest request, CancellationToken cancellationToken);

    Task<AdminVariantResponse?> UpsertVariantAsync(Guid variantId, UpdateAdminVariantRequest request, CancellationToken cancellationToken);

    Task<AdminBarcodeResponse?> UpsertBarcodeAsync(Guid barcodeId, UpdateAdminBarcodeRequest request, CancellationToken cancellationToken);

    Task<AdminModifierGroupResponse?> UpsertModifierGroupAsync(Guid modifierGroupId, UpdateAdminModifierGroupRequest request, CancellationToken cancellationToken);

    Task<AdminModifierResponse?> UpsertModifierAsync(Guid modifierId, UpdateAdminModifierRequest request, CancellationToken cancellationToken);

    Task<AdminRecipeResponse?> UpsertRecipeAsync(Guid recipeId, UpdateAdminRecipeRequest request, CancellationToken cancellationToken);

    Task<AdminRecipeItemResponse?> UpsertRecipeItemAsync(Guid recipeId, Guid recipeItemId, UpdateAdminRecipeItemRequest request, CancellationToken cancellationToken);

    Task<AdminUserAccessResponse?> UpdateUserAccessAsync(Guid userId, UpdateAdminUserAccessRequest request, CancellationToken cancellationToken);

    Task<AdminRolePermissionsResponse?> UpdateRolePermissionsAsync(Guid roleId, UpdateAdminRolePermissionsRequest request, CancellationToken cancellationToken);

    Task<AdminSoftDeleteResponse?> SoftDeleteCatalogEntityAsync(string entityType, Guid entityId, CancellationToken cancellationToken);
}
