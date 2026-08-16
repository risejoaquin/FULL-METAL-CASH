using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using SolidPOS.PosServer.Application.Inventory;
using SolidPOS.PosServer.Contracts.Inventory;
using SolidPOS.PosServer.Infrastructure.PostgreSql;

namespace SolidPOS.PosServer.Infrastructure.Inventory;

public sealed class PostgreSqlInventoryControlRepository : IInventoryControlRepository
{
    private readonly string _connectionString;

    public PostgreSqlInventoryControlRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    }

    public async Task<InventoryPolicyResponse> GetPolicyAsync(Guid tenantId, Guid? storeId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT tenant_id, store_id, allow_negative_stock, enforce_at_sale, offline_sale_behavior, low_stock_alerts_enabled, updated_at
            FROM pos.inventory_policies
            WHERE tenant_id = @tenant_id
              AND ((@store_id IS NULL AND store_id IS NULL) OR store_id = @store_id)
            ORDER BY store_id NULLS FIRST
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        AddNullableGuid(command, "store_id", storeId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadPolicy(reader);
        }

        return new InventoryPolicyResponse(tenantId, storeId, true, true, "allow_and_reconcile", true, DateTimeOffset.UtcNow);
    }

    public async Task<InventoryPolicyResponse?> UpsertPolicyAsync(Guid tenantId, UpdateInventoryPolicyRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.inventory_policies (
              tenant_id, store_id, allow_negative_stock, enforce_at_sale, offline_sale_behavior, low_stock_alerts_enabled, updated_at
            ) VALUES (
              @tenant_id, @store_id, @allow_negative_stock, @enforce_at_sale, @offline_sale_behavior, @low_stock_alerts_enabled, now()
            )
            ON CONFLICT (tenant_id, store_id_key)
            DO UPDATE SET
              allow_negative_stock = EXCLUDED.allow_negative_stock,
              enforce_at_sale = EXCLUDED.enforce_at_sale,
              offline_sale_behavior = EXCLUDED.offline_sale_behavior,
              low_stock_alerts_enabled = EXCLUDED.low_stock_alerts_enabled,
              updated_at = now()
            RETURNING tenant_id, store_id, allow_negative_stock, enforce_at_sale, offline_sale_behavior, low_stock_alerts_enabled, updated_at;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        AddNullableGuid(command, "store_id", request.StoreId);
        command.Parameters.AddWithValue("allow_negative_stock", request.AllowNegativeStock);
        command.Parameters.AddWithValue("enforce_at_sale", request.EnforceAtSale);
        command.Parameters.AddWithValue("offline_sale_behavior", request.OfflineSaleBehavior);
        command.Parameters.AddWithValue("low_stock_alerts_enabled", request.LowStockAlertsEnabled);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPolicy(reader) : null;
    }

    public async Task<InventoryCountResponse?> CreateCountAsync(Guid tenantId, Guid storeId, Guid? terminalId, CreateInventoryCountRequest request, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken, transaction);

        Guid countId = await FindInventoryCountByLocalIdAsync(connection, transaction, tenantId, request.LocalCountId, cancellationToken) ?? Guid.NewGuid();
        if (await InventoryCountExistsAsync(connection, transaction, tenantId, countId, cancellationToken))
        {
            InventoryCountResponse? existing = await ReadCountAsync(connection, transaction, tenantId, countId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return existing;
        }

        if (!await StoreAndUserValidAsync(connection, transaction, tenantId, storeId, request.CreatedByUserId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await using (var headerCommand = new NpgsqlCommand("""
            INSERT INTO pos.inventory_counts (id, tenant_id, store_id, terminal_id, local_count_id, status, reason, created_by_user_id, occurred_at)
            VALUES (@id, @tenant_id, @store_id, @terminal_id, @local_count_id, 'completed', @reason, @created_by_user_id, @occurred_at);
            """, connection, transaction))
        {
            headerCommand.Parameters.AddWithValue("id", countId);
            headerCommand.Parameters.AddWithValue("tenant_id", tenantId);
            headerCommand.Parameters.AddWithValue("store_id", storeId);
            AddNullableGuid(headerCommand, "terminal_id", terminalId);
            headerCommand.Parameters.AddWithValue("local_count_id", request.LocalCountId);
            headerCommand.Parameters.AddWithValue("reason", request.Reason);
            headerCommand.Parameters.AddWithValue("created_by_user_id", request.CreatedByUserId);
            headerCommand.Parameters.AddWithValue("occurred_at", request.OccurredAt);
            await headerCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (CreateInventoryCountLineRequest line in request.Lines)
        {
            if (!await InventoryLineValidAsync(connection, transaction, tenantId, line.ProductId, line.VariantId, line.UnitId, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            decimal counted = decimal.Parse(line.CountedQuantity, CultureInfo.InvariantCulture);
            decimal previous = await ReadStockQuantityAsync(connection, transaction, tenantId, storeId, line.ProductId, line.VariantId, line.UnitId, cancellationToken);
            decimal delta = counted - previous;
            Guid lineId = Guid.NewGuid();

            await using (var lineCommand = new NpgsqlCommand("""
                INSERT INTO pos.inventory_count_lines (id, tenant_id, count_id, product_id, variant_id, unit_id, previous_quantity, counted_quantity, adjustment_delta)
                VALUES (@id, @tenant_id, @count_id, @product_id, @variant_id, @unit_id, @previous_quantity, @counted_quantity, @adjustment_delta);
                """, connection, transaction))
            {
                lineCommand.Parameters.AddWithValue("id", lineId);
                lineCommand.Parameters.AddWithValue("tenant_id", tenantId);
                lineCommand.Parameters.AddWithValue("count_id", countId);
                lineCommand.Parameters.AddWithValue("product_id", line.ProductId);
                AddNullableGuid(lineCommand, "variant_id", line.VariantId);
                lineCommand.Parameters.AddWithValue("unit_id", line.UnitId);
                lineCommand.Parameters.AddWithValue("previous_quantity", previous);
                lineCommand.Parameters.AddWithValue("counted_quantity", counted);
                lineCommand.Parameters.AddWithValue("adjustment_delta", delta);
                await lineCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            if (delta != 0)
            {
                await InsertLedgerAsync(connection, transaction, tenantId, storeId, terminalId, line.ProductId, line.VariantId, "stock_count", delta, line.UnitId, "inventory_count", countId, request.LocalCountId, request.OccurredAt, new { reason = request.Reason, effect = "stock_count_reconciliation" }, cancellationToken);
            }
        }

        InventoryCountResponse? created = await ReadCountAsync(connection, transaction, tenantId, countId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return created;
    }

    public async Task<IReadOnlyCollection<InventoryCountListItemResponse>> ListCountsAsync(Guid tenantId, InventoryControlFilters filters, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT c.id, c.store_id, c.local_count_id, c.status, c.reason, COUNT(l.id)::int AS line_count, c.occurred_at, c.created_at
            FROM pos.inventory_counts c
            LEFT JOIN pos.inventory_count_lines l ON l.tenant_id = c.tenant_id AND l.count_id = c.id
            WHERE c.tenant_id = @tenant_id
              AND (@store_id IS NULL OR c.store_id = @store_id)
              AND (@from IS NULL OR c.occurred_at >= @from)
              AND (@to IS NULL OR c.occurred_at <= @to)
            GROUP BY c.id
            ORDER BY c.occurred_at DESC, c.created_at DESC
            LIMIT @limit;
            """;
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        AddCommonFilterParameters(command, tenantId, filters);
        List<InventoryCountListItemResponse> items = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new InventoryCountListItemResponse(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4), reader.GetInt32(5), reader.GetFieldValue<DateTimeOffset>(6), reader.GetFieldValue<DateTimeOffset>(7)));
        }
        return items;
    }

    public async Task<InventoryTransferResponse?> CreateTransferAsync(Guid tenantId, Guid? terminalId, CreateInventoryTransferRequest request, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken, transaction);

        Guid transferId = await FindInventoryTransferByLocalIdAsync(connection, transaction, tenantId, request.LocalTransferId, cancellationToken) ?? Guid.NewGuid();
        if (await InventoryTransferExistsAsync(connection, transaction, tenantId, transferId, cancellationToken))
        {
            InventoryTransferResponse? existing = await ReadTransferAsync(connection, transaction, tenantId, transferId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return existing;
        }

        if (!await StoresAndUserValidAsync(connection, transaction, tenantId, request.FromStoreId, request.ToStoreId, request.CreatedByUserId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await using (var headerCommand = new NpgsqlCommand("""
            INSERT INTO pos.inventory_transfers (id, tenant_id, from_store_id, to_store_id, local_transfer_id, status, reason, created_by_user_id, occurred_at)
            VALUES (@id, @tenant_id, @from_store_id, @to_store_id, @local_transfer_id, 'completed', @reason, @created_by_user_id, @occurred_at);
            """, connection, transaction))
        {
            headerCommand.Parameters.AddWithValue("id", transferId);
            headerCommand.Parameters.AddWithValue("tenant_id", tenantId);
            headerCommand.Parameters.AddWithValue("from_store_id", request.FromStoreId);
            headerCommand.Parameters.AddWithValue("to_store_id", request.ToStoreId);
            headerCommand.Parameters.AddWithValue("local_transfer_id", request.LocalTransferId);
            headerCommand.Parameters.AddWithValue("reason", request.Reason);
            headerCommand.Parameters.AddWithValue("created_by_user_id", request.CreatedByUserId);
            headerCommand.Parameters.AddWithValue("occurred_at", request.OccurredAt);
            await headerCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (CreateInventoryTransferLineRequest line in request.Lines)
        {
            if (!await InventoryLineValidAsync(connection, transaction, tenantId, line.ProductId, line.VariantId, line.UnitId, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            decimal quantity = decimal.Parse(line.Quantity, CultureInfo.InvariantCulture);
            await using (var lineCommand = new NpgsqlCommand("""
                INSERT INTO pos.inventory_transfer_lines (tenant_id, transfer_id, product_id, variant_id, unit_id, quantity)
                VALUES (@tenant_id, @transfer_id, @product_id, @variant_id, @unit_id, @quantity);
                """, connection, transaction))
            {
                lineCommand.Parameters.AddWithValue("tenant_id", tenantId);
                lineCommand.Parameters.AddWithValue("transfer_id", transferId);
                lineCommand.Parameters.AddWithValue("product_id", line.ProductId);
                AddNullableGuid(lineCommand, "variant_id", line.VariantId);
                lineCommand.Parameters.AddWithValue("unit_id", line.UnitId);
                lineCommand.Parameters.AddWithValue("quantity", quantity);
                await lineCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await InsertLedgerAsync(connection, transaction, tenantId, request.FromStoreId, terminalId, line.ProductId, line.VariantId, "transfer_out", -quantity, line.UnitId, "inventory_transfer", transferId, request.LocalTransferId, request.OccurredAt, new { reason = request.Reason, effect = "transfer_out" }, cancellationToken);
            await InsertLedgerAsync(connection, transaction, tenantId, request.ToStoreId, terminalId, line.ProductId, line.VariantId, "transfer_in", quantity, line.UnitId, "inventory_transfer", transferId, request.LocalTransferId, request.OccurredAt, new { reason = request.Reason, effect = "transfer_in" }, cancellationToken);
        }

        if (await NegativePolicyBlocksReferenceAsync(connection, transaction, tenantId, request.FromStoreId, "inventory_transfer", transferId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        InventoryTransferResponse? created = await ReadTransferAsync(connection, transaction, tenantId, transferId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return created;
    }

    public async Task<IReadOnlyCollection<InventoryTransferListItemResponse>> ListTransfersAsync(Guid tenantId, InventoryControlFilters filters, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT t.id, t.from_store_id, t.to_store_id, t.local_transfer_id, t.status, t.reason, COUNT(l.id)::int AS line_count, t.occurred_at, t.created_at
            FROM pos.inventory_transfers t
            LEFT JOIN pos.inventory_transfer_lines l ON l.tenant_id = t.tenant_id AND l.transfer_id = t.id
            WHERE t.tenant_id = @tenant_id
              AND (@store_id IS NULL OR t.from_store_id = @store_id OR t.to_store_id = @store_id)
              AND (@from IS NULL OR t.occurred_at >= @from)
              AND (@to IS NULL OR t.occurred_at <= @to)
            GROUP BY t.id
            ORDER BY t.occurred_at DESC, t.created_at DESC
            LIMIT @limit;
            """;
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        AddCommonFilterParameters(command, tenantId, filters);
        List<InventoryTransferListItemResponse> items = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new InventoryTransferListItemResponse(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3), reader.GetString(4), reader.GetString(5), reader.GetInt32(6), reader.GetFieldValue<DateTimeOffset>(7), reader.GetFieldValue<DateTimeOffset>(8)));
        }
        return items;
    }

    public async Task<IReadOnlyCollection<InventoryLowStockItemResponse>> GetLowStockAsync(Guid tenantId, Guid? storeId, int limit, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT s.tenant_id, s.store_id, s.product_id, s.variant_id, p.sku, p.name, s.unit_id, s.quantity_on_hand,
                   t.reorder_point, t.reorder_quantity,
                   CASE WHEN s.quantity_on_hand < 0 THEN 'negative' ELSE 'low' END AS severity
            FROM pos.inventory_stock s
            JOIN pos.products p ON p.tenant_id = s.tenant_id AND p.id = s.product_id
            JOIN pos.inventory_low_stock_thresholds t ON t.tenant_id = s.tenant_id
              AND t.store_id = s.store_id
              AND t.product_id = s.product_id
              AND ((t.variant_id IS NULL AND s.variant_id IS NULL) OR t.variant_id = s.variant_id)
              AND t.unit_id = s.unit_id
            WHERE s.tenant_id = @tenant_id
              AND EXISTS (
                SELECT 1
                FROM pos.inventory_policies pol
                WHERE pol.tenant_id = s.tenant_id
                  AND (pol.store_id IS NULL OR pol.store_id = s.store_id)
                  AND pol.low_stock_alerts_enabled = true
              )
              AND (@store_id IS NULL OR s.store_id = @store_id)
              AND s.quantity_on_hand <= t.reorder_point
            ORDER BY severity DESC, s.quantity_on_hand ASC, p.sku
            LIMIT @limit;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        AddNullableGuid(command, "store_id", storeId);
        command.Parameters.AddWithValue("limit", limit);
        List<InventoryLowStockItemResponse> items = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new InventoryLowStockItemResponse(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.GetString(4), reader.GetString(5), reader.GetGuid(6),
                reader.GetFieldValue<decimal>(7).ToString(CultureInfo.InvariantCulture),
                reader.GetFieldValue<decimal>(8).ToString(CultureInfo.InvariantCulture),
                reader.GetFieldValue<decimal>(9).ToString(CultureInfo.InvariantCulture),
                reader.GetString(10)));
        }
        return items;
    }

    public static async Task<bool> NegativePolicyBlocksReferenceAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid storeId, string referenceType, Guid referenceId, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH policy AS (
              SELECT allow_negative_stock, enforce_at_sale
              FROM pos.inventory_policies
              WHERE tenant_id = @tenant_id
                AND (store_id = @store_id OR store_id IS NULL)
              ORDER BY store_id NULLS LAST
              LIMIT 1
            ), affected AS (
              SELECT DISTINCT store_id, product_id, variant_id, unit_id
              FROM pos.inventory_ledger
              WHERE tenant_id = @tenant_id
                AND store_id = @store_id
                AND reference_type = @reference_type
                AND reference_id = @reference_id
            )
            SELECT EXISTS (
              SELECT 1
              FROM affected a
              JOIN pos.inventory_stock s
                ON s.tenant_id = @tenant_id
               AND s.store_id = a.store_id
               AND s.product_id = a.product_id
               AND ((s.variant_id IS NULL AND a.variant_id IS NULL) OR s.variant_id = a.variant_id)
               AND s.unit_id = a.unit_id
              WHERE COALESCE((SELECT enforce_at_sale FROM policy), true) = true
                AND COALESCE((SELECT allow_negative_stock FROM policy), true) = false
                AND s.quantity_on_hand < 0
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("store_id", storeId);
        command.Parameters.AddWithValue("reference_type", referenceType);
        command.Parameters.AddWithValue("reference_id", referenceId);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is true;
    }

    private static InventoryPolicyResponse ReadPolicy(NpgsqlDataReader reader) =>
        new(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetBoolean(2), reader.GetBoolean(3), reader.GetString(4), reader.GetBoolean(5), reader.GetFieldValue<DateTimeOffset>(6));

    private static async Task<Guid?> FindInventoryCountByLocalIdAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid localId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT id FROM pos.inventory_counts WHERE tenant_id = @tenant_id AND local_count_id = @local_id LIMIT 1;", connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("local_id", localId);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid id ? id : null;
    }

    private static async Task<Guid?> FindInventoryTransferByLocalIdAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid localId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT id FROM pos.inventory_transfers WHERE tenant_id = @tenant_id AND local_transfer_id = @local_id LIMIT 1;", connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("local_id", localId);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid id ? id : null;
    }

    private static async Task<bool> InventoryCountExistsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pos.inventory_counts WHERE tenant_id = @tenant_id AND id = @id);", connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("id", id);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is true;
    }

    private static async Task<bool> InventoryTransferExistsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pos.inventory_transfers WHERE tenant_id = @tenant_id AND id = @id);", connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("id", id);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is true;
    }

    private static async Task<InventoryCountResponse?> ReadCountAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT c.id, c.tenant_id, c.store_id, c.terminal_id, c.local_count_id, c.status, c.reason, c.created_by_user_id, c.occurred_at, c.created_at,
                   l.id, l.product_id, l.variant_id, l.unit_id, l.previous_quantity, l.counted_quantity, l.adjustment_delta
            FROM pos.inventory_counts c
            LEFT JOIN pos.inventory_count_lines l ON l.tenant_id = c.tenant_id AND l.count_id = c.id
            WHERE c.tenant_id = @tenant_id AND c.id = @id
            ORDER BY l.created_at, l.id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("id", id);
        List<InventoryCountLineResponse> lines = [];
        CountHeader? header = null;
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            header ??= new CountHeader(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.IsDBNull(3) ? null : reader.GetGuid(3), reader.GetGuid(4), reader.GetString(5), reader.GetString(6), reader.GetGuid(7), reader.GetFieldValue<DateTimeOffset>(8), reader.GetFieldValue<DateTimeOffset>(9));
            if (!reader.IsDBNull(10))
            {
                lines.Add(new InventoryCountLineResponse(reader.GetGuid(10), reader.GetGuid(11), reader.IsDBNull(12) ? null : reader.GetGuid(12), reader.GetGuid(13), reader.GetFieldValue<decimal>(14).ToString(CultureInfo.InvariantCulture), reader.GetFieldValue<decimal>(15).ToString(CultureInfo.InvariantCulture), reader.GetFieldValue<decimal>(16).ToString(CultureInfo.InvariantCulture)));
            }
        }
        return header is null ? null : new InventoryCountResponse(header.Id, header.TenantId, header.StoreId, header.TerminalId, header.LocalCountId, header.Status, header.Reason, header.CreatedByUserId, header.OccurredAt, header.CreatedAt, lines);
    }

    private static async Task<InventoryTransferResponse?> ReadTransferAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT t.id, t.tenant_id, t.from_store_id, t.to_store_id, t.local_transfer_id, t.status, t.reason, t.created_by_user_id, t.occurred_at, t.created_at,
                   l.product_id, l.variant_id, l.unit_id, l.quantity
            FROM pos.inventory_transfers t
            LEFT JOIN pos.inventory_transfer_lines l ON l.tenant_id = t.tenant_id AND l.transfer_id = t.id
            WHERE t.tenant_id = @tenant_id AND t.id = @id
            ORDER BY l.created_at, l.id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("id", id);
        List<InventoryTransferLineResponse> lines = [];
        TransferHeader? header = null;
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            header ??= new TransferHeader(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3), reader.GetGuid(4), reader.GetString(5), reader.GetString(6), reader.GetGuid(7), reader.GetFieldValue<DateTimeOffset>(8), reader.GetFieldValue<DateTimeOffset>(9));
            if (!reader.IsDBNull(10))
            {
                lines.Add(new InventoryTransferLineResponse(reader.GetGuid(10), reader.IsDBNull(11) ? null : reader.GetGuid(11), reader.GetGuid(12), reader.GetFieldValue<decimal>(13).ToString(CultureInfo.InvariantCulture)));
            }
        }
        return header is null ? null : new InventoryTransferResponse(header.Id, header.TenantId, header.FromStoreId, header.ToStoreId, header.LocalTransferId, header.Status, header.Reason, header.CreatedByUserId, header.OccurredAt, header.CreatedAt, lines);
    }

    private static async Task<bool> StoreAndUserValidAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid storeId, Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
              SELECT 1 FROM pos.stores s
              JOIN pos.users u ON u.tenant_id = s.tenant_id AND u.id = @user_id AND u.status = 'active' AND u.deleted_at IS NULL
              WHERE s.tenant_id = @tenant_id AND s.id = @store_id AND s.status = 'active' AND s.deleted_at IS NULL
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("store_id", storeId);
        command.Parameters.AddWithValue("user_id", userId);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is true;
    }

    private static async Task<bool> StoresAndUserValidAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid fromStoreId, Guid toStoreId, Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
              SELECT 1 FROM pos.stores fs
              JOIN pos.stores ts ON ts.tenant_id = fs.tenant_id AND ts.id = @to_store_id AND ts.status = 'active' AND ts.deleted_at IS NULL
              JOIN pos.users u ON u.tenant_id = fs.tenant_id AND u.id = @user_id AND u.status = 'active' AND u.deleted_at IS NULL
              WHERE fs.tenant_id = @tenant_id AND fs.id = @from_store_id AND fs.status = 'active' AND fs.deleted_at IS NULL
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("from_store_id", fromStoreId);
        command.Parameters.AddWithValue("to_store_id", toStoreId);
        command.Parameters.AddWithValue("user_id", userId);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is true;
    }

    private static async Task<bool> InventoryLineValidAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid productId, Guid? variantId, Guid unitId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
              SELECT 1 FROM pos.products p
              JOIN pos.units u ON u.tenant_id = p.tenant_id AND u.id = @unit_id
              WHERE p.tenant_id = @tenant_id
                AND p.id = @product_id
                AND p.is_stock_tracked = true
                AND p.status = 'active'
                AND p.deleted_at IS NULL
                AND (@variant_id IS NULL OR EXISTS (
                    SELECT 1 FROM pos.product_variants v
                    WHERE v.tenant_id = p.tenant_id AND v.product_id = p.id AND v.id = @variant_id AND v.status = 'active' AND v.deleted_at IS NULL
                ))
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("product_id", productId);
        AddNullableGuid(command, "variant_id", variantId);
        command.Parameters.AddWithValue("unit_id", unitId);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is true;
    }

    private static async Task<decimal> ReadStockQuantityAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid storeId, Guid productId, Guid? variantId, Guid unitId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COALESCE(SUM(quantity_on_hand), 0)
            FROM pos.inventory_stock
            WHERE tenant_id = @tenant_id
              AND store_id = @store_id
              AND product_id = @product_id
              AND ((@variant_id IS NULL AND variant_id IS NULL) OR variant_id = @variant_id)
              AND unit_id = @unit_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("store_id", storeId);
        command.Parameters.AddWithValue("product_id", productId);
        AddNullableGuid(command, "variant_id", variantId);
        command.Parameters.AddWithValue("unit_id", unitId);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is decimal value ? value : 0m;
    }

    private static async Task InsertLedgerAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid storeId, Guid? terminalId, Guid productId, Guid? variantId, string movementType, decimal quantityDelta, Guid unitId, string referenceType, Guid referenceId, Guid sourceEventId, DateTimeOffset occurredAt, object metadata, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.inventory_ledger (
              tenant_id, store_id, terminal_id, product_id, variant_id, movement_type, quantity_delta, unit_id,
              reference_type, reference_id, source_event_id, local_occurred_at, metadata
            ) VALUES (
              @tenant_id, @store_id, @terminal_id, @product_id, @variant_id, @movement_type, @quantity_delta, @unit_id,
              @reference_type, @reference_id, @source_event_id, @local_occurred_at, CAST(@metadata AS jsonb)
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("store_id", storeId);
        AddNullableGuid(command, "terminal_id", terminalId);
        command.Parameters.AddWithValue("product_id", productId);
        AddNullableGuid(command, "variant_id", variantId);
        command.Parameters.AddWithValue("movement_type", movementType);
        command.Parameters.AddWithValue("quantity_delta", quantityDelta);
        command.Parameters.AddWithValue("unit_id", unitId);
        command.Parameters.AddWithValue("reference_type", referenceType);
        command.Parameters.AddWithValue("reference_id", referenceId);
        command.Parameters.AddWithValue("source_event_id", sourceEventId);
        command.Parameters.AddWithValue("local_occurred_at", occurredAt);
        command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(metadata));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddCommonFilterParameters(NpgsqlCommand command, Guid tenantId, InventoryControlFilters filters)
    {
        command.Parameters.AddWithValue("tenant_id", tenantId);
        AddNullableGuid(command, "store_id", filters.StoreId);
        command.Parameters.Add("from", NpgsqlDbType.TimestampTz).Value = filters.From.HasValue ? filters.From.Value : (object)DBNull.Value;
        command.Parameters.Add("to", NpgsqlDbType.TimestampTz).Value = filters.To.HasValue ? filters.To.Value : (object)DBNull.Value;
        command.Parameters.AddWithValue("limit", filters.Limit);
    }

    private static void AddNullableGuid(NpgsqlCommand command, string name, Guid? value)
    {
        command.Parameters.Add(name, NpgsqlDbType.Uuid).Value = value.HasValue ? value.Value : (object)DBNull.Value;
    }

    private sealed record CountHeader(Guid Id, Guid TenantId, Guid StoreId, Guid? TerminalId, Guid LocalCountId, string Status, string Reason, Guid CreatedByUserId, DateTimeOffset OccurredAt, DateTimeOffset CreatedAt);
    private sealed record TransferHeader(Guid Id, Guid TenantId, Guid FromStoreId, Guid ToStoreId, Guid LocalTransferId, string Status, string Reason, Guid CreatedByUserId, DateTimeOffset OccurredAt, DateTimeOffset CreatedAt);
}
