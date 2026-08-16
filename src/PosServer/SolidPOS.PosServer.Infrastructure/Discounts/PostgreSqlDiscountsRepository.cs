using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using SolidPOS.PosServer.Application.Discounts;
using SolidPOS.PosServer.Contracts.Discounts;
using SolidPOS.PosServer.Infrastructure.PostgreSql;

namespace SolidPOS.PosServer.Infrastructure.Discounts;

public sealed class PostgreSqlDiscountsRepository : IDiscountsRepository
{
    private readonly string _connectionString;

    public PostgreSqlDiscountsRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    }

    public async Task<IReadOnlyCollection<DiscountListItemResponse>> ListAsync(Guid tenantId, DiscountListFilters filters, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, code, name, discount_type, value, store_id, category_id, product_id,
                   starts_at, ends_at, status, updated_at
            FROM pos.discounts
            WHERE tenant_id = @tenant_id
              AND deleted_at IS NULL
              AND (@status IS NULL OR status = @status)
              AND (@store_id IS NULL OR store_id IS NULL OR store_id = @store_id)
              AND (@category_id IS NULL OR category_id IS NULL OR category_id = @category_id)
              AND (@product_id IS NULL OR product_id IS NULL OR product_id = @product_id)
              AND (@search IS NULL OR name ILIKE '%' || @search || '%' OR code ILIKE '%' || @search || '%')
            ORDER BY updated_at DESC, name ASC
            LIMIT @limit;
            """;

        await using NpgsqlConnection connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        AddListParameters(command, tenantId, filters);

        List<DiscountListItemResponse> results = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new DiscountListItemResponse(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDecimal(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7),
                reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
                reader.GetString(10),
                reader.GetFieldValue<DateTimeOffset>(11)));
        }

        return results;
    }

    public async Task<DiscountResponse?> CreateAsync(Guid tenantId, CreateDiscountRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.discounts (
              tenant_id, code, name, discount_type, value, store_id, category_id, product_id,
              starts_at, ends_at, status, rules
            )
            VALUES (
              @tenant_id, @code, @name, @discount_type, @value, @store_id, @category_id, @product_id,
              @starts_at, @ends_at, @status, '{}'::jsonb
            )
            RETURNING id;
            """;

        await using NpgsqlConnection connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        AddNullableString(command, "code", request.Code);
        command.Parameters.AddWithValue("name", request.Name);
        command.Parameters.AddWithValue("discount_type", request.DiscountType);
        command.Parameters.AddWithValue("value", request.Value);
        AddNullableGuid(command, "store_id", request.StoreId);
        AddNullableGuid(command, "category_id", request.CategoryId);
        AddNullableGuid(command, "product_id", request.ProductId);
        AddNullableDateTimeOffset(command, "starts_at", request.StartsAt);
        AddNullableDateTimeOffset(command, "ends_at", request.EndsAt);
        command.Parameters.AddWithValue("status", request.Status ?? "active");

        try
        {
            Guid id = (Guid)(await command.ExecuteScalarAsync(cancellationToken) ?? Guid.Empty);
            return await GetByIdAsync(tenantId, id, cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return null;
        }
    }

    public async Task<DiscountResponse?> UpdateAsync(Guid tenantId, Guid discountId, UpdateDiscountRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pos.discounts
            SET code = COALESCE(@code, code),
                name = COALESCE(@name, name),
                discount_type = COALESCE(@discount_type, discount_type),
                value = COALESCE(@value, value),
                store_id = @store_id,
                category_id = @category_id,
                product_id = @product_id,
                starts_at = @starts_at,
                ends_at = @ends_at,
                status = COALESCE(@status, status),
                updated_at = now()
            WHERE tenant_id = @tenant_id
              AND id = @id
              AND deleted_at IS NULL;
            """;

        await using NpgsqlConnection connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("id", discountId);
        AddNullableString(command, "code", request.Code);
        AddNullableString(command, "name", request.Name);
        AddNullableString(command, "discount_type", request.DiscountType);
        AddNullableDecimal(command, "value", request.Value);
        AddNullableGuid(command, "store_id", request.StoreId);
        AddNullableGuid(command, "category_id", request.CategoryId);
        AddNullableGuid(command, "product_id", request.ProductId);
        AddNullableDateTimeOffset(command, "starts_at", request.StartsAt);
        AddNullableDateTimeOffset(command, "ends_at", request.EndsAt);
        AddNullableString(command, "status", request.Status);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            return await GetByIdAsync(tenantId, discountId, cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return null;
        }
    }

    public async Task<DiscountResponse?> GetByIdAsync(Guid tenantId, Guid discountId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, tenant_id, code, name, discount_type, value, store_id, category_id, product_id,
                   starts_at, ends_at, status, created_at, updated_at
            FROM pos.discounts
            WHERE tenant_id = @tenant_id
              AND id = @id
              AND deleted_at IS NULL;
            """;

        await using NpgsqlConnection connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("id", discountId);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadDiscount(reader) : null;
    }

    public async Task<bool> StoreExistsAsync(Guid tenantId, Guid storeId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT EXISTS (SELECT 1 FROM pos.stores WHERE tenant_id = @tenant_id AND id = @id AND deleted_at IS NULL);";
        return await ExistsAsync(tenantId, storeId, sql, cancellationToken);
    }

    public async Task<bool> CategoryExistsAsync(Guid tenantId, Guid categoryId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT EXISTS (SELECT 1 FROM pos.categories WHERE tenant_id = @tenant_id AND id = @id AND deleted_at IS NULL);";
        return await ExistsAsync(tenantId, categoryId, sql, cancellationToken);
    }

    public async Task<bool> ProductExistsAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT EXISTS (SELECT 1 FROM pos.products WHERE tenant_id = @tenant_id AND id = @id AND deleted_at IS NULL);";
        return await ExistsAsync(tenantId, productId, sql, cancellationToken);
    }

    private async Task<bool> ExistsAsync(Guid tenantId, Guid id, string sql, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("id", id);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private async Task<NpgsqlConnection> OpenTenantConnectionAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);
        return connection;
    }

    private static DiscountResponse ReadDiscount(NpgsqlDataReader reader)
    {
        return new DiscountResponse(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetDecimal(5),
            reader.IsDBNull(6) ? null : reader.GetGuid(6),
            reader.IsDBNull(7) ? null : reader.GetGuid(7),
            reader.IsDBNull(8) ? null : reader.GetGuid(8),
            reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
            reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
            reader.GetString(11),
            reader.GetFieldValue<DateTimeOffset>(12),
            reader.GetFieldValue<DateTimeOffset>(13));
    }

    private static void AddListParameters(NpgsqlCommand command, Guid tenantId, DiscountListFilters filters)
    {
        command.Parameters.AddWithValue("tenant_id", tenantId);
        AddNullableString(command, "search", filters.Search);
        AddNullableString(command, "status", filters.Status);
        AddNullableGuid(command, "store_id", filters.StoreId);
        AddNullableGuid(command, "category_id", filters.CategoryId);
        AddNullableGuid(command, "product_id", filters.ProductId);
        command.Parameters.AddWithValue("limit", filters.Limit);
    }

    private static void AddNullableGuid(NpgsqlCommand command, string name, Guid? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.Uuid);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    private static void AddNullableString(NpgsqlCommand command, string name, string? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.Text);
        parameter.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }

    private static void AddNullableDecimal(NpgsqlCommand command, string name, decimal? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.Numeric);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    private static void AddNullableDateTimeOffset(NpgsqlCommand command, string name, DateTimeOffset? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.TimestampTz);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }
}
