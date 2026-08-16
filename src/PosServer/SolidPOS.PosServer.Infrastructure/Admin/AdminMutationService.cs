using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Admin;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Sync;
using SolidPOS.PosServer.Contracts.Admin;

namespace SolidPOS.PosServer.Infrastructure.Admin;

public sealed class AdminMutationService : IAdminMutationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> EntityStatuses = ["active", "inactive", "archived"];
    private static readonly HashSet<string> UserStatuses = ["active", "suspended", "invited"];
    private static readonly HashSet<string> ProductTypes = ["simple", "variant_parent", "ingredient", "service", "kit", "combo", "recipe_item"];
    private static readonly HashSet<string> TaxModes = ["taxable", "exempt"];

    private readonly ITenantContext _tenantContext;
    private readonly IAdminMutationRepository _repository;
    private readonly ISyncChangeWriter _syncChangeWriter;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly ILogger<AdminMutationService> _logger;

    public AdminMutationService(
        ITenantContext tenantContext,
        IAdminMutationRepository repository,
        ISyncChangeWriter syncChangeWriter,
        IAuditEventWriter auditEventWriter,
        ILogger<AdminMutationService> logger)
    {
        _tenantContext = tenantContext;
        _repository = repository;
        _syncChangeWriter = syncChangeWriter;
        _auditEventWriter = auditEventWriter;
        _logger = logger;
    }

    public async Task<AdminCategoryResponse?> UpsertCategoryAsync(Guid categoryId, UpdateAdminCategoryRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(out Guid tenantId) || string.IsNullOrWhiteSpace(request.Name) || !EntityStatuses.Contains(request.Status))
        {
            _logger.LogWarning("Admin category mutation rejected for tenant {TenantId}", _tenantContext.TenantId);
            return null;
        }

        AdminCategoryResponse? response = await _repository.UpsertCategoryAsync(tenantId, categoryId, request, cancellationToken);
        if (response is null)
        {
            _logger.LogWarning("Admin category mutation rejected by repository for tenant {TenantId} category {CategoryId}", tenantId, categoryId);
            return null;
        }

        await WriteCatalogChangeAsync(tenantId, categoryId, response.Version, response, cancellationToken);
        await WriteAuditAsync(tenantId, "admin.catalog.category.upsert", "category", categoryId, response, cancellationToken);
        _logger.LogInformation("Admin category mutated for tenant {TenantId} category {CategoryId} version {Version}", tenantId, categoryId, response.Version);
        return response;
    }

    public async Task<AdminProductResponse?> UpsertProductAsync(Guid productId, UpdateAdminProductRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(out Guid tenantId)
            || string.IsNullOrWhiteSpace(request.Sku)
            || string.IsNullOrWhiteSpace(request.Name)
            || !ProductTypes.Contains(request.ProductType)
            || !TaxModes.Contains(request.TaxMode)
            || !EntityStatuses.Contains(request.Status))
        {
            _logger.LogWarning("Admin product mutation rejected for tenant {TenantId}", _tenantContext.TenantId);
            return null;
        }

        AdminProductResponse? response = await _repository.UpsertProductAsync(tenantId, productId, request, cancellationToken);
        if (response is null)
        {
            _logger.LogWarning("Admin product mutation rejected by repository for tenant {TenantId} product {ProductId}", tenantId, productId);
            return null;
        }

        await WriteCatalogChangeAsync(tenantId, productId, response.Version, response, cancellationToken);
        await WriteAuditAsync(tenantId, "admin.catalog.product.upsert", "product", productId, response, cancellationToken);
        _logger.LogInformation("Admin product mutated for tenant {TenantId} product {ProductId} version {Version}", tenantId, productId, response.Version);
        return response;
    }

    public async Task<AdminProductPriceResponse?> UpsertProductPriceAsync(Guid priceId, UpdateAdminProductPriceRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(out Guid tenantId)
            || request.PriceCents < 0
            || string.IsNullOrWhiteSpace(request.Currency)
            || (request.EndsAt.HasValue && request.StartsAt.HasValue && request.EndsAt <= request.StartsAt))
        {
            _logger.LogWarning("Admin price mutation rejected for tenant {TenantId}", _tenantContext.TenantId);
            return null;
        }

        AdminProductPriceResponse? response = await _repository.UpsertProductPriceAsync(tenantId, priceId, request, cancellationToken);
        if (response is null)
        {
            _logger.LogWarning("Admin price mutation rejected by repository for tenant {TenantId} price {PriceId}", tenantId, priceId);
            return null;
        }

        await _syncChangeWriter.AppendAsync(
            tenantId,
            null,
            "price.updated",
            response.Id,
            "update",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            JsonSerializer.SerializeToElement(response, JsonOptions),
            _tenantContext.TerminalId,
            cancellationToken);

        await WriteAuditAsync(tenantId, "admin.catalog.price.upsert", "price", priceId, response, cancellationToken);
        _logger.LogInformation("Admin price mutated for tenant {TenantId} price {PriceId}", tenantId, priceId);
        return response;
    }

    public async Task<AdminVariantResponse?> UpsertVariantAsync(Guid variantId, UpdateAdminVariantRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(out Guid tenantId)
            || string.IsNullOrWhiteSpace(request.Sku)
            || string.IsNullOrWhiteSpace(request.Name)
            || !EntityStatuses.Contains(request.Status))
        {
            _logger.LogWarning("Admin variant mutation rejected for tenant {TenantId}", _tenantContext.TenantId);
            return null;
        }

        AdminVariantResponse? response = await _repository.UpsertVariantAsync(tenantId, variantId, request, cancellationToken);
        if (response is null)
        {
            _logger.LogWarning("Admin variant mutation rejected by repository for tenant {TenantId} variant {VariantId}", tenantId, variantId);
            return null;
        }

        await WriteCatalogChangeAsync(tenantId, variantId, response.Version, response, cancellationToken);
        await WriteAuditAsync(tenantId, "admin.catalog.variant.upsert", "variant", variantId, response, cancellationToken);
        _logger.LogInformation("Admin variant mutated for tenant {TenantId} variant {VariantId} version {Version}", tenantId, variantId, response.Version);
        return response;
    }

    public async Task<AdminBarcodeResponse?> UpsertBarcodeAsync(Guid barcodeId, UpdateAdminBarcodeRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(out Guid tenantId)
            || string.IsNullOrWhiteSpace(request.Barcode)
            || !IsPositiveDecimal(request.Quantity))
        {
            _logger.LogWarning("Admin barcode mutation rejected for tenant {TenantId}", _tenantContext.TenantId);
            return null;
        }

        AdminBarcodeResponse? response = await _repository.UpsertBarcodeAsync(tenantId, barcodeId, request, cancellationToken);
        if (response is null)
        {
            _logger.LogWarning("Admin barcode mutation rejected by repository for tenant {TenantId} barcode {BarcodeId}", tenantId, barcodeId);
            return null;
        }

        await WriteCatalogChangeAsync(tenantId, barcodeId, response.Version, response, cancellationToken);
        await WriteAuditAsync(tenantId, "admin.catalog.barcode.upsert", "barcode", barcodeId, response, cancellationToken);
        _logger.LogInformation("Admin barcode mutated for tenant {TenantId} barcode {BarcodeId}", tenantId, barcodeId);
        return response;
    }

    public async Task<AdminModifierGroupResponse?> UpsertModifierGroupAsync(Guid modifierGroupId, UpdateAdminModifierGroupRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(out Guid tenantId)
            || string.IsNullOrWhiteSpace(request.Name)
            || request.MinSelected < 0
            || request.MaxSelected < request.MinSelected)
        {
            _logger.LogWarning("Admin modifier group mutation rejected for tenant {TenantId}", _tenantContext.TenantId);
            return null;
        }

        AdminModifierGroupResponse? response = await _repository.UpsertModifierGroupAsync(tenantId, modifierGroupId, request, cancellationToken);
        if (response is null)
        {
            _logger.LogWarning("Admin modifier group mutation rejected by repository for tenant {TenantId} group {ModifierGroupId}", tenantId, modifierGroupId);
            return null;
        }

        await WriteCatalogChangeAsync(tenantId, modifierGroupId, response.Version, response, cancellationToken);
        await WriteAuditAsync(tenantId, "admin.catalog.modifier_group.upsert", "modifier_group", modifierGroupId, response, cancellationToken);
        _logger.LogInformation("Admin modifier group mutated for tenant {TenantId} group {ModifierGroupId}", tenantId, modifierGroupId);
        return response;
    }

    public async Task<AdminModifierResponse?> UpsertModifierAsync(Guid modifierId, UpdateAdminModifierRequest request, CancellationToken cancellationToken)
    {
        string behavior = request.InventoryBehavior?.Trim().ToLowerInvariant() ?? "none";
        bool effectShapeIsValid = behavior == "none"
            || ((behavior == "add" || behavior == "substitute")
                && request.LinkedProductId.HasValue
                && request.ConsumptionUnitId.HasValue
                && IsPositiveDecimal(request.ConsumptionQuantity)
                && (behavior != "substitute" || request.ReplacesProductId.HasValue));

        if (!TryGetTenantId(out Guid tenantId)
            || string.IsNullOrWhiteSpace(request.Name)
            || !effectShapeIsValid)
        {
            _logger.LogWarning("Admin modifier mutation rejected for tenant {TenantId}", _tenantContext.TenantId);
            return null;
        }

        request = request with { InventoryBehavior = behavior };

        AdminModifierResponse? response = await _repository.UpsertModifierAsync(tenantId, modifierId, request, cancellationToken);
        if (response is null)
        {
            _logger.LogWarning("Admin modifier mutation rejected by repository for tenant {TenantId} modifier {ModifierId}", tenantId, modifierId);
            return null;
        }

        await WriteCatalogChangeAsync(tenantId, modifierId, response.Version, response, cancellationToken);
        await WriteAuditAsync(tenantId, "admin.catalog.modifier.upsert", "modifier", modifierId, response, cancellationToken);
        _logger.LogInformation("Admin modifier mutated for tenant {TenantId} modifier {ModifierId}", tenantId, modifierId);
        return response;
    }

    public async Task<AdminRecipeResponse?> UpsertRecipeAsync(Guid recipeId, UpdateAdminRecipeRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(out Guid tenantId)
            || request.Version <= 0
            || !IsPositiveDecimal(request.YieldQuantity)
            || !IsNonNegativeDecimal(request.WastePercent)
            || !RecipeStatuses.Contains(request.Status))
        {
            _logger.LogWarning("Admin recipe mutation rejected for tenant {TenantId}", _tenantContext.TenantId);
            return null;
        }

        AdminRecipeResponse? response = await _repository.UpsertRecipeAsync(tenantId, recipeId, request, cancellationToken);
        if (response is null)
        {
            _logger.LogWarning("Admin recipe mutation rejected by repository for tenant {TenantId} recipe {RecipeId}", tenantId, recipeId);
            return null;
        }

        await WriteCatalogChangeAsync(tenantId, recipeId, response.Version, response, cancellationToken);
        await WriteAuditAsync(tenantId, "admin.catalog.recipe.upsert", "recipe", recipeId, response, cancellationToken);
        _logger.LogInformation("Admin recipe mutated for tenant {TenantId} recipe {RecipeId} version {Version}", tenantId, recipeId, response.Version);
        return response;
    }

    public async Task<AdminRecipeItemResponse?> UpsertRecipeItemAsync(Guid recipeId, Guid recipeItemId, UpdateAdminRecipeItemRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(out Guid tenantId) || !IsPositiveDecimal(request.Quantity))
        {
            _logger.LogWarning("Admin recipe item mutation rejected for tenant {TenantId}", _tenantContext.TenantId);
            return null;
        }

        AdminRecipeItemResponse? response = await _repository.UpsertRecipeItemAsync(tenantId, recipeId, recipeItemId, request, cancellationToken);
        if (response is null)
        {
            _logger.LogWarning("Admin recipe item mutation rejected by repository for tenant {TenantId} recipeItem {RecipeItemId}", tenantId, recipeItemId);
            return null;
        }

        await WriteCatalogChangeAsync(tenantId, recipeItemId, response.Version, response, cancellationToken);
        await WriteAuditAsync(tenantId, "admin.catalog.recipe_item.upsert", "recipe_item", recipeItemId, response, cancellationToken);
        _logger.LogInformation("Admin recipe item mutated for tenant {TenantId} recipeItem {RecipeItemId}", tenantId, recipeItemId);
        return response;
    }

    public async Task<AdminUserAccessResponse?> UpdateUserAccessAsync(Guid userId, UpdateAdminUserAccessRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(out Guid tenantId)
            || string.IsNullOrWhiteSpace(request.FullName)
            || !UserStatuses.Contains(request.Status)
            || request.RoleCodes.Count == 0)
        {
            _logger.LogWarning("Admin user access mutation rejected for tenant {TenantId}", _tenantContext.TenantId);
            return null;
        }

        AdminUserAccessResponse? response = await _repository.UpdateUserAccessAsync(tenantId, userId, request, cancellationToken);
        if (response is null)
        {
            _logger.LogWarning("Admin user access mutation rejected by repository for tenant {TenantId} user {UserId}", tenantId, userId);
            return null;
        }

        await WriteAccessChangeAsync(tenantId, userId, response.Version, response, cancellationToken);
        await WriteAuditAsync(tenantId, "admin.access.user.update", "user", userId, response, cancellationToken);
        _logger.LogInformation("Admin user access mutated for tenant {TenantId} user {UserId}", tenantId, userId);
        return response;
    }

    public async Task<AdminRolePermissionsResponse?> UpdateRolePermissionsAsync(Guid roleId, UpdateAdminRolePermissionsRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(out Guid tenantId) || request.PermissionCodes.Count == 0)
        {
            _logger.LogWarning("Admin role permissions mutation rejected for tenant {TenantId}", _tenantContext.TenantId);
            return null;
        }

        AdminRolePermissionsResponse? response = await _repository.UpdateRolePermissionsAsync(tenantId, roleId, request, cancellationToken);
        if (response is null)
        {
            _logger.LogWarning("Admin role permissions mutation rejected by repository for tenant {TenantId} role {RoleId}", tenantId, roleId);
            return null;
        }

        await WriteAccessChangeAsync(tenantId, roleId, response.Version, response, cancellationToken);
        await WriteAuditAsync(tenantId, "admin.access.role_permissions.update", "role", roleId, response, cancellationToken);
        _logger.LogInformation("Admin role permissions mutated for tenant {TenantId} role {RoleId}", tenantId, roleId);
        return response;
    }

    public async Task<AdminSoftDeleteResponse?> SoftDeleteCatalogEntityAsync(string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(out Guid tenantId) || string.IsNullOrWhiteSpace(entityType) || entityId == Guid.Empty)
        {
            _logger.LogWarning("Admin soft delete rejected for tenant {TenantId}", _tenantContext.TenantId);
            return null;
        }

        AdminSoftDeleteResponse? response = await _repository.SoftDeleteCatalogEntityAsync(tenantId, entityType, entityId, cancellationToken);
        if (response is null)
        {
            _logger.LogWarning("Admin soft delete rejected by repository for tenant {TenantId} entityType {EntityType} entityId {EntityId}", tenantId, entityType, entityId);
            return null;
        }

        await _syncChangeWriter.AppendAsync(
            tenantId,
            null,
            response.SyncEntityType,
            entityId,
            "delete",
            response.Version,
            JsonSerializer.SerializeToElement(response, JsonOptions),
            _tenantContext.TerminalId,
            cancellationToken);

        await WriteAuditAsync(tenantId, $"admin.catalog.{response.EntityType}.soft_delete", response.EntityType, entityId, response, cancellationToken);
        _logger.LogInformation("Admin soft deleted {EntityType} {EntityId} for tenant {TenantId}", response.EntityType, entityId, tenantId);
        return response;
    }

    private bool TryGetTenantId(out Guid tenantId)
    {
        tenantId = _tenantContext.TenantId ?? Guid.Empty;
        return tenantId != Guid.Empty;
    }

    private static readonly HashSet<string> RecipeStatuses = ["draft", "active", "inactive", "archived"];

    private static bool IsPositiveDecimal(string? value)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed) && parsed > 0;
    }

    private static bool IsNonNegativeDecimal(string? value)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed) && parsed >= 0;
    }

    private Task WriteCatalogChangeAsync<T>(Guid tenantId, Guid entityId, long version, T payload, CancellationToken cancellationToken)
    {
        return _syncChangeWriter.AppendAsync(
            tenantId,
            null,
            "tenant.catalog",
            entityId,
            "update",
            version,
            JsonSerializer.SerializeToElement(payload, JsonOptions),
            _tenantContext.TerminalId,
            cancellationToken);
    }

    private Task WriteAccessChangeAsync<T>(Guid tenantId, Guid entityId, long version, T payload, CancellationToken cancellationToken)
    {
        return _syncChangeWriter.AppendAsync(
            tenantId,
            null,
            "tenant.access",
            entityId,
            "update",
            version,
            JsonSerializer.SerializeToElement(payload, JsonOptions),
            _tenantContext.TerminalId,
            cancellationToken);
    }

    private Task WriteAuditAsync<T>(Guid tenantId, string action, string entityType, Guid entityId, T afterData, CancellationToken cancellationToken)
    {
        return _auditEventWriter.AppendAsync(
            tenantId,
            action,
            entityType,
            entityId,
            null,
            JsonSerializer.SerializeToElement(afterData, JsonOptions),
            cancellationToken);
    }
}
