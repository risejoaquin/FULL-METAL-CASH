using System.Globalization;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using SolidPOS.PosServer.Application.Reports;
using SolidPOS.PosServer.Contracts.Reports;
using SolidPOS.PosServer.Infrastructure.PostgreSql;

namespace SolidPOS.PosServer.Infrastructure.Reports;

public sealed class PostgreSqlReportsRepository : IReportsRepository
{
    private readonly string _connectionString;

    public PostgreSqlReportsRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    }

    public async Task<bool> StoreExistsAsync(Guid tenantId, Guid storeId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
              SELECT 1
              FROM pos.stores
              WHERE tenant_id = @tenant_id
                AND id = @store_id
            );
            """;

        await using NpgsqlConnection connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("store_id", storeId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<SalesRangeReportResponse> GetSalesRangeAsync(Guid tenantId, ReportDateRangeFilters filters, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
              COUNT(*) FILTER (WHERE status IN ('completed', 'partially_returned', 'returned'))::bigint AS completed_count,
              COUNT(*) FILTER (WHERE status = 'voided')::bigint AS voided_count,
              (
                SELECT COUNT(*)::bigint
                FROM pos.returns r
                JOIN pos.sales rs ON rs.tenant_id = r.tenant_id AND rs.id = r.sale_id
                WHERE r.tenant_id = @tenant_id
                  AND r.status = 'completed'
                  AND r.occurred_at >= @from
                  AND r.occurred_at <= @to
                  AND (@store_id IS NULL OR rs.store_id = @store_id)
              ) AS return_count,
              (
                SELECT COALESCE(SUM(r.refund_cents), 0)::bigint
                FROM pos.returns r
                JOIN pos.sales rs ON rs.tenant_id = r.tenant_id AND rs.id = r.sale_id
                WHERE r.tenant_id = @tenant_id
                  AND r.status = 'completed'
                  AND r.occurred_at >= @from
                  AND r.occurred_at <= @to
                  AND (@store_id IS NULL OR rs.store_id = @store_id)
              ) AS refund_cents,
              COALESCE(SUM(subtotal_cents) FILTER (WHERE status IN ('completed', 'partially_returned', 'returned')), 0)::bigint AS gross_sales_cents,
              COALESCE(SUM(discount_cents) FILTER (WHERE status IN ('completed', 'partially_returned', 'returned')), 0)::bigint AS discount_cents,
              COALESCE(SUM(tax_cents) FILTER (WHERE status IN ('completed', 'partially_returned', 'returned')), 0)::bigint AS tax_cents,
              COALESCE(SUM(tip_cents) FILTER (WHERE status IN ('completed', 'partially_returned', 'returned')), 0)::bigint AS tip_cents,
              COALESCE(SUM(subtotal_cents - discount_cents) FILTER (WHERE status IN ('completed', 'partially_returned', 'returned')), 0)::bigint AS net_sales_cents,
              COALESCE(SUM(total_cents) FILTER (WHERE status IN ('completed', 'partially_returned', 'returned')), 0)::bigint AS total_sales_cents,
              COALESCE(SUM(total_cents) FILTER (WHERE status IN ('completed', 'partially_returned', 'returned')), 0)::bigint - (
                SELECT COALESCE(SUM(r.refund_cents), 0)::bigint
                FROM pos.returns r
                JOIN pos.sales rs ON rs.tenant_id = r.tenant_id AND rs.id = r.sale_id
                WHERE r.tenant_id = @tenant_id
                  AND r.status = 'completed'
                  AND r.occurred_at >= @from
                  AND r.occurred_at <= @to
                  AND (@store_id IS NULL OR rs.store_id = @store_id)
              ) AS net_after_returns_cents,
              COALESCE(ROUND(AVG(total_cents) FILTER (WHERE status IN ('completed', 'partially_returned', 'returned'))), 0)::bigint AS average_ticket_cents
            FROM pos.sales
            WHERE tenant_id = @tenant_id
              AND deleted_at IS NULL
              AND occurred_at >= @from
              AND occurred_at <= @to
              AND (@store_id IS NULL OR store_id = @store_id);
            """;

        await using NpgsqlConnection connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        AddCommonDateRangeParameters(command, tenantId, filters);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return new SalesRangeReportResponse(
            tenantId,
            filters.StoreId,
            filters.From!.Value,
            filters.To!.Value,
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetInt64(7),
            reader.GetInt64(8),
            reader.GetInt64(9),
            reader.GetInt64(10),
            reader.GetInt64(11));
    }

    public async Task<IReadOnlyCollection<PaymentMethodReportItemResponse>> GetSalesByPaymentMethodAsync(Guid tenantId, ReportDateRangeFilters filters, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH sale_payment_method AS (
              SELECT
                s.id AS sale_id,
                pm.code,
                pm.name,
                pm.method_type,
                COUNT(p.id)::bigint AS payment_count,
                SUM(p.amount_cents)::numeric AS tendered_cents,
                s.change_cents
              FROM pos.payments p
              JOIN pos.sales s
                ON s.tenant_id = p.tenant_id
               AND s.id = p.sale_id
              JOIN pos.payment_methods pm
                ON pm.tenant_id = p.tenant_id
               AND pm.id = p.payment_method_id
              WHERE p.tenant_id = @tenant_id
                AND s.deleted_at IS NULL
                AND s.status IN ('completed', 'partially_returned', 'returned')
                AND p.status = 'approved'
                AND s.occurred_at >= @from
                AND s.occurred_at <= @to
                AND (@store_id IS NULL OR s.store_id = @store_id)
              GROUP BY s.id, pm.code, pm.name, pm.method_type, s.change_cents
            ), allocated AS (
              SELECT
                sale_id, code, name, method_type, payment_count, tendered_cents, change_cents,
                SUM(tendered_cents) FILTER (WHERE method_type = 'cash')
                  OVER (PARTITION BY sale_id) AS sale_cash_tendered_cents
              FROM sale_payment_method
            ), effective AS (
              SELECT
                sale_id, code, name, method_type, payment_count,
                CASE
                  WHEN method_type = 'cash' AND COALESCE(sale_cash_tendered_cents, 0) > 0
                  THEN tendered_cents - (change_cents * tendered_cents / sale_cash_tendered_cents)
                  ELSE tendered_cents
                END AS effective_cents
              FROM allocated
            )
            SELECT
              code,
              name,
              method_type,
              SUM(payment_count)::bigint AS payment_count,
              COUNT(DISTINCT sale_id)::bigint AS sale_count,
              COALESCE(ROUND(SUM(effective_cents)), 0)::bigint AS total_cents
            FROM effective
            GROUP BY code, name, method_type
            ORDER BY total_cents DESC, code ASC;
            """;

        await using NpgsqlConnection connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        AddCommonDateRangeParameters(command, tenantId, filters);

        List<PaymentMethodReportItemResponse> items = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new PaymentMethodReportItemResponse(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetInt64(5)));
        }

        return items;
    }

    public async Task<IReadOnlyCollection<CashShiftReportItemResponse>> GetCashShiftsAsync(Guid tenantId, ReportListFilters filters, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
              id,
              store_id,
              terminal_id,
              opened_by_user_id,
              closed_by_user_id,
              status,
              opening_amount_cents,
              expected_cash_cents,
              counted_cash_cents,
              difference_cents,
              opened_at,
              closed_at
            FROM pos.cash_shifts
            WHERE tenant_id = @tenant_id
              AND opened_at >= @from
              AND opened_at <= @to
              AND (@store_id IS NULL OR store_id = @store_id)
            ORDER BY opened_at DESC
            LIMIT @limit;
            """;

        await using NpgsqlConnection connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        AddCommonListParameters(command, tenantId, filters);

        List<CashShiftReportItemResponse> items = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CashShiftReportItemResponse(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                NullableGuid(reader, 4),
                reader.GetString(5),
                reader.GetInt64(6),
                reader.GetInt64(7),
                NullableInt64(reader, 8),
                NullableInt64(reader, 9),
                reader.GetFieldValue<DateTimeOffset>(10),
                NullableDateTimeOffset(reader, 11)));
        }

        return items;
    }

    public async Task<IReadOnlyCollection<TopProductReportItemResponse>> GetTopProductsAsync(Guid tenantId, ReportListFilters filters, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
              sl.product_id,
              sl.variant_id,
              COALESCE(v.sku, p.sku) AS sku,
              COALESCE(v.name, p.name) AS name,
              SUM(sl.quantity) AS quantity_sold,
              COUNT(sl.id)::bigint AS line_count,
              COALESCE(SUM(sl.total_cents), 0)::bigint AS total_cents
            FROM pos.sale_lines sl
            JOIN pos.sales s
              ON s.tenant_id = sl.tenant_id
             AND s.id = sl.sale_id
            JOIN pos.products p
              ON p.tenant_id = sl.tenant_id
             AND p.id = sl.product_id
            LEFT JOIN pos.product_variants v
              ON v.tenant_id = sl.tenant_id
             AND v.id = sl.variant_id
            WHERE sl.tenant_id = @tenant_id
              AND s.deleted_at IS NULL
              AND s.status IN ('completed', 'partially_returned', 'returned')
              AND s.occurred_at >= @from
              AND s.occurred_at <= @to
              AND (@store_id IS NULL OR s.store_id = @store_id)
            GROUP BY sl.product_id, sl.variant_id, COALESCE(v.sku, p.sku), COALESCE(v.name, p.name)
            ORDER BY total_cents DESC, quantity_sold DESC
            LIMIT @limit;
            """;

        await using NpgsqlConnection connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        AddCommonListParameters(command, tenantId, filters);

        List<TopProductReportItemResponse> items = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new TopProductReportItemResponse(
                reader.GetGuid(0),
                NullableGuid(reader, 1),
                reader.GetString(2),
                reader.GetString(3),
                DecimalString(reader, 4),
                reader.GetInt64(5),
                reader.GetInt64(6)));
        }

        return items;
    }

    public async Task<IReadOnlyCollection<NegativeInventoryItemResponse>> GetNegativeInventoryAsync(Guid tenantId, Guid? storeId, int limit, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
              stock.store_id,
              stock.product_id,
              stock.variant_id,
              COALESCE(v.sku, p.sku) AS sku,
              COALESCE(v.name, p.name) AS name,
              stock.unit_id,
              u.code,
              stock.quantity_on_hand
            FROM pos.inventory_stock stock
            JOIN pos.products p
              ON p.tenant_id = stock.tenant_id
             AND p.id = stock.product_id
            LEFT JOIN pos.product_variants v
              ON v.tenant_id = stock.tenant_id
             AND v.id = stock.variant_id
            JOIN pos.units u
              ON u.tenant_id = stock.tenant_id
             AND u.id = stock.unit_id
            WHERE stock.tenant_id = @tenant_id
              AND stock.quantity_on_hand < 0
              AND (@store_id IS NULL OR stock.store_id = @store_id)
            ORDER BY stock.quantity_on_hand ASC, sku ASC
            LIMIT @limit;
            """;

        await using NpgsqlConnection connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        AddNullableUuid(command, "store_id", storeId);
        command.Parameters.AddWithValue("limit", limit);

        List<NegativeInventoryItemResponse> items = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new NegativeInventoryItemResponse(
                reader.GetGuid(0),
                reader.GetGuid(1),
                NullableGuid(reader, 2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetGuid(5),
                reader.GetString(6),
                DecimalString(reader, 7)));
        }

        return items;
    }

    public async Task<IReadOnlyCollection<InventoryMovementReportItemResponse>> GetInventoryMovementsAsync(Guid tenantId, ReportListFilters filters, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
              il.id,
              il.store_id,
              il.terminal_id,
              il.product_id,
              il.variant_id,
              COALESCE(v.sku, p.sku) AS sku,
              COALESCE(v.name, p.name) AS name,
              il.movement_type,
              il.quantity_delta,
              il.unit_id,
              u.code,
              il.reference_type,
              il.reference_id,
              NULLIF(il.metadata->>'effect', '') AS effect,
              NULLIF(il.metadata->>'recipeId', '')::uuid AS recipe_id,
              NULLIF(il.metadata->>'modifierId', '')::uuid AS modifier_id,
              NULLIF(il.metadata->>'modifierBehavior', '') AS modifier_behavior,
              il.local_occurred_at,
              il.created_at
            FROM pos.inventory_ledger il
            JOIN pos.products p
              ON p.tenant_id = il.tenant_id
             AND p.id = il.product_id
            LEFT JOIN pos.product_variants v
              ON v.tenant_id = il.tenant_id
             AND v.id = il.variant_id
            JOIN pos.units u
              ON u.tenant_id = il.tenant_id
             AND u.id = il.unit_id
            WHERE il.tenant_id = @tenant_id
              AND il.local_occurred_at >= @from
              AND il.local_occurred_at <= @to
              AND (@store_id IS NULL OR il.store_id = @store_id)
            ORDER BY il.local_occurred_at DESC, il.id DESC
            LIMIT @limit;
            """;

        await using NpgsqlConnection connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        AddCommonListParameters(command, tenantId, filters);

        List<InventoryMovementReportItemResponse> items = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new InventoryMovementReportItemResponse(
                reader.GetGuid(0),
                reader.GetGuid(1),
                NullableGuid(reader, 2),
                reader.GetGuid(3),
                NullableGuid(reader, 4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                DecimalString(reader, 8),
                reader.GetGuid(9),
                reader.GetString(10),
                NullableString(reader, 11),
                NullableGuid(reader, 12),
                NullableString(reader, 13),
                NullableGuid(reader, 14),
                NullableGuid(reader, 15),
                NullableString(reader, 16),
                reader.GetFieldValue<DateTimeOffset>(17),
                reader.GetFieldValue<DateTimeOffset>(18)));
        }

        return items;
    }

    public async Task<long> GetNegativeInventoryCountAsync(Guid tenantId, Guid? storeId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*)::bigint
            FROM pos.inventory_stock
            WHERE tenant_id = @tenant_id
              AND quantity_on_hand < 0
              AND (@store_id IS NULL OR store_id = @store_id);
            """;

        await using NpgsqlConnection connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        AddNullableUuid(command, "store_id", storeId);
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    public async Task<DashboardInventorySummaryResponse> GetDashboardInventorySummaryAsync(Guid tenantId, ReportDateRangeFilters filters, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
              (SELECT COUNT(*)::bigint
               FROM pos.inventory_stock stock
               WHERE stock.tenant_id = @tenant_id
                 AND stock.quantity_on_hand < 0
                 AND (@store_id IS NULL OR stock.store_id = @store_id)) AS negative_stock_count,
              COUNT(*) FILTER (WHERE il.movement_type = 'sale_recipe_component')::bigint AS sale_recipe_movement_count,
              COUNT(*) FILTER (WHERE il.metadata->>'effect' = 'recipe_component')::bigint AS recipe_component_count,
              COUNT(*) FILTER (WHERE il.metadata->>'effect' = 'modifier_add_component')::bigint AS modifier_add_count,
              COUNT(*) FILTER (WHERE il.metadata->>'effect' = 'modifier_substitute_component')::bigint AS modifier_substitute_count
            FROM pos.inventory_ledger il
            WHERE il.tenant_id = @tenant_id
              AND il.local_occurred_at >= @from
              AND il.local_occurred_at <= @to
              AND (@store_id IS NULL OR il.store_id = @store_id);
            """;

        await using NpgsqlConnection connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        AddCommonDateRangeParameters(command, tenantId, filters);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new DashboardInventorySummaryResponse(
            reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4));
    }

    public async Task<IReadOnlyCollection<DashboardSalesTrendPointResponse>> GetSalesTrendAsync(Guid tenantId, ReportDateRangeFilters filters, string trendBucket, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
              (date_trunc(@bucket, occurred_at AT TIME ZONE 'UTC') AT TIME ZONE 'UTC') AS bucket_start,
              COUNT(*)::bigint AS completed_sales_count,
              COALESCE(SUM(subtotal_cents - discount_cents), 0)::bigint AS net_sales_cents,
              COALESCE(SUM(tax_cents), 0)::bigint AS tax_cents,
              COALESCE(SUM(tip_cents), 0)::bigint AS tip_cents,
              COALESCE(SUM(total_cents), 0)::bigint AS total_sales_cents
            FROM pos.sales
            WHERE tenant_id = @tenant_id
              AND deleted_at IS NULL
              AND status IN ('completed', 'partially_returned', 'returned')
              AND occurred_at >= @from
              AND occurred_at <= @to
              AND (@store_id IS NULL OR store_id = @store_id)
            GROUP BY bucket_start
            ORDER BY bucket_start;
            """;

        await using NpgsqlConnection connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        AddCommonDateRangeParameters(command, tenantId, filters);
        command.Parameters.AddWithValue("bucket", trendBucket);

        List<DashboardSalesTrendPointResponse> items = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new DashboardSalesTrendPointResponse(
                reader.GetFieldValue<DateTimeOffset>(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetInt64(5)));
        }

        return items;
    }

    private async Task<NpgsqlConnection> OpenTenantConnectionAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);
        return connection;
    }

    private static void AddCommonDateRangeParameters(NpgsqlCommand command, Guid tenantId, ReportDateRangeFilters filters)
    {
        command.Parameters.AddWithValue("tenant_id", tenantId);
        AddNullableUuid(command, "store_id", filters.StoreId);
        command.Parameters.AddWithValue("from", filters.From!.Value);
        command.Parameters.AddWithValue("to", filters.To!.Value);
    }

    private static void AddCommonListParameters(NpgsqlCommand command, Guid tenantId, ReportListFilters filters)
    {
        command.Parameters.AddWithValue("tenant_id", tenantId);
        AddNullableUuid(command, "store_id", filters.StoreId);
        command.Parameters.AddWithValue("from", filters.From!.Value);
        command.Parameters.AddWithValue("to", filters.To!.Value);
        command.Parameters.AddWithValue("limit", filters.Limit);
    }

    private static void AddNullableUuid(NpgsqlCommand command, string name, Guid? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.Uuid);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    private static Guid? NullableGuid(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static long? NullableInt64(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static string? NullableString(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset? NullableDateTimeOffset(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private static string DecimalString(NpgsqlDataReader reader, int ordinal)
    {
        return reader.GetDecimal(ordinal).ToString("0.####", CultureInfo.InvariantCulture);
    }
}
