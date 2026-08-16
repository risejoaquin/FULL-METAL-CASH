using SolidPOS.PosServer.Contracts.Admin;

namespace SolidPOS.PosServer.Application.Admin;

public interface IAdminMutationRepository
{
    Task<AdminCategoryResponse?> UpsertCategoryAsync(Guid tenantId, Guid categoryId, UpdateAdminCategoryRequest request, CancellationToken cancellationToken);

    Task<AdminProductResponse?> UpsertProductAsync(Guid tenantId, Guid productId, UpdateAdminProductRequest request, CancellationToken cancellationToken);

    Task<AdminProductPriceResponse?> UpsertProductPriceAsync(Guid tenantId, Guid priceId, UpdateAdminProductPriceRequest request, CancellationToken cancellationToken);

    Task<AdminVariantResponse?> UpsertVariantAsync(Guid tenantId, Guid variantId, UpdateAdminVariantRequest request, CancellationToken cancellationToken);

    Task<AdminBarcodeResponse?> UpsertBarcodeAsync(Guid tenantId, Guid barcodeId, UpdateAdminBarcodeRequest request, CancellationToken cancellationToken);

    Task<AdminModifierGroupResponse?> UpsertModifierGroupAsync(Guid tenantId, Guid modifierGroupId, UpdateAdminModifierGroupRequest request, CancellationToken cancellationToken);

    Task<AdminModifierResponse?> UpsertModifierAsync(Guid tenantId, Guid modifierId, UpdateAdminModifierRequest request, CancellationToken cancellationToken);

    Task<AdminRecipeResponse?> UpsertRecipeAsync(Guid tenantId, Guid recipeId, UpdateAdminRecipeRequest request, CancellationToken cancellationToken);

    Task<AdminRecipeItemResponse?> UpsertRecipeItemAsync(Guid tenantId, Guid recipeId, Guid recipeItemId, UpdateAdminRecipeItemRequest request, CancellationToken cancellationToken);

    Task<AdminUserAccessResponse?> UpdateUserAccessAsync(Guid tenantId, Guid userId, UpdateAdminUserAccessRequest request, CancellationToken cancellationToken);

    Task<AdminRolePermissionsResponse?> UpdateRolePermissionsAsync(Guid tenantId, Guid roleId, UpdateAdminRolePermissionsRequest request, CancellationToken cancellationToken);

    Task<AdminSoftDeleteResponse?> SoftDeleteCatalogEntityAsync(Guid tenantId, string entityType, Guid entityId, CancellationToken cancellationToken);
}
