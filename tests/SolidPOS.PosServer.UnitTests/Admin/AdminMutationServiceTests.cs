using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Admin;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Sync;
using SolidPOS.PosServer.Contracts.Admin;
using SolidPOS.PosServer.Infrastructure.Admin;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Admin;

public sealed class AdminMutationServiceTests
{
    [Fact]
    public async Task Upsert_product_writes_tenant_catalog_sync_change()
    {
        Guid tenantId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        UpdateAdminProductRequest request = CreateProductRequest();
        AdminProductResponse response = CreateProductResponse(tenantId, productId, request, version: 4);

        Mock<IAdminMutationRepository> repository = new();
        repository
            .Setup(x => x.UpsertProductAsync(tenantId, productId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        AdminMutationService service = CreateService(tenantId, repository.Object, out Mock<ISyncChangeWriter> syncChangeWriter);

        AdminProductResponse? result = await service.UpsertProductAsync(productId, request, CancellationToken.None);

        Assert.NotNull(result);
        syncChangeWriter.Verify(
            x => x.AppendAsync(
                tenantId,
                null,
                "tenant.catalog",
                productId,
                "update",
                4,
                It.IsAny<JsonElement>(),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Update_user_access_writes_tenant_access_sync_change()
    {
        Guid tenantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        UpdateAdminUserAccessRequest request = new("Caja Manager", "active", ["manager"]);
        AdminUserAccessResponse response = new(
            userId,
            tenantId,
            "manager@solidpos.local",
            request.FullName,
            request.Status,
            request.RoleCodes,
            1786810000000,
            DateTimeOffset.UtcNow);

        Mock<IAdminMutationRepository> repository = new();
        repository
            .Setup(x => x.UpdateUserAccessAsync(tenantId, userId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        AdminMutationService service = CreateService(tenantId, repository.Object, out Mock<ISyncChangeWriter> syncChangeWriter);

        AdminUserAccessResponse? result = await service.UpdateUserAccessAsync(userId, request, CancellationToken.None);

        Assert.NotNull(result);
        syncChangeWriter.Verify(
            x => x.AppendAsync(
                tenantId,
                null,
                "tenant.access",
                userId,
                "update",
                response.Version,
                It.IsAny<JsonElement>(),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Upsert_variant_writes_tenant_catalog_sync_change()
    {
        Guid tenantId = Guid.NewGuid();
        Guid variantId = Guid.NewGuid();
        UpdateAdminVariantRequest request = new(
            Guid.NewGuid(),
            "FRAPPE-DEMO-20",
            "Frappe Demo 20oz",
            JsonDocument.Parse("""{"size":"20oz"}""").RootElement.Clone(),
            "active",
            null);
        AdminVariantResponse response = new(
            variantId,
            tenantId,
            request.ProductId,
            request.Sku,
            request.Name,
            request.Attributes,
            request.Status,
            3,
            DateTimeOffset.UtcNow);

        Mock<IAdminMutationRepository> repository = new();
        repository
            .Setup(x => x.UpsertVariantAsync(tenantId, variantId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        AdminMutationService service = CreateService(tenantId, repository.Object, out Mock<ISyncChangeWriter> syncChangeWriter);

        AdminVariantResponse? result = await service.UpsertVariantAsync(variantId, request, CancellationToken.None);

        Assert.NotNull(result);
        syncChangeWriter.Verify(
            x => x.AppendAsync(
                tenantId,
                null,
                "tenant.catalog",
                variantId,
                "update",
                3,
                It.IsAny<JsonElement>(),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Upsert_recipe_item_rejects_non_positive_quantity_without_sync_change()
    {
        Guid tenantId = Guid.NewGuid();
        Guid recipeId = Guid.NewGuid();
        Guid recipeItemId = Guid.NewGuid();
        UpdateAdminRecipeItemRequest request = new(Guid.NewGuid(), null, "0", Guid.NewGuid(), false);

        Mock<IAdminMutationRepository> repository = new();
        AdminMutationService service = CreateService(tenantId, repository.Object, out Mock<ISyncChangeWriter> syncChangeWriter);

        AdminRecipeItemResponse? result = await service.UpsertRecipeItemAsync(recipeId, recipeItemId, request, CancellationToken.None);

        Assert.Null(result);
        repository.Verify(
            x => x.UpsertRecipeItemAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<UpdateAdminRecipeItemRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        syncChangeWriter.Verify(
            x => x.AppendAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<JsonElement>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Upsert_category_rejects_invalid_status_without_sync_change()
    {
        Guid tenantId = Guid.NewGuid();
        Guid categoryId = Guid.NewGuid();
        UpdateAdminCategoryRequest request = new(null, "Bebidas", 1, "deleted", null);

        Mock<IAdminMutationRepository> repository = new();
        AdminMutationService service = CreateService(tenantId, repository.Object, out Mock<ISyncChangeWriter> syncChangeWriter);

        AdminCategoryResponse? result = await service.UpsertCategoryAsync(categoryId, request, CancellationToken.None);

        Assert.Null(result);
        repository.Verify(
            x => x.UpsertCategoryAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<UpdateAdminCategoryRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        syncChangeWriter.Verify(
            x => x.AppendAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<JsonElement>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Soft_delete_catalog_entity_writes_delete_delta_and_audit_event()
    {
        Guid tenantId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        DateTimeOffset deletedAt = DateTimeOffset.UtcNow;
        AdminSoftDeleteResponse response = new(tenantId, "product", productId, "tenant.catalog", 8, deletedAt);

        Mock<IAdminMutationRepository> repository = new();
        repository
            .Setup(x => x.SoftDeleteCatalogEntityAsync(tenantId, "products", productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        AdminMutationService service = CreateService(
            tenantId,
            repository.Object,
            out Mock<ISyncChangeWriter> syncChangeWriter,
            out Mock<IAuditEventWriter> auditEventWriter);

        AdminSoftDeleteResponse? result = await service.SoftDeleteCatalogEntityAsync("products", productId, CancellationToken.None);

        Assert.NotNull(result);
        syncChangeWriter.Verify(
            x => x.AppendAsync(
                tenantId,
                null,
                "tenant.catalog",
                productId,
                "delete",
                8,
                It.IsAny<JsonElement>(),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
        auditEventWriter.Verify(
            x => x.AppendAsync(
                tenantId,
                "admin.catalog.product.soft_delete",
                "product",
                productId,
                null,
                It.IsAny<JsonElement>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static AdminMutationService CreateService(Guid tenantId, IAdminMutationRepository repository, out Mock<ISyncChangeWriter> syncChangeWriter)
    {
        return CreateService(tenantId, repository, out syncChangeWriter, out _);
    }

    private static AdminMutationService CreateService(
        Guid tenantId,
        IAdminMutationRepository repository,
        out Mock<ISyncChangeWriter> syncChangeWriter,
        out Mock<IAuditEventWriter> auditEventWriter)
    {
        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);

        syncChangeWriter = new Mock<ISyncChangeWriter>();
        auditEventWriter = new Mock<IAuditEventWriter>();
        auditEventWriter
            .Setup(x => x.AppendAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<JsonElement?>(),
                It.IsAny<JsonElement?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new AdminMutationService(
            tenantContext.Object,
            repository,
            syncChangeWriter.Object,
            auditEventWriter.Object,
            Mock.Of<ILogger<AdminMutationService>>());
    }

    private static UpdateAdminProductRequest CreateProductRequest()
    {
        return new UpdateAdminProductRequest(
            null,
            "TE-DEMO",
            "Te chai demo",
            "Producto admin smoke",
            "simple",
            Guid.NewGuid(),
            Guid.NewGuid(),
            true,
            true,
            true,
            "taxable",
            JsonDocument.Parse("""{"adminSmoke":true}""").RootElement.Clone(),
            "active",
            null);
    }

    private static AdminProductResponse CreateProductResponse(Guid tenantId, Guid productId, UpdateAdminProductRequest request, long version)
    {
        return new AdminProductResponse(
            productId,
            tenantId,
            request.CategoryId,
            request.Sku,
            request.Name,
            request.Description,
            request.ProductType,
            request.SaleUnitId,
            request.InventoryUnitId,
            request.IsSellable,
            request.IsStockTracked,
            request.AllowNegativeStock,
            request.TaxMode,
            request.Attributes,
            request.Status,
            version,
            DateTimeOffset.UtcNow);
    }
}
