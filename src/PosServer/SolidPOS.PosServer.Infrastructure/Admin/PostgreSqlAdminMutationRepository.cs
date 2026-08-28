using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using SolidPOS.PosServer.Application.Admin;
using SolidPOS.PosServer.Contracts.Admin;
using SolidPOS.PosServer.Infrastructure.PostgreSql;

namespace SolidPOS.PosServer.Infrastructure.Admin;

public sealed class PostgreSqlAdminMutationRepository : IAdminMutationRepository
{
    private readonly string _connectionString;

    public PostgreSqlAdminMutationRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    }

    public async Task<AdminCategoryResponse?> UpsertCategoryAsync(Guid tenantId, Guid categoryId, UpdateAdminCategoryRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.categories (id, tenant_id, parent_id, name, sort_order, status)
            VALUES (@id, @tenant_id, @parent_id, @name, @sort_order, @status)
            ON CONFLICT (id) DO UPDATE SET
              parent_id = EXCLUDED.parent_id,
              name = EXCLUDED.name,
              sort_order = EXCLUDED.sort_order,
              status = EXCLUDED.status,
              version = pos.categories.version + 1,
              deleted_at = NULL
            WHERE pos.categories.tenant_id = @tenant_id
              AND (@expected_version IS NULL OR pos.categories.version = @expected_version)
            RETURNING id, tenant_id, parent_id, name, sort_order, status, version, updated_at;
            """;

        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", categoryId);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        AddNullableUuid(command, "parent_id", request.ParentId);
        command.Parameters.AddWithValue("name", request.Name.Trim());
        command.Parameters.AddWithValue("sort_order", request.SortOrder);
        command.Parameters.AddWithValue("status", request.Status);
        AddNullableBigint(command, "expected_version", request.ExpectedVersion);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AdminCategoryResponse(
            reader.GetGuid(0),
            reader.GetGuid(1),
            NullableGuid(reader, 2),
            reader.GetString(3),
            reader.GetInt32(4),
            reader.GetString(5),
            reader.GetInt64(6),
            reader.GetFieldValue<DateTimeOffset>(7));
    }

    public async Task<AdminProductResponse?> UpsertProductAsync(Guid tenantId, Guid productId, UpdateAdminProductRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.products (
              id, tenant_id, category_id, sku, name, description, product_type,
              sale_unit_id, inventory_unit_id, is_sellable, is_stock_tracked,
              allow_negative_stock, tax_mode, attributes, status
            )
            VALUES (
              @id, @tenant_id, @category_id, @sku, @name, @description, @product_type,
              @sale_unit_id, @inventory_unit_id, @is_sellable, @is_stock_tracked,
              @allow_negative_stock, @tax_mode, CAST(@attributes AS jsonb), @status
            )
            ON CONFLICT (id) DO UPDATE SET
              category_id = EXCLUDED.category_id,
              sku = EXCLUDED.sku,
              name = EXCLUDED.name,
              description = EXCLUDED.description,
              product_type = EXCLUDED.product_type,
              sale_unit_id = EXCLUDED.sale_unit_id,
              inventory_unit_id = EXCLUDED.inventory_unit_id,
              is_sellable = EXCLUDED.is_sellable,
              is_stock_tracked = EXCLUDED.is_stock_tracked,
              allow_negative_stock = EXCLUDED.allow_negative_stock,
              tax_mode = EXCLUDED.tax_mode,
              attributes = EXCLUDED.attributes,
              status = EXCLUDED.status,
              version = pos.products.version + 1,
              deleted_at = NULL
            WHERE pos.products.tenant_id = @tenant_id
              AND (@expected_version IS NULL OR pos.products.version = @expected_version)
            RETURNING
              id, tenant_id, category_id, sku, name, description, product_type,
              sale_unit_id, inventory_unit_id, is_sellable, is_stock_tracked,
              allow_negative_stock, tax_mode, attributes::text, status, version, updated_at;
            """;

        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", productId);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        AddNullableUuid(command, "category_id", request.CategoryId);
        command.Parameters.AddWithValue("sku", request.Sku.Trim());
        command.Parameters.AddWithValue("name", request.Name.Trim());
        command.Parameters.AddWithValue("description", string.IsNullOrWhiteSpace(request.Description) ? DBNull.Value : request.Description.Trim());
        command.Parameters.AddWithValue("product_type", request.ProductType);
        AddNullableUuid(command, "sale_unit_id", request.SaleUnitId);
        AddNullableUuid(command, "inventory_unit_id", request.InventoryUnitId);
        command.Parameters.AddWithValue("is_sellable", request.IsSellable);
        command.Parameters.AddWithValue("is_stock_tracked", request.IsStockTracked);
        command.Parameters.AddWithValue("allow_negative_stock", request.AllowNegativeStock);
        command.Parameters.AddWithValue("tax_mode", request.TaxMode);
        command.Parameters.AddWithValue("attributes", NpgsqlDbType.Jsonb, request.Attributes.GetRawText());
        command.Parameters.AddWithValue("status", request.Status);
        AddNullableBigint(command, "expected_version", request.ExpectedVersion);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AdminProductResponse(
            reader.GetGuid(0),
            reader.GetGuid(1),
            NullableGuid(reader, 2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6),
            NullableGuid(reader, 7),
            NullableGuid(reader, 8),
            reader.GetBoolean(9),
            reader.GetBoolean(10),
            reader.GetBoolean(11),
            reader.GetString(12),
            JsonDocument.Parse(reader.GetString(13)).RootElement.Clone(),
            reader.GetString(14),
            reader.GetInt64(15),
            reader.GetFieldValue<DateTimeOffset>(16));
    }

    public async Task<AdminProductPriceResponse?> UpsertProductPriceAsync(Guid tenantId, Guid priceId, UpdateAdminProductPriceRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.product_prices (id, tenant_id, price_list_id, product_id, variant_id, price_cents, currency, starts_at, ends_at)
            VALUES (@id, @tenant_id, @price_list_id, @product_id, @variant_id, @price_cents, @currency, @starts_at, @ends_at)
            ON CONFLICT (id) DO UPDATE SET
              price_list_id = EXCLUDED.price_list_id,
              product_id = EXCLUDED.product_id,
              variant_id = EXCLUDED.variant_id,
              price_cents = EXCLUDED.price_cents,
              currency = EXCLUDED.currency,
              starts_at = EXCLUDED.starts_at,
              ends_at = EXCLUDED.ends_at,
              deleted_at = NULL
            WHERE pos.product_prices.tenant_id = @tenant_id
            RETURNING id, tenant_id, price_list_id, product_id, variant_id, price_cents, currency::text, starts_at, ends_at;
            """;

        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", priceId);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("price_list_id", request.PriceListId);
        command.Parameters.AddWithValue("product_id", request.ProductId);
        AddNullableUuid(command, "variant_id", request.VariantId);
        command.Parameters.AddWithValue("price_cents", request.PriceCents);
        command.Parameters.AddWithValue("currency", request.Currency.Trim().ToUpperInvariant());
        AddNullableTimestamp(command, "starts_at", request.StartsAt);
        AddNullableTimestamp(command, "ends_at", request.EndsAt);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AdminProductPriceResponse(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            NullableGuid(reader, 4),
            reader.GetInt64(5),
            reader.GetString(6),
            NullableDateTimeOffset(reader, 7),
            NullableDateTimeOffset(reader, 8));
    }

    public async Task<AdminVariantResponse?> UpsertVariantAsync(Guid tenantId, Guid variantId, UpdateAdminVariantRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.product_variants (id, tenant_id, product_id, sku, name, attributes, status)
            VALUES (@id, @tenant_id, @product_id, @sku, @name, CAST(@attributes AS jsonb), @status)
            ON CONFLICT (id) DO UPDATE SET
              product_id = EXCLUDED.product_id,
              sku = EXCLUDED.sku,
              name = EXCLUDED.name,
              attributes = EXCLUDED.attributes,
              status = EXCLUDED.status,
              version = pos.product_variants.version + 1,
              updated_at = now(),
              deleted_at = NULL
            WHERE pos.product_variants.tenant_id = @tenant_id
              AND (@expected_version IS NULL OR pos.product_variants.version = @expected_version)
            RETURNING id, tenant_id, product_id, sku, name, attributes::text, status, version, updated_at;
            """;

        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", variantId);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("product_id", request.ProductId);
        command.Parameters.AddWithValue("sku", request.Sku.Trim());
        command.Parameters.AddWithValue("name", request.Name.Trim());
        command.Parameters.AddWithValue("attributes", NpgsqlDbType.Jsonb, request.Attributes.GetRawText());
        command.Parameters.AddWithValue("status", request.Status);
        AddNullableBigint(command, "expected_version", request.ExpectedVersion);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AdminVariantResponse(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            JsonDocument.Parse(reader.GetString(5)).RootElement.Clone(),
            reader.GetString(6),
            reader.GetInt64(7),
            reader.GetFieldValue<DateTimeOffset>(8));
    }

    public async Task<AdminBarcodeResponse?> UpsertBarcodeAsync(Guid tenantId, Guid barcodeId, UpdateAdminBarcodeRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.product_barcodes (id, tenant_id, product_id, variant_id, barcode, quantity, unit_id)
            VALUES (@id, @tenant_id, @product_id, @variant_id, @barcode, @quantity, @unit_id)
            ON CONFLICT (id) DO UPDATE SET
              product_id = EXCLUDED.product_id,
              variant_id = EXCLUDED.variant_id,
              barcode = EXCLUDED.barcode,
              quantity = EXCLUDED.quantity,
              unit_id = EXCLUDED.unit_id,
              deleted_at = NULL
            WHERE pos.product_barcodes.tenant_id = @tenant_id
            RETURNING id, tenant_id, product_id, variant_id, barcode, quantity, unit_id;
            """;

        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", barcodeId);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("product_id", request.ProductId);
        AddNullableUuid(command, "variant_id", request.VariantId);
        command.Parameters.AddWithValue("barcode", request.Barcode.Trim());
        command.Parameters.AddWithValue("quantity", decimal.Parse(request.Quantity, CultureInfo.InvariantCulture));
        AddNullableUuid(command, "unit_id", request.UnitId);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AdminBarcodeResponse(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            NullableGuid(reader, 3),
            reader.GetString(4),
            DecimalString(reader, 5),
            NullableGuid(reader, 6),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public async Task<AdminModifierGroupResponse?> UpsertModifierGroupAsync(Guid tenantId, Guid modifierGroupId, UpdateAdminModifierGroupRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.modifier_groups (id, tenant_id, name, min_selected, max_selected, required)
            VALUES (@id, @tenant_id, @name, @min_selected, @max_selected, @required)
            ON CONFLICT (id) DO UPDATE SET
              name = EXCLUDED.name,
              min_selected = EXCLUDED.min_selected,
              max_selected = EXCLUDED.max_selected,
              required = EXCLUDED.required,
              updated_at = now(),
              deleted_at = NULL
            WHERE pos.modifier_groups.tenant_id = @tenant_id
            RETURNING id, tenant_id, name, min_selected, max_selected, required, updated_at;
            """;

        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", modifierGroupId);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("name", request.Name.Trim());
        command.Parameters.AddWithValue("min_selected", request.MinSelected);
        command.Parameters.AddWithValue("max_selected", request.MaxSelected);
        command.Parameters.AddWithValue("required", request.Required);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        DateTimeOffset updatedAt = reader.GetFieldValue<DateTimeOffset>(6);
        return new AdminModifierGroupResponse(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetBoolean(5),
            updatedAt.ToUnixTimeMilliseconds(),
            updatedAt);
    }

    public async Task<AdminModifierResponse?> UpsertModifierAsync(Guid tenantId, Guid modifierId, UpdateAdminModifierRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.modifiers (
              id, tenant_id, group_id, name, price_delta_cents, linked_product_id, linked_variant_id,
              inventory_behavior, consumption_quantity, consumption_unit_id, replaces_product_id, replaces_variant_id
            )
            VALUES (
              @id, @tenant_id, @group_id, @name, @price_delta_cents, @linked_product_id, @linked_variant_id,
              @inventory_behavior, @consumption_quantity, @consumption_unit_id, @replaces_product_id, @replaces_variant_id
            )
            ON CONFLICT (id) DO UPDATE SET
              group_id = EXCLUDED.group_id,
              name = EXCLUDED.name,
              price_delta_cents = EXCLUDED.price_delta_cents,
              linked_product_id = EXCLUDED.linked_product_id,
              linked_variant_id = EXCLUDED.linked_variant_id,
              inventory_behavior = EXCLUDED.inventory_behavior,
              consumption_quantity = EXCLUDED.consumption_quantity,
              consumption_unit_id = EXCLUDED.consumption_unit_id,
              replaces_product_id = EXCLUDED.replaces_product_id,
              replaces_variant_id = EXCLUDED.replaces_variant_id,
              updated_at = now(),
              deleted_at = NULL
            WHERE pos.modifiers.tenant_id = @tenant_id
            RETURNING id, tenant_id, group_id, name, price_delta_cents, linked_product_id, linked_variant_id,
                      inventory_behavior, consumption_quantity, consumption_unit_id, replaces_product_id, replaces_variant_id, updated_at;
            """;

        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", modifierId);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("group_id", request.GroupId);
        command.Parameters.AddWithValue("name", request.Name.Trim());
        command.Parameters.AddWithValue("price_delta_cents", request.PriceDeltaCents);
        AddNullableUuid(command, "linked_product_id", request.LinkedProductId);
        AddNullableUuid(command, "linked_variant_id", request.LinkedVariantId);
        command.Parameters.AddWithValue("inventory_behavior", request.InventoryBehavior);
        AddNullableNumeric(command, "consumption_quantity", request.ConsumptionQuantity);
        AddNullableUuid(command, "consumption_unit_id", request.ConsumptionUnitId);
        AddNullableUuid(command, "replaces_product_id", request.ReplacesProductId);
        AddNullableUuid(command, "replaces_variant_id", request.ReplacesVariantId);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        DateTimeOffset updatedAt = reader.GetFieldValue<DateTimeOffset>(12);
        return new AdminModifierResponse(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetInt64(4),
            NullableGuid(reader, 5),
            NullableGuid(reader, 6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : DecimalString(reader, 8),
            NullableGuid(reader, 9),
            NullableGuid(reader, 10),
            NullableGuid(reader, 11),
            updatedAt.ToUnixTimeMilliseconds(),
            updatedAt);
    }

    public async Task<AdminRecipeResponse?> UpsertRecipeAsync(Guid tenantId, Guid recipeId, UpdateAdminRecipeRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.recipes (
              id, tenant_id, output_product_id, output_variant_id, version,
              yield_quantity, yield_unit_id, waste_percent, status
            )
            VALUES (
              @id, @tenant_id, @output_product_id, @output_variant_id, @version,
              @yield_quantity, @yield_unit_id, @waste_percent, @status
            )
            ON CONFLICT (id) DO UPDATE SET
              output_product_id = EXCLUDED.output_product_id,
              output_variant_id = EXCLUDED.output_variant_id,
              version = EXCLUDED.version,
              yield_quantity = EXCLUDED.yield_quantity,
              yield_unit_id = EXCLUDED.yield_unit_id,
              waste_percent = EXCLUDED.waste_percent,
              status = EXCLUDED.status,
              updated_at = now(),
              deleted_at = NULL
            WHERE pos.recipes.tenant_id = @tenant_id
            RETURNING id, tenant_id, output_product_id, output_variant_id, version, yield_quantity, yield_unit_id, waste_percent, status, updated_at;
            """;

        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", recipeId);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("output_product_id", request.OutputProductId);
        AddNullableUuid(command, "output_variant_id", request.OutputVariantId);
        command.Parameters.AddWithValue("version", request.Version);
        command.Parameters.AddWithValue("yield_quantity", decimal.Parse(request.YieldQuantity, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("yield_unit_id", request.YieldUnitId);
        command.Parameters.AddWithValue("waste_percent", decimal.Parse(request.WastePercent, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("status", request.Status);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AdminRecipeResponse(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            NullableGuid(reader, 3),
            reader.GetInt32(4),
            DecimalString(reader, 5),
            reader.GetGuid(6),
            DecimalString(reader, 7),
            reader.GetString(8),
            reader.GetFieldValue<DateTimeOffset>(9));
    }

    public async Task<AdminRecipeItemResponse?> UpsertRecipeItemAsync(Guid tenantId, Guid recipeId, Guid recipeItemId, UpdateAdminRecipeItemRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.recipe_items (id, tenant_id, recipe_id, ingredient_product_id, ingredient_variant_id, quantity, unit_id, optional)
            VALUES (@id, @tenant_id, @recipe_id, @ingredient_product_id, @ingredient_variant_id, @quantity, @unit_id, @optional)
            ON CONFLICT (id) DO UPDATE SET
              recipe_id = EXCLUDED.recipe_id,
              ingredient_product_id = EXCLUDED.ingredient_product_id,
              ingredient_variant_id = EXCLUDED.ingredient_variant_id,
              quantity = EXCLUDED.quantity,
              unit_id = EXCLUDED.unit_id,
              optional = EXCLUDED.optional
            WHERE pos.recipe_items.tenant_id = @tenant_id
            RETURNING id, tenant_id, recipe_id, ingredient_product_id, ingredient_variant_id, quantity, unit_id, optional;
            """;

        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", recipeItemId);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("recipe_id", recipeId);
        command.Parameters.AddWithValue("ingredient_product_id", request.IngredientProductId);
        AddNullableUuid(command, "ingredient_variant_id", request.IngredientVariantId);
        command.Parameters.AddWithValue("quantity", decimal.Parse(request.Quantity, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("unit_id", request.UnitId);
        command.Parameters.AddWithValue("optional", request.Optional);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AdminRecipeItemResponse(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            NullableGuid(reader, 4),
            DecimalString(reader, 5),
            reader.GetGuid(6),
            reader.GetBoolean(7),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public async Task<AdminUserAccessResponse?> UpdateUserAccessAsync(Guid tenantId, Guid userId, UpdateAdminUserAccessRequest request, CancellationToken cancellationToken)
    {
        const string updateUserSql = """
            UPDATE pos.users
            SET full_name = @full_name,
                status = @status
            WHERE tenant_id = @tenant_id
              AND id = @user_id
              AND deleted_at IS NULL
            RETURNING id, tenant_id, email::text, full_name, status, updated_at;
            """;

        const string roleSql = """
            SELECT id, code
            FROM pos.roles
            WHERE tenant_id = @tenant_id
              AND code = ANY(@role_codes)
              AND deleted_at IS NULL;
            """;

        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var updateUserCommand = new NpgsqlCommand(updateUserSql, connection, transaction);
        updateUserCommand.Parameters.AddWithValue("tenant_id", tenantId);
        updateUserCommand.Parameters.AddWithValue("user_id", userId);
        updateUserCommand.Parameters.AddWithValue("full_name", request.FullName.Trim());
        updateUserCommand.Parameters.AddWithValue("status", request.Status);

        Guid returnedUserId;
        Guid returnedTenantId;
        string email;
        string fullName;
        string status;
        DateTimeOffset updatedAt;
        await using (NpgsqlDataReader userReader = await updateUserCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (!await userReader.ReadAsync(cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            returnedUserId = userReader.GetGuid(0);
            returnedTenantId = userReader.GetGuid(1);
            email = userReader.GetString(2);
            fullName = userReader.GetString(3);
            status = userReader.GetString(4);
            updatedAt = userReader.GetFieldValue<DateTimeOffset>(5);
        }

        string[] roleCodes = request.RoleCodes.Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        await using var roleCommand = new NpgsqlCommand(roleSql, connection, transaction);
        roleCommand.Parameters.AddWithValue("tenant_id", tenantId);
        roleCommand.Parameters.Add("role_codes", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = roleCodes;

        List<(Guid Id, string Code)> roles = [];
        await using (NpgsqlDataReader roleReader = await roleCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await roleReader.ReadAsync(cancellationToken))
            {
                roles.Add((roleReader.GetGuid(0), roleReader.GetString(1)));
            }
        }

        if (roles.Count != roleCodes.Length)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await ReplaceUserRolesAsync(connection, transaction, tenantId, userId, roles.Select(x => x.Id).ToArray(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AdminUserAccessResponse(
            returnedUserId,
            returnedTenantId,
            email,
            fullName,
            status,
            roles.Select(x => x.Code).OrderBy(x => x).ToArray(),
            updatedAt.ToUnixTimeMilliseconds(),
            updatedAt);
    }

    public async Task<AdminRolePermissionsResponse?> UpdateRolePermissionsAsync(Guid tenantId, Guid roleId, UpdateAdminRolePermissionsRequest request, CancellationToken cancellationToken)
    {
        const string roleExistsSql = """
            SELECT EXISTS (
              SELECT 1
              FROM pos.roles
              WHERE tenant_id = @tenant_id
                AND id = @role_id
                AND deleted_at IS NULL
            );
            """;

        const string permissionsSql = """
            SELECT code
            FROM pos.permissions
            WHERE code = ANY(@permission_codes);
            """;

        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var roleExistsCommand = new NpgsqlCommand(roleExistsSql, connection, transaction);
        roleExistsCommand.Parameters.AddWithValue("tenant_id", tenantId);
        roleExistsCommand.Parameters.AddWithValue("role_id", roleId);
        if (await roleExistsCommand.ExecuteScalarAsync(cancellationToken) is not true)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        string[] permissionCodes = request.PermissionCodes.Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        await using var permissionsCommand = new NpgsqlCommand(permissionsSql, connection, transaction);
        permissionsCommand.Parameters.Add("permission_codes", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = permissionCodes;

        List<string> existingPermissionCodes = [];
        await using (NpgsqlDataReader reader = await permissionsCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                existingPermissionCodes.Add(reader.GetString(0));
            }
        }

        if (existingPermissionCodes.Count != permissionCodes.Length)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await ReplaceRolePermissionsAsync(connection, transaction, tenantId, roleId, existingPermissionCodes.ToArray(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        DateTimeOffset updatedAt = DateTimeOffset.UtcNow;
        return new AdminRolePermissionsResponse(
            tenantId,
            roleId,
            existingPermissionCodes.OrderBy(x => x).ToArray(),
            updatedAt.ToUnixTimeMilliseconds(),
            updatedAt);
    }

    public async Task<AdminSoftDeleteResponse?> SoftDeleteCatalogEntityAsync(Guid tenantId, string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        string normalizedEntityType = entityType.Trim().ToLowerInvariant();
        SoftDeleteStatement? statement = normalizedEntityType switch
        {
            "category" or "categories" => new SoftDeleteStatement(
                "category",
                "tenant.catalog",
                """
                UPDATE pos.categories
                SET deleted_at = now(),
                    version = version + 1
                WHERE tenant_id = @tenant_id
                  AND id = @entity_id
                  AND deleted_at IS NULL
                RETURNING version, deleted_at;
                """),
            "product" or "products" => new SoftDeleteStatement(
                "product",
                "tenant.catalog",
                """
                UPDATE pos.products
                SET deleted_at = now(),
                    version = version + 1
                WHERE tenant_id = @tenant_id
                  AND id = @entity_id
                  AND deleted_at IS NULL
                RETURNING version, deleted_at;
                """),
            "variant" or "variants" => new SoftDeleteStatement(
                "variant",
                "tenant.catalog",
                """
                UPDATE pos.product_variants
                SET deleted_at = now(),
                    version = version + 1,
                    updated_at = now()
                WHERE tenant_id = @tenant_id
                  AND id = @entity_id
                  AND deleted_at IS NULL
                RETURNING version, deleted_at;
                """),
            "barcode" or "barcodes" => new SoftDeleteStatement(
                "barcode",
                "tenant.catalog",
                """
                UPDATE pos.product_barcodes
                SET deleted_at = now()
                WHERE tenant_id = @tenant_id
                  AND id = @entity_id
                  AND deleted_at IS NULL
                RETURNING CAST(EXTRACT(EPOCH FROM deleted_at) * 1000 AS bigint), deleted_at;
                """),
            "price" or "prices" => new SoftDeleteStatement(
                "price",
                "price.updated",
                """
                UPDATE pos.product_prices
                SET deleted_at = now()
                WHERE tenant_id = @tenant_id
                  AND id = @entity_id
                  AND deleted_at IS NULL
                RETURNING CAST(EXTRACT(EPOCH FROM deleted_at) * 1000 AS bigint), deleted_at;
                """),
            "modifier-group" or "modifier-groups" or "modifier_group" or "modifier_groups" => new SoftDeleteStatement(
                "modifier_group",
                "tenant.catalog",
                """
                UPDATE pos.modifier_groups
                SET deleted_at = now(),
                    updated_at = now()
                WHERE tenant_id = @tenant_id
                  AND id = @entity_id
                  AND deleted_at IS NULL
                RETURNING CAST(EXTRACT(EPOCH FROM updated_at) * 1000 AS bigint), deleted_at;
                """),
            "modifier" or "modifiers" => new SoftDeleteStatement(
                "modifier",
                "tenant.catalog",
                """
                UPDATE pos.modifiers
                SET deleted_at = now(),
                    updated_at = now()
                WHERE tenant_id = @tenant_id
                  AND id = @entity_id
                  AND deleted_at IS NULL
                RETURNING CAST(EXTRACT(EPOCH FROM updated_at) * 1000 AS bigint), deleted_at;
                """),
            "recipe" or "recipes" => new SoftDeleteStatement(
                "recipe",
                "tenant.catalog",
                """
                UPDATE pos.recipes
                SET deleted_at = now(),
                    version = version + 1,
                    updated_at = now()
                WHERE tenant_id = @tenant_id
                  AND id = @entity_id
                  AND deleted_at IS NULL
                RETURNING version::bigint, deleted_at;
                """),
            _ => null
        };

        if (statement is null)
        {
            return null;
        }

        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(statement.Sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("entity_id", entityId);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AdminSoftDeleteResponse(
            tenantId,
            statement.EntityType,
            entityId,
            statement.SyncEntityType,
            reader.GetInt64(0),
            reader.GetFieldValue<DateTimeOffset>(1));
    }

    private async Task<NpgsqlConnection> OpenTenantConnectionAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);
        return connection;
    }

    private static async Task ReplaceUserRolesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid userId, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        const string deleteSql = "DELETE FROM pos.user_roles WHERE tenant_id = @tenant_id AND user_id = @user_id;";
        await using (var deleteCommand = new NpgsqlCommand(deleteSql, connection, transaction))
        {
            deleteCommand.Parameters.AddWithValue("tenant_id", tenantId);
            deleteCommand.Parameters.AddWithValue("user_id", userId);
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        const string insertSql = "INSERT INTO pos.user_roles (tenant_id, user_id, role_id) VALUES (@tenant_id, @user_id, @role_id);";
        foreach (Guid roleId in roleIds)
        {
            await using var insertCommand = new NpgsqlCommand(insertSql, connection, transaction);
            insertCommand.Parameters.AddWithValue("tenant_id", tenantId);
            insertCommand.Parameters.AddWithValue("user_id", userId);
            insertCommand.Parameters.AddWithValue("role_id", roleId);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ReplaceRolePermissionsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid roleId, IReadOnlyCollection<string> permissionCodes, CancellationToken cancellationToken)
    {
        const string deleteSql = "DELETE FROM pos.role_permissions WHERE tenant_id = @tenant_id AND role_id = @role_id;";
        await using (var deleteCommand = new NpgsqlCommand(deleteSql, connection, transaction))
        {
            deleteCommand.Parameters.AddWithValue("tenant_id", tenantId);
            deleteCommand.Parameters.AddWithValue("role_id", roleId);
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        const string insertSql = "INSERT INTO pos.role_permissions (tenant_id, role_id, permission_code) VALUES (@tenant_id, @role_id, @permission_code);";
        foreach (string permissionCode in permissionCodes)
        {
            await using var insertCommand = new NpgsqlCommand(insertSql, connection, transaction);
            insertCommand.Parameters.AddWithValue("tenant_id", tenantId);
            insertCommand.Parameters.AddWithValue("role_id", roleId);
            insertCommand.Parameters.AddWithValue("permission_code", permissionCode);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static void AddNullableNumeric(NpgsqlCommand command, string name, string? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.Numeric);
        parameter.Value = string.IsNullOrWhiteSpace(value)
            ? DBNull.Value
            : decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    private static void AddNullableUuid(NpgsqlCommand command, string name, Guid? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.Uuid);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    private static void AddNullableBigint(NpgsqlCommand command, string name, long? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.Bigint);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    private static void AddNullableTimestamp(NpgsqlCommand command, string name, DateTimeOffset? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.TimestampTz);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    private static Guid? NullableGuid(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static DateTimeOffset? NullableDateTimeOffset(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private static string DecimalString(NpgsqlDataReader reader, int ordinal)
    {
        return reader.GetDecimal(ordinal).ToString("0.####", CultureInfo.InvariantCulture);
    }

    private sealed record SoftDeleteStatement(string EntityType, string SyncEntityType, string Sql);
}
