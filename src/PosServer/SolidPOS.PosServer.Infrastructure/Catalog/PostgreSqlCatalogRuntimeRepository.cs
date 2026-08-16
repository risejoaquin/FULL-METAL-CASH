using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;
using SolidPOS.PosServer.Application.Catalog;
using SolidPOS.PosServer.Contracts.Catalog;
using SolidPOS.PosServer.Infrastructure.PostgreSql;

namespace SolidPOS.PosServer.Infrastructure.Catalog;

public sealed class PostgreSqlCatalogRuntimeRepository : ICatalogRuntimeRepository
{
    private readonly string _connectionString;

    public PostgreSqlCatalogRuntimeRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    }

    public async Task<CatalogSnapshotResponse> GetSnapshotAsync(Guid tenantId, DateTimeOffset generatedAt, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        return new CatalogSnapshotResponse(
            tenantId,
            generatedAt,
            await ReadCategoriesAsync(connection, tenantId, cancellationToken),
            await ReadUnitsAsync(connection, tenantId, cancellationToken),
            await ReadProductsAsync(connection, tenantId, cancellationToken),
            await ReadVariantsAsync(connection, tenantId, cancellationToken),
            await ReadBarcodesAsync(connection, tenantId, cancellationToken),
            await ReadPriceListsAsync(connection, tenantId, cancellationToken),
            await ReadPricesAsync(connection, tenantId, cancellationToken),
            await ReadModifierGroupsAsync(connection, tenantId, cancellationToken),
            await ReadModifiersAsync(connection, tenantId, cancellationToken),
            await ReadProductModifierGroupsAsync(connection, tenantId, cancellationToken),
            await ReadRecipesAsync(connection, tenantId, cancellationToken),
            await ReadRecipeItemsAsync(connection, tenantId, cancellationToken));
    }

    private static async Task<IReadOnlyCollection<CatalogCategoryResponse>> ReadCategoriesAsync(NpgsqlConnection connection, Guid tenantId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, parent_id, name, sort_order, status, version, updated_at
            FROM pos.categories
            WHERE tenant_id = @tenant_id
              AND deleted_at IS NULL
            ORDER BY sort_order, name;
            """;

        await using var command = CreateTenantCommand(sql, connection, tenantId);
        List<CatalogCategoryResponse> items = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CatalogCategoryResponse(
                reader.GetGuid(0),
                NullableGuid(reader, 1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetInt64(5),
                reader.GetFieldValue<DateTimeOffset>(6)));
        }

        return items;
    }

    private static async Task<IReadOnlyCollection<CatalogUnitResponse>> ReadUnitsAsync(NpgsqlConnection connection, Guid tenantId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, code, name, symbol, factor_to_base, is_base
            FROM pos.units
            WHERE tenant_id = @tenant_id
            ORDER BY code;
            """;

        await using var command = CreateTenantCommand(sql, connection, tenantId);
        List<CatalogUnitResponse> items = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CatalogUnitResponse(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                DecimalString(reader, 4),
                reader.GetBoolean(5)));
        }

        return items;
    }

    private static async Task<IReadOnlyCollection<CatalogProductResponse>> ReadProductsAsync(NpgsqlConnection connection, Guid tenantId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
              id,
              category_id,
              sku,
              name,
              description,
              product_type,
              sale_unit_id,
              inventory_unit_id,
              is_sellable,
              is_stock_tracked,
              allow_negative_stock,
              tax_mode,
              attributes::text,
              status,
              version,
              updated_at
            FROM pos.products
            WHERE tenant_id = @tenant_id
              AND deleted_at IS NULL
            ORDER BY name;
            """;

        await using var command = CreateTenantCommand(sql, connection, tenantId);
        List<CatalogProductResponse> items = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CatalogProductResponse(
                reader.GetGuid(0),
                NullableGuid(reader, 1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                NullableGuid(reader, 6),
                NullableGuid(reader, 7),
                reader.GetBoolean(8),
                reader.GetBoolean(9),
                reader.GetBoolean(10),
                reader.GetString(11),
                Json(reader.GetString(12)),
                reader.GetString(13),
                reader.GetInt64(14),
                reader.GetFieldValue<DateTimeOffset>(15)));
        }

        return items;
    }

    private static async Task<IReadOnlyCollection<CatalogVariantResponse>> ReadVariantsAsync(NpgsqlConnection connection, Guid tenantId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, product_id, sku, name, attributes::text, status, version, updated_at
            FROM pos.product_variants
            WHERE tenant_id = @tenant_id
              AND deleted_at IS NULL
            ORDER BY sku;
            """;

        await using var command = CreateTenantCommand(sql, connection, tenantId);
        List<CatalogVariantResponse> items = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CatalogVariantResponse(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                Json(reader.GetString(4)),
                reader.GetString(5),
                reader.GetInt64(6),
                reader.GetFieldValue<DateTimeOffset>(7)));
        }

        return items;
    }

    private static async Task<IReadOnlyCollection<CatalogBarcodeResponse>> ReadBarcodesAsync(NpgsqlConnection connection, Guid tenantId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, product_id, variant_id, barcode, quantity, unit_id
            FROM pos.product_barcodes
            WHERE tenant_id = @tenant_id
              AND deleted_at IS NULL
            ORDER BY barcode;
            """;

        await using var command = CreateTenantCommand(sql, connection, tenantId);
        List<CatalogBarcodeResponse> items = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CatalogBarcodeResponse(
                reader.GetGuid(0),
                reader.GetGuid(1),
                NullableGuid(reader, 2),
                reader.GetString(3),
                DecimalString(reader, 4),
                NullableGuid(reader, 5)));
        }

        return items;
    }

    private static async Task<IReadOnlyCollection<CatalogPriceListResponse>> ReadPriceListsAsync(NpgsqlConnection connection, Guid tenantId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, code, name, currency::text, status
            FROM pos.price_lists
            WHERE tenant_id = @tenant_id
              AND deleted_at IS NULL
            ORDER BY code;
            """;

        await using var command = CreateTenantCommand(sql, connection, tenantId);
        List<CatalogPriceListResponse> items = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CatalogPriceListResponse(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)));
        }

        return items;
    }

    private static async Task<IReadOnlyCollection<CatalogPriceResponse>> ReadPricesAsync(NpgsqlConnection connection, Guid tenantId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, price_list_id, product_id, variant_id, price_cents, currency::text, starts_at, ends_at
            FROM pos.product_prices
            WHERE tenant_id = @tenant_id
              AND deleted_at IS NULL
              AND (starts_at IS NULL OR starts_at <= now())
              AND (ends_at IS NULL OR ends_at > now())
            ORDER BY product_id, variant_id;
            """;

        await using var command = CreateTenantCommand(sql, connection, tenantId);
        List<CatalogPriceResponse> items = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CatalogPriceResponse(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                NullableGuid(reader, 3),
                reader.GetInt64(4),
                reader.GetString(5),
                NullableDateTimeOffset(reader, 6),
                NullableDateTimeOffset(reader, 7)));
        }

        return items;
    }

    private static async Task<IReadOnlyCollection<CatalogModifierGroupResponse>> ReadModifierGroupsAsync(NpgsqlConnection connection, Guid tenantId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, name, min_selected, max_selected, required
            FROM pos.modifier_groups
            WHERE tenant_id = @tenant_id
              AND deleted_at IS NULL
            ORDER BY name;
            """;

        await using var command = CreateTenantCommand(sql, connection, tenantId);
        List<CatalogModifierGroupResponse> items = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CatalogModifierGroupResponse(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetBoolean(4)));
        }

        return items;
    }

    private static async Task<IReadOnlyCollection<CatalogModifierResponse>> ReadModifiersAsync(NpgsqlConnection connection, Guid tenantId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, group_id, name, price_delta_cents, linked_product_id, linked_variant_id,
                   inventory_behavior, consumption_quantity, consumption_unit_id, replaces_product_id, replaces_variant_id
            FROM pos.modifiers
            WHERE tenant_id = @tenant_id
              AND deleted_at IS NULL
            ORDER BY name;
            """;

        await using var command = CreateTenantCommand(sql, connection, tenantId);
        List<CatalogModifierResponse> items = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CatalogModifierResponse(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetInt64(3),
                NullableGuid(reader, 4),
                NullableGuid(reader, 5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : DecimalString(reader, 7),
                NullableGuid(reader, 8),
                NullableGuid(reader, 9),
                NullableGuid(reader, 10)));
        }

        return items;
    }

    private static async Task<IReadOnlyCollection<CatalogProductModifierGroupResponse>> ReadProductModifierGroupsAsync(NpgsqlConnection connection, Guid tenantId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT product_id, modifier_group_id
            FROM pos.product_modifier_groups
            WHERE tenant_id = @tenant_id
            ORDER BY product_id, modifier_group_id;
            """;

        await using var command = CreateTenantCommand(sql, connection, tenantId);
        List<CatalogProductModifierGroupResponse> items = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CatalogProductModifierGroupResponse(reader.GetGuid(0), reader.GetGuid(1)));
        }

        return items;
    }

    private static async Task<IReadOnlyCollection<CatalogRecipeResponse>> ReadRecipesAsync(NpgsqlConnection connection, Guid tenantId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, output_product_id, output_variant_id, version, yield_quantity, yield_unit_id, waste_percent, status
            FROM pos.recipes
            WHERE tenant_id = @tenant_id
              AND status = 'active'
              AND deleted_at IS NULL
            ORDER BY output_product_id, version DESC;
            """;

        await using var command = CreateTenantCommand(sql, connection, tenantId);
        List<CatalogRecipeResponse> items = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CatalogRecipeResponse(
                reader.GetGuid(0),
                reader.GetGuid(1),
                NullableGuid(reader, 2),
                reader.GetInt32(3),
                DecimalString(reader, 4),
                reader.GetGuid(5),
                DecimalString(reader, 6),
                reader.GetString(7)));
        }

        return items;
    }

    private static async Task<IReadOnlyCollection<CatalogRecipeItemResponse>> ReadRecipeItemsAsync(NpgsqlConnection connection, Guid tenantId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, recipe_id, ingredient_product_id, ingredient_variant_id, quantity, unit_id, optional
            FROM pos.recipe_items
            WHERE tenant_id = @tenant_id
            ORDER BY recipe_id, created_at;
            """;

        await using var command = CreateTenantCommand(sql, connection, tenantId);
        List<CatalogRecipeItemResponse> items = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CatalogRecipeItemResponse(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                NullableGuid(reader, 3),
                DecimalString(reader, 4),
                reader.GetGuid(5),
                reader.GetBoolean(6)));
        }

        return items;
    }

    private static NpgsqlCommand CreateTenantCommand(string sql, NpgsqlConnection connection, Guid tenantId)
    {
        NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        return command;
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
        return reader.GetFieldValue<decimal>(ordinal).ToString(CultureInfo.InvariantCulture);
    }

    private static JsonElement Json(string value)
    {
        using JsonDocument document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }
}
