using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using SolidPOS.PosServer.Application.Sales;
using SolidPOS.PosServer.Contracts.Receipts;
using SolidPOS.PosServer.Contracts.Sales;
using SolidPOS.PosServer.Infrastructure.PostgreSql;
using SolidPOS.PosServer.Infrastructure.Inventory;

namespace SolidPOS.PosServer.Infrastructure.Sales;

public sealed class PostgreSqlSalesRepository : ISalesRepository
{
    private readonly string _connectionString;

    public PostgreSqlSalesRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    }

    public async Task<SaleResponse?> CreateAsync(
        Guid tenantId,
        Guid storeId,
        Guid terminalId,
        CreateSaleRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken, transaction);

        SaleResponse? existingSale = await ReadSaleByLocalIdAsync(connection, transaction, tenantId, terminalId, request.LocalSaleId, cancellationToken);
        if (existingSale is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return existingSale;
        }

        Guid? cashShiftId = await ReadOpenCashShiftIdAsync(connection, transaction, tenantId, storeId, terminalId, request.CashierUserId, request.CustomerId, cancellationToken);
        if (!cashShiftId.HasValue)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        List<PreparedSaleLine> preparedLines = [];
        int lineNumber = 1;
        foreach (CreateSaleLineRequest line in request.Lines)
        {
            PreparedProduct? product = await ReadProductAsync(connection, transaction, tenantId, line.ProductId, line.VariantId, cancellationToken);
            if (product is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            IReadOnlyCollection<PreparedModifier> modifiers = await ReadModifiersAsync(connection, transaction, tenantId, line.ProductId, line.ModifierIds, cancellationToken);
            if ((line.ModifierIds?.Count ?? 0) != modifiers.Count)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            decimal quantity = decimal.Parse(line.Quantity, CultureInfo.InvariantCulture);
            long modifierDeltaCents = modifiers.Sum(x => x.PriceDeltaCents);
            long unitPriceCents = product.PriceCents + modifierDeltaCents;
            long grossCents = ToCents(quantity * unitPriceCents);
            if (line.DiscountCents > grossCents)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            if (line.DiscountCents > 0)
            {
                if (!line.DiscountId.HasValue)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return null;
                }

                PreparedDiscount? discount = await ReadActiveDiscountAsync(
                    connection,
                    transaction,
                    tenantId,
                    line.DiscountId.Value,
                    storeId,
                    product.Id,
                    product.CategoryId,
                    request.OccurredAt,
                    cancellationToken);

                if (discount is null || CalculateDiscountCents(discount, grossCents) != line.DiscountCents)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return null;
                }
            }

            long lineTotalCents = grossCents - line.DiscountCents;
            preparedLines.Add(new PreparedSaleLine(
                lineNumber++,
                product,
                quantity,
                unitPriceCents,
                line.DiscountCents,
                line.DiscountId,
                0,
                lineTotalCents,
                line.PreparationNote,
                modifiers));
        }

        List<PreparedPayment> preparedPayments = [];
        foreach (CreateSalePaymentRequest payment in request.Payments)
        {
            PreparedPaymentMethod? method = await ReadPaymentMethodAsync(connection, transaction, tenantId, payment.MethodCode, cancellationToken);
            if (method is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            preparedPayments.Add(new PreparedPayment(method, payment.LocalPaymentId, payment.AmountCents, payment.Reference));
        }

        long subtotalCents = preparedLines.Sum(x => x.UnitPriceGrossCents);
        long discountCents = preparedLines.Sum(x => x.DiscountCents);
        long taxCents = preparedLines.Sum(x => x.TaxCents);
        long totalCents = preparedLines.Sum(x => x.TotalCents) + request.TipCents;
        long paidCents = preparedPayments.Sum(x => x.AmountCents);
        if (paidCents < totalCents)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        long changeCents = paidCents - totalCents;
        long cashTenderedCents = preparedPayments
            .Where(x => x.Method.MethodType == "cash")
            .Sum(x => x.AmountCents);
        if (changeCents > cashTenderedCents)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        Guid saleId = await InsertSaleAsync(
            connection,
            transaction,
            tenantId,
            storeId,
            terminalId,
            cashShiftId.Value,
            request,
            subtotalCents,
            discountCents,
            taxCents,
            totalCents,
            paidCents,
            changeCents,
            cancellationToken);

        foreach (PreparedSaleLine line in preparedLines)
        {
            await InsertSaleLineAsync(connection, transaction, tenantId, saleId, line, cancellationToken);
        }

        foreach (PreparedPayment payment in preparedPayments)
        {
            await InsertPaymentAsync(connection, transaction, tenantId, saleId, payment, cancellationToken);
        }

        foreach (PreparedSaleLine line in preparedLines)
        {
            await InsertInventoryEffectsAsync(
                connection,
                transaction,
                tenantId,
                storeId,
                terminalId,
                saleId,
                request.LocalSaleId,
                request.OccurredAt,
                line,
                cancellationToken);
        }

        if (await PostgreSqlInventoryControlRepository.NegativePolicyBlocksReferenceAsync(connection, transaction, tenantId, storeId, "sale", saleId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (preparedPayments.Any(x => x.Method.MethodType == "cash"))
        {
            await IncreaseExpectedCashAsync(
                connection,
                transaction,
                tenantId,
                cashShiftId.Value,
                preparedPayments.Where(x => x.Method.MethodType == "cash").Sum(x => x.AmountCents) - changeCents,
                cancellationToken);
        }

        SaleResponse? createdSale = await ReadSaleByIdAsync(connection, transaction, tenantId, saleId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return createdSale;
    }

    public async Task<SaleResponse?> VoidAsync(
        Guid tenantId,
        Guid storeId,
        Guid terminalId,
        Guid saleId,
        VoidSaleRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken, transaction);

        VoidableSale? sale = await ReadVoidableSaleAsync(connection, transaction, tenantId, storeId, terminalId, saleId, cancellationToken);
        if (sale is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (sale.Status == "voided")
        {
            SaleResponse? alreadyVoided = await ReadSaleByIdAsync(connection, transaction, tenantId, saleId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return alreadyVoided;
        }

        if (sale.Status != "completed")
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (!await VoidRuntimeContextIsValidAsync(connection, transaction, tenantId, sale.CashShiftId, request.VoidedByUserId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await InsertVoidInventoryCompensationAsync(connection, transaction, tenantId, saleId, request, cancellationToken);
        await MarkSaleVoidedAsync(connection, transaction, tenantId, saleId, request, cancellationToken);
        await MarkPaymentsVoidedAsync(connection, transaction, tenantId, saleId, cancellationToken);

        long cashImpactCents = await ReadSaleCashImpactAsync(connection, transaction, tenantId, saleId, cancellationToken);
        if (cashImpactCents > 0)
        {
            await IncreaseExpectedCashAsync(connection, transaction, tenantId, sale.CashShiftId, -cashImpactCents, cancellationToken);
        }

        SaleResponse? voidedSale = await ReadSaleByIdAsync(connection, transaction, tenantId, saleId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return voidedSale;
    }

    public async Task<SaleResponse?> VoidByLocalSaleIdAsync(
        Guid tenantId,
        Guid storeId,
        Guid terminalId,
        Guid localSaleId,
        VoidSaleRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken, transaction);

        SaleResponse? existingSale = await ReadSaleByLocalIdAsync(connection, transaction, tenantId, terminalId, localSaleId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return existingSale is null
            ? null
            : await VoidAsync(tenantId, storeId, terminalId, existingSale.Id, request, cancellationToken);
    }



    public async Task<SaleDetailResponse?> GetByIdAsync(
        Guid tenantId,
        Guid saleId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken, transaction);

        SaleResponse? sale = await ReadSaleByIdAsync(connection, transaction, tenantId, saleId, cancellationToken);
        if (sale is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        IReadOnlyCollection<SaleDetailLineResponse> detailLines = await ReadSaleDetailLinesAsync(connection, transaction, tenantId, saleId, cancellationToken);
        IReadOnlyCollection<SaleInventoryMovementResponse> inventoryMovements = await ReadSaleInventoryMovementsAsync(connection, transaction, tenantId, saleId, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new SaleDetailResponse(
            sale.Id,
            sale.TenantId,
            sale.StoreId,
            sale.TerminalId,
            sale.CashShiftId,
            sale.CustomerId,
            sale.CashierUserId,
            sale.LocalSaleId,
            sale.Status,
            sale.SubtotalCents,
            sale.DiscountCents,
            sale.TaxCents,
            sale.TipCents,
            sale.TotalCents,
            sale.PaidCents,
            sale.ChangeCents,
            sale.Currency,
            sale.OccurredAt,
            sale.LocalCreatedAt,
            sale.Version,
            sale.CreatedAt,
            detailLines,
            sale.Payments,
            inventoryMovements);
    }

    public async Task<IReadOnlyCollection<SaleListItemResponse>?> ListAsync(
        Guid tenantId,
        SaleListFilters filters,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken, transaction);

        if (filters.StoreId.HasValue && !await StoreBelongsToTenantAsync(connection, transaction, tenantId, filters.StoreId.Value, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        if (filters.TerminalId.HasValue && !await TerminalBelongsToTenantAsync(connection, transaction, tenantId, filters.TerminalId.Value, filters.StoreId, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        const string sql = """
            SELECT
              s.id,
              s.tenant_id,
              s.store_id,
              s.terminal_id,
              s.cash_shift_id,
              s.customer_id,
              s.cashier_user_id,
              s.local_sale_id,
              s.status,
              s.total_cents,
              s.paid_cents,
              s.change_cents,
              s.currency::text,
              s.occurred_at,
              s.created_at,
              (
                SELECT COUNT(*)::int
                FROM pos.sale_lines sl
                WHERE sl.tenant_id = s.tenant_id
                  AND sl.sale_id = s.id
              ) AS line_count,
              (
                SELECT COUNT(*)::int
                FROM pos.payments p
                WHERE p.tenant_id = s.tenant_id
                  AND p.sale_id = s.id
              ) AS payment_count
            FROM pos.sales s
            WHERE s.tenant_id = @tenant_id
              AND s.deleted_at IS NULL
              AND (@store_id IS NULL OR s.store_id = @store_id)
              AND (@terminal_id IS NULL OR s.terminal_id = @terminal_id)
              AND (@status IS NULL OR s.status = @status)
              AND (@from IS NULL OR s.occurred_at >= @from)
              AND (@to IS NULL OR s.occurred_at <= @to)
            ORDER BY s.occurred_at DESC, s.created_at DESC
            LIMIT @limit;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        AddNullableGuid(command, "store_id", filters.StoreId);
        AddNullableGuid(command, "terminal_id", filters.TerminalId);
        command.Parameters.Add("status", NpgsqlDbType.Text).Value = string.IsNullOrWhiteSpace(filters.Status) ? (object)DBNull.Value : filters.Status;
        AddNullableTimestamp(command, "from", filters.From);
        AddNullableTimestamp(command, "to", filters.To);
        command.Parameters.AddWithValue("limit", Math.Clamp(filters.Limit, 1, 200));

        List<SaleListItemResponse> sales = [];
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                sales.Add(new SaleListItemResponse(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetGuid(2),
                    reader.GetGuid(3),
                    reader.IsDBNull(4) ? null : reader.GetGuid(4),
                    reader.IsDBNull(5) ? null : reader.GetGuid(5),
                    reader.GetGuid(6),
                    reader.GetGuid(7),
                    reader.GetString(8),
                    reader.GetInt64(9),
                    reader.GetInt64(10),
                    reader.GetInt64(11),
                    reader.GetString(12),
                    reader.GetFieldValue<DateTimeOffset>(13),
                    reader.GetFieldValue<DateTimeOffset>(14),
                    reader.GetInt32(15),
                    reader.GetInt32(16)));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return sales;
    }

    public async Task<ReceiptResponse?> GetReceiptAsync(
        Guid tenantId,
        Guid saleId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken, transaction);

        const string sql = """
            SELECT
              s.id,
              s.tenant_id,
              s.store_id,
              t.name AS tenant_name,
              st.name AS store_name,
              st.address AS store_address,
              st.phone AS store_phone,
              s.terminal_id,
              te.name AS terminal_name,
              s.cashier_user_id,
              u.full_name AS cashier_name,
              s.local_sale_id,
              s.status,
              s.currency::text,
              s.occurred_at,
              s.created_at,
              s.subtotal_cents,
              s.discount_cents,
              s.tax_cents,
              s.tip_cents,
              s.total_cents,
              s.paid_cents,
              s.change_cents
            FROM pos.sales s
            JOIN pos.tenants t
              ON t.id = s.tenant_id
            JOIN pos.stores st
              ON st.tenant_id = s.tenant_id
             AND st.id = s.store_id
            JOIN pos.terminals te
              ON te.tenant_id = s.tenant_id
             AND te.id = s.terminal_id
            JOIN pos.users u
              ON u.tenant_id = s.tenant_id
             AND u.id = s.cashier_user_id
            WHERE s.tenant_id = @tenant_id
              AND s.id = @sale_id
              AND s.deleted_at IS NULL;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("sale_id", saleId);

        ReceiptHeader? header = null;
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                header = new ReceiptHeader(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetGuid(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.GetGuid(7),
                    reader.GetString(8),
                    reader.GetGuid(9),
                    reader.GetString(10),
                    reader.GetGuid(11),
                    reader.GetString(12),
                    reader.GetString(13),
                    reader.GetFieldValue<DateTimeOffset>(14),
                    reader.GetFieldValue<DateTimeOffset>(15),
                    reader.GetInt64(16),
                    reader.GetInt64(17),
                    reader.GetInt64(18),
                    reader.GetInt64(19),
                    reader.GetInt64(20),
                    reader.GetInt64(21),
                    reader.GetInt64(22));
            }
        }

        if (header is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        IReadOnlyCollection<ReceiptLineResponse> lines = await ReadReceiptLinesAsync(connection, transaction, tenantId, saleId, cancellationToken);
        IReadOnlyCollection<ReceiptPaymentResponse> payments = await ReadReceiptPaymentsAsync(connection, transaction, tenantId, saleId, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new ReceiptResponse(
            header.SaleId,
            header.TenantId,
            header.StoreId,
            header.TenantName,
            header.StoreName,
            header.StoreAddress,
            header.StorePhone,
            header.TerminalId,
            header.TerminalName,
            header.CashierUserId,
            header.CashierName,
            header.LocalSaleId,
            header.Status,
            header.Currency,
            header.OccurredAt,
            header.CreatedAt,
            header.SubtotalCents,
            header.DiscountCents,
            header.TaxCents,
            header.TipCents,
            header.TotalCents,
            header.PaidCents,
            header.ChangeCents,
            lines,
            payments);
    }

    private static async Task<Guid?> ReadOpenCashShiftIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid storeId,
        Guid terminalId,
        Guid cashierUserId,
        Guid? customerId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT cs.id
            FROM pos.cash_shifts cs
            WHERE cs.tenant_id = @tenant_id
              AND cs.store_id = @store_id
              AND cs.terminal_id = @terminal_id
              AND cs.status = 'open'
              AND EXISTS (
                SELECT 1 FROM pos.tenants
                WHERE id = @tenant_id
                  AND status = 'active'
                  AND deleted_at IS NULL
              )
              AND EXISTS (
                SELECT 1 FROM pos.terminals
                WHERE tenant_id = @tenant_id
                  AND id = @terminal_id
                  AND store_id = @store_id
                  AND status = 'active'
                  AND hard_locked_at IS NULL
                  AND deleted_at IS NULL
              )
              AND EXISTS (
                SELECT 1 FROM pos.users
                WHERE tenant_id = @tenant_id
                  AND id = @cashier_user_id
                  AND status = 'active'
                  AND deleted_at IS NULL
              )
              AND (
                @customer_id IS NULL OR EXISTS (
                  SELECT 1 FROM pos.customers
                  WHERE tenant_id = @tenant_id
                    AND id = @customer_id
                    AND status = 'active'
                    AND deleted_at IS NULL
                )
              )
            FOR UPDATE;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("store_id", storeId);
        command.Parameters.AddWithValue("terminal_id", terminalId);
        command.Parameters.AddWithValue("cashier_user_id", cashierUserId);
        AddNullableGuid(command, "customer_id", customerId);

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid id ? id : null;
    }

    private static async Task<VoidableSale?> ReadVoidableSaleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid storeId,
        Guid terminalId,
        Guid saleId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, cash_shift_id, status
            FROM pos.sales
            WHERE tenant_id = @tenant_id
              AND store_id = @store_id
              AND terminal_id = @terminal_id
              AND id = @sale_id
              AND deleted_at IS NULL
            FOR UPDATE;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("store_id", storeId);
        command.Parameters.AddWithValue("terminal_id", terminalId);
        command.Parameters.AddWithValue("sale_id", saleId);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new VoidableSale(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2))
            : null;
    }

    private static async Task<bool> VoidRuntimeContextIsValidAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid cashShiftId,
        Guid voidedByUserId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
              SELECT 1
              FROM pos.cash_shifts cs
              JOIN pos.users u
                ON u.tenant_id = cs.tenant_id
               AND u.id = @voided_by_user_id
               AND u.status = 'active'
               AND u.deleted_at IS NULL
              WHERE cs.tenant_id = @tenant_id
                AND cs.id = @cash_shift_id
                AND cs.status = 'open'
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("cash_shift_id", cashShiftId);
        command.Parameters.AddWithValue("voided_by_user_id", voidedByUserId);

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is true;
    }

    private static async Task InsertVoidInventoryCompensationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid saleId,
        VoidSaleRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.inventory_ledger (
              tenant_id, store_id, terminal_id, product_id, variant_id,
              movement_type, quantity_delta, unit_id, reference_type, reference_id,
              source_event_id, local_occurred_at, metadata
            )
            SELECT
              tenant_id,
              store_id,
              terminal_id,
              product_id,
              variant_id,
              'void_compensation',
              -quantity_delta,
              unit_id,
              'sale_void',
              reference_id,
              @source_event_id,
              @local_occurred_at,
              jsonb_build_object(
                'saleId', reference_id,
                'voidedByUserId', CAST(@voided_by_user_id AS text),
                'reason', @reason,
                'compensatesLedgerId', id,
                'originalMovementType', movement_type,
                'effect', 'sale_void_inventory_compensation'
              )
            FROM pos.inventory_ledger il
            WHERE il.tenant_id = @tenant_id
              AND il.reference_type = 'sale'
              AND il.reference_id = @sale_id
              AND il.movement_type IN ('sale', 'sale_recipe_component')
              AND NOT EXISTS (
                SELECT 1
                FROM pos.inventory_ledger existing
                WHERE existing.tenant_id = il.tenant_id
                  AND existing.reference_type = 'sale_void'
                  AND existing.reference_id = il.reference_id
              );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("sale_id", saleId);
        command.Parameters.AddWithValue("source_event_id", saleId);
        command.Parameters.AddWithValue("local_occurred_at", request.OccurredAt);
        command.Parameters.AddWithValue("voided_by_user_id", request.VoidedByUserId);
        command.Parameters.AddWithValue("reason", request.Reason);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkSaleVoidedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid saleId,
        VoidSaleRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pos.sales
            SET status = 'voided',
                version = version + 1,
                updated_at = now(),
                metadata = metadata || jsonb_build_object(
                  'voidedByUserId', CAST(@voided_by_user_id AS text),
                  'voidReason', @reason,
                  'voidedAt', CAST(@voided_at AS text)
                )
            WHERE tenant_id = @tenant_id
              AND id = @sale_id
              AND status = 'completed';
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("sale_id", saleId);
        command.Parameters.AddWithValue("voided_by_user_id", request.VoidedByUserId);
        command.Parameters.AddWithValue("reason", request.Reason);
        command.Parameters.AddWithValue("voided_at", request.OccurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkPaymentsVoidedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid saleId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pos.payments
            SET status = 'voided'
            WHERE tenant_id = @tenant_id
              AND sale_id = @sale_id
              AND status = 'approved';
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("sale_id", saleId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> ReadSaleCashImpactAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid saleId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT GREATEST(
              COALESCE(SUM(CASE WHEN pm.method_type = 'cash' THEN p.amount_cents ELSE 0 END), 0) - s.change_cents,
              0
            )
            FROM pos.sales s
            JOIN pos.payments p
              ON p.tenant_id = s.tenant_id
             AND p.sale_id = s.id
            JOIN pos.payment_methods pm
              ON pm.tenant_id = p.tenant_id
             AND pm.id = p.payment_method_id
            WHERE s.tenant_id = @tenant_id
              AND s.id = @sale_id
            GROUP BY s.change_cents;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("sale_id", saleId);

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null || result is DBNull
            ? 0
            : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static async Task<PreparedDiscount?> ReadActiveDiscountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid discountId,
        Guid storeId,
        Guid productId,
        Guid? categoryId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, discount_type, value
            FROM pos.discounts
            WHERE tenant_id = @tenant_id
              AND id = @discount_id
              AND status = 'active'
              AND deleted_at IS NULL
              AND (starts_at IS NULL OR starts_at <= @occurred_at)
              AND (ends_at IS NULL OR ends_at >= @occurred_at)
              AND (store_id IS NULL OR store_id = @store_id)
              AND (product_id IS NULL OR product_id = @product_id)
              AND (category_id IS NULL OR category_id = @category_id)
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("discount_id", discountId);
        command.Parameters.AddWithValue("store_id", storeId);
        command.Parameters.AddWithValue("product_id", productId);
        AddNullableGuid(command, "category_id", categoryId);
        command.Parameters.AddWithValue("occurred_at", occurredAt);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PreparedDiscount(reader.GetGuid(0), reader.GetString(1), reader.GetDecimal(2));
    }

    private static long CalculateDiscountCents(PreparedDiscount discount, long grossCents)
    {
        if (discount.DiscountType == "percentage")
        {
            return Math.Min(grossCents, ToCents(grossCents * discount.Value / 100m));
        }

        return Math.Min(grossCents, ToCents(discount.Value));
    }

    private static async Task<PreparedProduct?> ReadProductAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid productId,
        Guid? variantId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
              p.id,
              p.name,
              p.category_id,
              p.sale_unit_id,
              p.inventory_unit_id,
              p.product_type,
              p.tax_mode,
              p.is_stock_tracked,
              COALESCE(v.name, p.name) AS description,
              pp.price_cents,
              r.id AS recipe_id,
              r.waste_percent
            FROM pos.products p
            LEFT JOIN pos.product_variants v
              ON v.tenant_id = p.tenant_id
             AND v.product_id = p.id
             AND v.id = @variant_id
             AND v.status = 'active'
             AND v.deleted_at IS NULL
            JOIN pos.product_prices pp
              ON pp.tenant_id = p.tenant_id
             AND pp.product_id = p.id
             AND (
                  (pp.variant_id IS NULL AND @variant_id IS NULL)
                  OR pp.variant_id = @variant_id
             )
             AND pp.deleted_at IS NULL
             AND (pp.starts_at IS NULL OR pp.starts_at <= now())
             AND (pp.ends_at IS NULL OR pp.ends_at > now())
            JOIN pos.price_lists pl
              ON pl.tenant_id = p.tenant_id
             AND pl.id = pp.price_list_id
             AND pl.status = 'active'
             AND pl.deleted_at IS NULL
            LEFT JOIN pos.recipes r
              ON r.tenant_id = p.tenant_id
             AND r.output_product_id = p.id
             AND (
                  (r.output_variant_id IS NULL AND @variant_id IS NULL)
                  OR r.output_variant_id = @variant_id
             )
             AND r.status = 'active'
             AND r.deleted_at IS NULL
            WHERE p.tenant_id = @tenant_id
              AND p.id = @product_id
              AND p.is_sellable = true
              AND p.status = 'active'
              AND p.deleted_at IS NULL
              AND (@variant_id IS NULL OR v.id IS NOT NULL)
            ORDER BY pp.starts_at DESC NULLS LAST, pp.created_at DESC
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("product_id", productId);
        AddNullableGuid(command, "variant_id", variantId);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PreparedProduct(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            variantId,
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetBoolean(7),
            reader.GetString(8),
            reader.GetInt64(9),
            reader.IsDBNull(10) ? null : reader.GetGuid(10),
            reader.IsDBNull(11) ? 0 : reader.GetFieldValue<decimal>(11));
    }

    private static async Task<IReadOnlyCollection<PreparedModifier>> ReadModifiersAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid productId,
        IReadOnlyCollection<Guid>? modifierIds,
        CancellationToken cancellationToken)
    {
        if (modifierIds is null || modifierIds.Count == 0)
        {
            return [];
        }

        const string sql = """
            SELECT
              m.id, m.name, m.price_delta_cents, m.linked_product_id, m.linked_variant_id,
              m.inventory_behavior, m.consumption_quantity, m.consumption_unit_id,
              m.replaces_product_id, m.replaces_variant_id
            FROM pos.modifiers m
            JOIN pos.product_modifier_groups pmg
              ON pmg.tenant_id = m.tenant_id
             AND pmg.modifier_group_id = m.group_id
             AND pmg.product_id = @product_id
            WHERE m.tenant_id = @tenant_id
              AND m.id = ANY(@modifier_ids)
              AND m.deleted_at IS NULL
            ORDER BY m.name;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("product_id", productId);
        command.Parameters.Add("modifier_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = modifierIds.ToArray();

        List<PreparedModifier> modifiers = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            modifiers.Add(new PreparedModifier(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<decimal>(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7),
                reader.IsDBNull(8) ? null : reader.GetGuid(8),
                reader.IsDBNull(9) ? null : reader.GetGuid(9)));
        }

        return modifiers;
    }

    private static async Task InsertInventoryEffectsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid storeId,
        Guid terminalId,
        Guid saleId,
        Guid localSaleId,
        DateTimeOffset occurredAt,
        PreparedSaleLine line,
        CancellationToken cancellationToken)
    {
        if (line.Product.RecipeId.HasValue)
        {
            await InsertRecipeInventoryEffectsAsync(connection, transaction, tenantId, storeId, terminalId, saleId, localSaleId, occurredAt, line, cancellationToken);
        }
        else if (line.Product.IsStockTracked && line.Product.InventoryUnitId.HasValue)
        {
            await InsertInventoryLedgerAsync(
                connection, transaction, tenantId, storeId, terminalId,
                line.Product.Id, line.Product.VariantId, "sale", -line.Quantity,
                line.Product.InventoryUnitId.Value, saleId, saleId, occurredAt,
                new
                {
                    localSaleId,
                    lineNumber = line.LineNumber,
                    productName = line.Product.Name,
                    effect = "direct_product_sale"
                },
                cancellationToken);
        }

        decimal wasteMultiplier = line.Product.RecipeId.HasValue
            ? 1 + (line.Product.RecipeWastePercent / 100)
            : 1;

        foreach (PreparedModifier modifier in line.Modifiers.Where(x => x.InventoryBehavior is "add" or "substitute"))
        {
            if (!modifier.LinkedProductId.HasValue || !modifier.ConsumptionQuantity.HasValue || !modifier.ConsumptionUnitId.HasValue)
            {
                throw new InvalidOperationException($"Modifier {modifier.Id} has an incomplete inventory effect configuration.");
            }

            PreparedInventoryProduct? linkedProduct = await ReadInventoryProductAsync(
                connection, transaction, tenantId, modifier.LinkedProductId.Value, modifier.LinkedVariantId, cancellationToken);
            if (linkedProduct is null || !linkedProduct.IsStockTracked || !linkedProduct.InventoryUnitId.HasValue)
            {
                throw new InvalidOperationException($"Modifier {modifier.Id} points to an inventory product that is not stock tracked.");
            }

            if (linkedProduct.InventoryUnitId.Value != modifier.ConsumptionUnitId.Value)
            {
                throw new InvalidOperationException($"Modifier {modifier.Id} consumption unit does not match the linked product inventory unit.");
            }

            decimal quantityDelta = -(modifier.ConsumptionQuantity.Value * line.Quantity * wasteMultiplier);
            await InsertInventoryLedgerAsync(
                connection, transaction, tenantId, storeId, terminalId,
                linkedProduct.Id, linkedProduct.VariantId, "sale_recipe_component", quantityDelta,
                modifier.ConsumptionUnitId.Value, saleId, saleId, occurredAt,
                new
                {
                    localSaleId,
                    lineNumber = line.LineNumber,
                    modifierId = modifier.Id,
                    modifierName = modifier.Name,
                    modifierBehavior = modifier.InventoryBehavior,
                    replacesProductId = modifier.ReplacesProductId,
                    replacesVariantId = modifier.ReplacesVariantId,
                    effect = modifier.InventoryBehavior == "substitute"
                        ? "modifier_substitute_component"
                        : "modifier_add_component"
                },
                cancellationToken);
        }
    }

    private static async Task InsertRecipeInventoryEffectsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid storeId,
        Guid terminalId,
        Guid saleId,
        Guid localSaleId,
        DateTimeOffset occurredAt,
        PreparedSaleLine line,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT ingredient_product_id, ingredient_variant_id, quantity, unit_id, optional
            FROM pos.recipe_items
            WHERE tenant_id = @tenant_id
              AND recipe_id = @recipe_id
            ORDER BY created_at;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("recipe_id", line.Product.RecipeId!.Value);

        List<PreparedRecipeItem> items = [];
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new PreparedRecipeItem(
                    reader.GetGuid(0),
                    reader.IsDBNull(1) ? null : reader.GetGuid(1),
                    reader.GetFieldValue<decimal>(2),
                    reader.GetGuid(3),
                    reader.GetBoolean(4)));
            }
        }

        IReadOnlyCollection<PreparedModifier> substitutions = line.Modifiers
            .Where(x => x.InventoryBehavior == "substitute")
            .ToArray();

        foreach (PreparedRecipeItem item in items)
        {
            bool replaced = substitutions.Any(modifier =>
                modifier.ReplacesProductId == item.IngredientProductId
                && (!modifier.ReplacesVariantId.HasValue || modifier.ReplacesVariantId == item.IngredientVariantId));
            if (replaced)
            {
                continue;
            }

            decimal wasteMultiplier = 1 + (line.Product.RecipeWastePercent / 100);
            decimal quantityDelta = -(item.Quantity * line.Quantity * wasteMultiplier);
            await InsertInventoryLedgerAsync(
                connection, transaction, tenantId, storeId, terminalId,
                item.IngredientProductId, item.IngredientVariantId, "sale_recipe_component", quantityDelta,
                item.UnitId, saleId, saleId, occurredAt,
                new
                {
                    localSaleId,
                    lineNumber = line.LineNumber,
                    recipeId = line.Product.RecipeId,
                    outputProductId = line.Product.Id,
                    outputVariantId = line.Product.VariantId,
                    effect = "recipe_component",
                    item.Optional
                },
                cancellationToken);
        }
    }

    private static async Task<PreparedInventoryProduct?> ReadInventoryProductAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid productId,
        Guid? variantId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, inventory_unit_id, is_stock_tracked
            FROM pos.products
            WHERE tenant_id = @tenant_id
              AND id = @product_id
              AND status = 'active'
              AND deleted_at IS NULL;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("product_id", productId);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new PreparedInventoryProduct(
                reader.GetGuid(0),
                variantId,
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.GetBoolean(2))
            : null;
    }

    private static async Task InsertInventoryLedgerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid storeId,
        Guid terminalId,
        Guid productId,
        Guid? variantId,
        string movementType,
        decimal quantityDelta,
        Guid unitId,
        Guid referenceId,
        Guid sourceEventId,
        DateTimeOffset localOccurredAt,
        object metadata,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.inventory_ledger (
              tenant_id, store_id, terminal_id, product_id, variant_id,
              movement_type, quantity_delta, unit_id, reference_type, reference_id,
              source_event_id, local_occurred_at, metadata
            )
            VALUES (
              @tenant_id, @store_id, @terminal_id, @product_id, @variant_id,
              @movement_type, @quantity_delta, @unit_id, 'sale', @reference_id,
              @source_event_id, @local_occurred_at, CAST(@metadata AS jsonb)
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("store_id", storeId);
        command.Parameters.AddWithValue("terminal_id", terminalId);
        command.Parameters.AddWithValue("product_id", productId);
        AddNullableGuid(command, "variant_id", variantId);
        command.Parameters.AddWithValue("movement_type", movementType);
        command.Parameters.AddWithValue("quantity_delta", quantityDelta);
        command.Parameters.AddWithValue("unit_id", unitId);
        command.Parameters.AddWithValue("reference_id", referenceId);
        command.Parameters.AddWithValue("source_event_id", sourceEventId);
        command.Parameters.AddWithValue("local_occurred_at", localOccurredAt);
        command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(metadata));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<PreparedPaymentMethod?> ReadPaymentMethodAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        string methodCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, code, method_type
            FROM pos.payment_methods
            WHERE tenant_id = @tenant_id
              AND code = @code
              AND status = 'active'
              AND deleted_at IS NULL;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("code", methodCode.Trim());

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new PreparedPaymentMethod(reader.GetGuid(0), reader.GetString(1), reader.GetString(2))
            : null;
    }

    private static async Task<Guid> InsertSaleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid storeId,
        Guid terminalId,
        Guid cashShiftId,
        CreateSaleRequest request,
        long subtotalCents,
        long discountCents,
        long taxCents,
        long totalCents,
        long paidCents,
        long changeCents,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.sales (
              tenant_id, store_id, terminal_id, cash_shift_id, customer_id, cashier_user_id,
              local_sale_id, status, subtotal_cents, discount_cents, tax_cents, tip_cents,
              total_cents, paid_cents, change_cents, currency, occurred_at, local_created_at
            )
            VALUES (
              @tenant_id, @store_id, @terminal_id, @cash_shift_id, @customer_id, @cashier_user_id,
              @local_sale_id, 'completed', @subtotal_cents, @discount_cents, @tax_cents, @tip_cents,
              @total_cents, @paid_cents, @change_cents, 'MXN', @occurred_at, @local_created_at
            )
            RETURNING id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("store_id", storeId);
        command.Parameters.AddWithValue("terminal_id", terminalId);
        command.Parameters.AddWithValue("cash_shift_id", cashShiftId);
        AddNullableGuid(command, "customer_id", request.CustomerId);
        command.Parameters.AddWithValue("cashier_user_id", request.CashierUserId);
        command.Parameters.AddWithValue("local_sale_id", request.LocalSaleId);
        command.Parameters.AddWithValue("subtotal_cents", subtotalCents);
        command.Parameters.AddWithValue("discount_cents", discountCents);
        command.Parameters.AddWithValue("tax_cents", taxCents);
        command.Parameters.AddWithValue("tip_cents", request.TipCents);
        command.Parameters.AddWithValue("total_cents", totalCents);
        command.Parameters.AddWithValue("paid_cents", paidCents);
        command.Parameters.AddWithValue("change_cents", changeCents);
        command.Parameters.AddWithValue("occurred_at", request.OccurredAt);
        command.Parameters.AddWithValue("local_created_at", request.LocalCreatedAt);

        return (Guid)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Sale insert did not return id."));
    }

    private static async Task InsertSaleLineAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid saleId,
        PreparedSaleLine line,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.sale_lines (
              tenant_id, sale_id, product_id, variant_id, line_number, description,
              quantity, unit_id, unit_price_cents, discount_cents, tax_cents, total_cents,
              recipe_id, preparation_note, modifiers, snapshot
            )
            VALUES (
              @tenant_id, @sale_id, @product_id, @variant_id, @line_number, @description,
              @quantity, @unit_id, @unit_price_cents, @discount_cents, @tax_cents, @total_cents,
              @recipe_id, @preparation_note, CAST(@modifiers AS jsonb), CAST(@snapshot AS jsonb)
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("sale_id", saleId);
        command.Parameters.AddWithValue("product_id", line.Product.Id);
        AddNullableGuid(command, "variant_id", line.Product.VariantId);
        command.Parameters.AddWithValue("line_number", line.LineNumber);
        command.Parameters.AddWithValue("description", line.Product.Description);
        command.Parameters.AddWithValue("quantity", line.Quantity);
        AddNullableGuid(command, "unit_id", line.Product.UnitId);
        command.Parameters.AddWithValue("unit_price_cents", line.UnitPriceCents);
        command.Parameters.AddWithValue("discount_cents", line.DiscountCents);
        command.Parameters.AddWithValue("tax_cents", line.TaxCents);
        command.Parameters.AddWithValue("total_cents", line.TotalCents);
        AddNullableGuid(command, "recipe_id", line.Product.RecipeId);
        command.Parameters.AddWithValue("preparation_note", string.IsNullOrWhiteSpace(line.PreparationNote) ? (object)DBNull.Value : line.PreparationNote);
        command.Parameters.AddWithValue("modifiers", JsonSerializer.Serialize(line.Modifiers.Select(x => x.Id)));
        command.Parameters.AddWithValue("snapshot", JsonSerializer.Serialize(new
        {
            productName = line.Product.Name,
            productType = line.Product.ProductType,
            taxMode = line.Product.TaxMode,
            discountId = line.DiscountId,
            modifiers = line.Modifiers.Select(x => new
            {
                x.Id,
                x.Name,
                x.PriceDeltaCents,
                x.InventoryBehavior,
                x.LinkedProductId,
                x.LinkedVariantId,
                x.ConsumptionQuantity,
                x.ConsumptionUnitId,
                x.ReplacesProductId,
                x.ReplacesVariantId
            })
        }));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertPaymentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid saleId,
        PreparedPayment payment,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.payments (
              tenant_id, sale_id, payment_method_id, local_payment_id,
              amount_cents, currency, status, reference
            )
            VALUES (
              @tenant_id, @sale_id, @payment_method_id, @local_payment_id,
              @amount_cents, 'MXN', 'approved', @reference
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("sale_id", saleId);
        command.Parameters.AddWithValue("payment_method_id", payment.Method.Id);
        command.Parameters.AddWithValue("local_payment_id", payment.LocalPaymentId);
        command.Parameters.AddWithValue("amount_cents", payment.AmountCents);
        command.Parameters.AddWithValue("reference", string.IsNullOrWhiteSpace(payment.Reference) ? (object)DBNull.Value : payment.Reference);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task IncreaseExpectedCashAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid cashShiftId,
        long cashDeltaCents,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pos.cash_shifts
            SET expected_cash_cents = expected_cash_cents + @cash_delta_cents,
                updated_at = now()
            WHERE tenant_id = @tenant_id
              AND id = @cash_shift_id
              AND status = 'open';
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("cash_shift_id", cashShiftId);
        command.Parameters.AddWithValue("cash_delta_cents", cashDeltaCents);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<SaleResponse?> ReadSaleByLocalIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid terminalId,
        Guid localSaleId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id
            FROM pos.sales
            WHERE tenant_id = @tenant_id
              AND terminal_id = @terminal_id
              AND local_sale_id = @local_sale_id
              AND deleted_at IS NULL;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("terminal_id", terminalId);
        command.Parameters.AddWithValue("local_sale_id", localSaleId);

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid saleId
            ? await ReadSaleByIdAsync(connection, transaction, tenantId, saleId, cancellationToken)
            : null;
    }

    private static async Task<SaleResponse?> ReadSaleByIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid saleId,
        CancellationToken cancellationToken)
    {
        const string saleSql = """
            SELECT id, tenant_id, store_id, terminal_id, cash_shift_id, customer_id, cashier_user_id,
                   local_sale_id, status, subtotal_cents, discount_cents, tax_cents, tip_cents,
                   total_cents, paid_cents, change_cents, currency::text, occurred_at, local_created_at,
                   version, created_at
            FROM pos.sales
            WHERE tenant_id = @tenant_id
              AND id = @sale_id
              AND deleted_at IS NULL;
            """;

        await using var saleCommand = new NpgsqlCommand(saleSql, connection, transaction);
        saleCommand.Parameters.AddWithValue("tenant_id", tenantId);
        saleCommand.Parameters.AddWithValue("sale_id", saleId);

        SaleHeader? header = null;
        await using (NpgsqlDataReader reader = await saleCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                header = new SaleHeader(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetGuid(2),
                    reader.GetGuid(3),
                    reader.GetGuid(4),
                    reader.IsDBNull(5) ? null : reader.GetGuid(5),
                    reader.GetGuid(6),
                    reader.GetGuid(7),
                    reader.GetString(8),
                    reader.GetInt64(9),
                    reader.GetInt64(10),
                    reader.GetInt64(11),
                    reader.GetInt64(12),
                    reader.GetInt64(13),
                    reader.GetInt64(14),
                    reader.GetInt64(15),
                    reader.GetString(16),
                    reader.GetFieldValue<DateTimeOffset>(17),
                    reader.GetFieldValue<DateTimeOffset>(18),
                    reader.GetInt64(19),
                    reader.GetFieldValue<DateTimeOffset>(20));
            }
        }

        if (header is null)
        {
            return null;
        }

        IReadOnlyCollection<SaleLineResponse> lines = await ReadSaleLinesAsync(connection, transaction, tenantId, saleId, cancellationToken);
        IReadOnlyCollection<SalePaymentResponse> payments = await ReadPaymentsAsync(connection, transaction, tenantId, saleId, cancellationToken);

        return new SaleResponse(
            header.Id,
            header.TenantId,
            header.StoreId,
            header.TerminalId,
            header.CashShiftId,
            header.CustomerId,
            header.CashierUserId,
            header.LocalSaleId,
            header.Status,
            header.SubtotalCents,
            header.DiscountCents,
            header.TaxCents,
            header.TipCents,
            header.TotalCents,
            header.PaidCents,
            header.ChangeCents,
            header.Currency,
            header.OccurredAt,
            header.LocalCreatedAt,
            header.Version,
            header.CreatedAt,
            lines,
            payments);
    }

    private static async Task<IReadOnlyCollection<SaleLineResponse>> ReadSaleLinesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid saleId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, product_id, variant_id, line_number, description, quantity, unit_id,
                   unit_price_cents, discount_cents, tax_cents, total_cents, recipe_id,
                   preparation_note, modifiers::text
            FROM pos.sale_lines
            WHERE tenant_id = @tenant_id
              AND sale_id = @sale_id
            ORDER BY line_number;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("sale_id", saleId);

        List<SaleLineResponse> lines = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lines.Add(new SaleLineResponse(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetFieldValue<decimal>(5).ToString(CultureInfo.InvariantCulture),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.GetInt64(7),
                reader.GetInt64(8),
                reader.GetInt64(9),
                reader.GetInt64(10),
                reader.IsDBNull(11) ? null : reader.GetGuid(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                ReadModifierIds(reader.GetString(13))));
        }

        return lines;
    }

    private static async Task<IReadOnlyCollection<SalePaymentResponse>> ReadPaymentsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid saleId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT p.id, p.payment_method_id, p.local_payment_id, pm.code, pm.method_type,
                   p.amount_cents, p.currency::text, p.status, p.reference, p.created_at
            FROM pos.payments p
            JOIN pos.payment_methods pm
              ON pm.tenant_id = p.tenant_id
             AND pm.id = p.payment_method_id
            WHERE p.tenant_id = @tenant_id
              AND p.sale_id = @sale_id
            ORDER BY p.created_at;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("sale_id", saleId);

        List<SalePaymentResponse> payments = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            payments.Add(new SalePaymentResponse(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt64(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.GetFieldValue<DateTimeOffset>(9)));
        }

        return payments;
    }



    private static async Task<bool> StoreBelongsToTenantAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid storeId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
              SELECT 1
              FROM pos.stores
              WHERE tenant_id = @tenant_id
                AND id = @store_id
                AND deleted_at IS NULL
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("store_id", storeId);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is true;
    }

    private static async Task<bool> TerminalBelongsToTenantAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid terminalId,
        Guid? storeId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
              SELECT 1
              FROM pos.terminals
              WHERE tenant_id = @tenant_id
                AND id = @terminal_id
                AND (@store_id IS NULL OR store_id = @store_id)
                AND deleted_at IS NULL
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("terminal_id", terminalId);
        AddNullableGuid(command, "store_id", storeId);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is true;
    }

    private static async Task<IReadOnlyCollection<SaleDetailLineResponse>> ReadSaleDetailLinesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid saleId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, product_id, variant_id, line_number, description, quantity, unit_id,
                   unit_price_cents, discount_cents, tax_cents, total_cents, recipe_id,
                   preparation_note, modifiers::text, snapshot::text
            FROM pos.sale_lines
            WHERE tenant_id = @tenant_id
              AND sale_id = @sale_id
            ORDER BY line_number;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("sale_id", saleId);

        List<SaleDetailLineResponse> lines = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            IReadOnlyCollection<Guid> modifierIds = ReadModifierIds(reader.GetString(13));
            IReadOnlyCollection<SaleModifierResponse> modifiers = ReadModifierSnapshots(reader.GetString(14), modifierIds);
            lines.Add(new SaleDetailLineResponse(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetFieldValue<decimal>(5).ToString(CultureInfo.InvariantCulture),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.GetInt64(7),
                reader.GetInt64(8),
                reader.GetInt64(9),
                reader.GetInt64(10),
                reader.IsDBNull(11) ? null : reader.GetGuid(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                modifierIds,
                modifiers));
        }

        return lines;
    }

    private static async Task<IReadOnlyCollection<SaleInventoryMovementResponse>> ReadSaleInventoryMovementsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid saleId,
        CancellationToken cancellationToken)
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
              il.metadata ->> 'effect' AS effect,
              NULLIF(il.metadata ->> 'recipeId', '')::uuid AS recipe_id,
              NULLIF(il.metadata ->> 'modifierId', '')::uuid AS modifier_id,
              il.metadata ->> 'modifierBehavior' AS modifier_behavior,
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
              AND il.reference_id = @sale_id
              AND il.reference_type IN ('sale', 'sale_void')
            ORDER BY il.created_at, il.id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("sale_id", saleId);

        List<SaleInventoryMovementResponse> movements = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            movements.Add(new SaleInventoryMovementResponse(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetFieldValue<decimal>(8).ToString(CultureInfo.InvariantCulture),
                reader.GetGuid(9),
                reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetGuid(12),
                reader.IsDBNull(13) ? null : reader.GetGuid(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.GetFieldValue<DateTimeOffset>(15),
                reader.GetFieldValue<DateTimeOffset>(16)));
        }

        return movements;
    }

    private static async Task<IReadOnlyCollection<ReceiptLineResponse>> ReadReceiptLinesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid saleId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, line_number, description, quantity, unit_price_cents, discount_cents,
                   tax_cents, total_cents, preparation_note, modifiers::text, snapshot::text
            FROM pos.sale_lines
            WHERE tenant_id = @tenant_id
              AND sale_id = @sale_id
            ORDER BY line_number;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("sale_id", saleId);

        List<ReceiptLineResponse> lines = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            IReadOnlyCollection<Guid> modifierIds = ReadModifierIds(reader.GetString(9));
            IReadOnlyCollection<ReceiptModifierResponse> modifiers = ReadModifierSnapshots(reader.GetString(10), modifierIds)
                .Select(x => new ReceiptModifierResponse(x.Id, x.Name, x.PriceDeltaCents, x.InventoryBehavior))
                .ToArray();

            lines.Add(new ReceiptLineResponse(
                reader.GetGuid(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetFieldValue<decimal>(3).ToString(CultureInfo.InvariantCulture),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetInt64(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                modifiers));
        }

        return lines;
    }

    private static async Task<IReadOnlyCollection<ReceiptPaymentResponse>> ReadReceiptPaymentsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid saleId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT p.id, pm.code, pm.method_type, p.amount_cents, p.currency::text,
                   p.status, p.reference, p.created_at
            FROM pos.payments p
            JOIN pos.payment_methods pm
              ON pm.tenant_id = p.tenant_id
             AND pm.id = p.payment_method_id
            WHERE p.tenant_id = @tenant_id
              AND p.sale_id = @sale_id
            ORDER BY p.created_at;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("sale_id", saleId);

        List<ReceiptPaymentResponse> payments = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            payments.Add(new ReceiptPaymentResponse(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetFieldValue<DateTimeOffset>(7)));
        }

        return payments;
    }

    private static IReadOnlyCollection<SaleModifierResponse> ReadModifierSnapshots(string snapshotJson, IReadOnlyCollection<Guid> modifierIds)
    {
        if (modifierIds.Count == 0)
        {
            return [];
        }

        using JsonDocument document = JsonDocument.Parse(snapshotJson);
        if (!document.RootElement.TryGetProperty("modifiers", out JsonElement modifiersElement) || modifiersElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<SaleModifierResponse> modifiers = [];
        foreach (JsonElement element in modifiersElement.EnumerateArray())
        {
            if (!TryReadGuidProperty(element, "Id", out Guid id) && !TryReadGuidProperty(element, "id", out id))
            {
                continue;
            }

            if (!modifierIds.Contains(id))
            {
                continue;
            }

            modifiers.Add(new SaleModifierResponse(
                id,
                ReadStringProperty(element, "Name", "name") ?? string.Empty,
                ReadInt64Property(element, "PriceDeltaCents", "priceDeltaCents"),
                ReadStringProperty(element, "InventoryBehavior", "inventoryBehavior") ?? "none",
                ReadNullableGuidProperty(element, "LinkedProductId", "linkedProductId"),
                ReadNullableGuidProperty(element, "LinkedVariantId", "linkedVariantId"),
                ReadNullableDecimalProperty(element, "ConsumptionQuantity", "consumptionQuantity"),
                ReadNullableGuidProperty(element, "ConsumptionUnitId", "consumptionUnitId"),
                ReadNullableGuidProperty(element, "ReplacesProductId", "replacesProductId"),
                ReadNullableGuidProperty(element, "ReplacesVariantId", "replacesVariantId")));
        }

        return modifiers;
    }

    private static string? ReadStringProperty(JsonElement element, string pascalName, string camelName)
    {
        if (element.TryGetProperty(pascalName, out JsonElement pascal) && pascal.ValueKind == JsonValueKind.String)
        {
            return pascal.GetString();
        }

        return element.TryGetProperty(camelName, out JsonElement camel) && camel.ValueKind == JsonValueKind.String
            ? camel.GetString()
            : null;
    }

    private static long ReadInt64Property(JsonElement element, string pascalName, string camelName)
    {
        if (element.TryGetProperty(pascalName, out JsonElement pascal) && pascal.TryGetInt64(out long pascalValue))
        {
            return pascalValue;
        }

        return element.TryGetProperty(camelName, out JsonElement camel) && camel.TryGetInt64(out long camelValue)
            ? camelValue
            : 0;
    }

    private static string? ReadNullableDecimalProperty(JsonElement element, string pascalName, string camelName)
    {
        if (element.TryGetProperty(pascalName, out JsonElement pascal) && pascal.ValueKind == JsonValueKind.Number)
        {
            return pascal.GetDecimal().ToString(CultureInfo.InvariantCulture);
        }

        return element.TryGetProperty(camelName, out JsonElement camel) && camel.ValueKind == JsonValueKind.Number
            ? camel.GetDecimal().ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static Guid? ReadNullableGuidProperty(JsonElement element, string pascalName, string camelName)
    {
        return TryReadGuidProperty(element, pascalName, out Guid pascal)
            ? pascal
            : TryReadGuidProperty(element, camelName, out Guid camel)
                ? camel
                : null;
    }

    private static bool TryReadGuidProperty(JsonElement element, string propertyName, out Guid id)
    {
        id = Guid.Empty;
        if (!element.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        return Guid.TryParse(value.GetString(), out id);
    }

    private static IReadOnlyCollection<Guid> ReadModifierIds(string json)
    {
        return JsonSerializer.Deserialize<IReadOnlyCollection<Guid>>(json) ?? [];
    }

    private static long ToCents(decimal value)
    {
        return decimal.ToInt64(decimal.Round(value, 0, MidpointRounding.AwayFromZero));
    }

    private static void AddNullableGuid(NpgsqlCommand command, string name, Guid? value)
    {
        command.Parameters.Add(name, NpgsqlDbType.Uuid).Value = value.HasValue ? value.Value : (object)DBNull.Value;
    }

    private static void AddNullableTimestamp(NpgsqlCommand command, string name, DateTimeOffset? value)
    {
        command.Parameters.Add(name, NpgsqlDbType.TimestampTz).Value = value.HasValue ? value.Value : (object)DBNull.Value;
    }

    private sealed record PreparedProduct(
        Guid Id,
        string Name,
        Guid? CategoryId,
        Guid? VariantId,
        Guid? UnitId,
        Guid? InventoryUnitId,
        string ProductType,
        string TaxMode,
        bool IsStockTracked,
        string Description,
        long PriceCents,
        Guid? RecipeId,
        decimal RecipeWastePercent);

    private sealed record PreparedModifier(
        Guid Id,
        string Name,
        long PriceDeltaCents,
        Guid? LinkedProductId,
        Guid? LinkedVariantId,
        string InventoryBehavior,
        decimal? ConsumptionQuantity,
        Guid? ConsumptionUnitId,
        Guid? ReplacesProductId,
        Guid? ReplacesVariantId);

    private sealed record PreparedRecipeItem(Guid IngredientProductId, Guid? IngredientVariantId, decimal Quantity, Guid UnitId, bool Optional);

    private sealed record PreparedInventoryProduct(Guid Id, Guid? VariantId, Guid? InventoryUnitId, bool IsStockTracked);

    private sealed record PreparedPaymentMethod(Guid Id, string Code, string MethodType);

    private sealed record PreparedPayment(PreparedPaymentMethod Method, Guid LocalPaymentId, long AmountCents, string? Reference);

    private sealed record PreparedDiscount(Guid Id, string DiscountType, decimal Value);

    private sealed record VoidableSale(Guid Id, Guid CashShiftId, string Status);

    private sealed record PreparedSaleLine(
        int LineNumber,
        PreparedProduct Product,
        decimal Quantity,
        long UnitPriceCents,
        long DiscountCents,
        Guid? DiscountId,
        long TaxCents,
        long TotalCents,
        string? PreparationNote,
        IReadOnlyCollection<PreparedModifier> Modifiers)
    {
        public long UnitPriceGrossCents => ToCents(Quantity * UnitPriceCents);
    }

    private sealed record ReceiptHeader(
        Guid SaleId,
        Guid TenantId,
        Guid StoreId,
        string TenantName,
        string StoreName,
        string? StoreAddress,
        string? StorePhone,
        Guid TerminalId,
        string TerminalName,
        Guid CashierUserId,
        string CashierName,
        Guid LocalSaleId,
        string Status,
        string Currency,
        DateTimeOffset OccurredAt,
        DateTimeOffset CreatedAt,
        long SubtotalCents,
        long DiscountCents,
        long TaxCents,
        long TipCents,
        long TotalCents,
        long PaidCents,
        long ChangeCents);

    private sealed record SaleHeader(
        Guid Id,
        Guid TenantId,
        Guid StoreId,
        Guid TerminalId,
        Guid CashShiftId,
        Guid? CustomerId,
        Guid CashierUserId,
        Guid LocalSaleId,
        string Status,
        long SubtotalCents,
        long DiscountCents,
        long TaxCents,
        long TipCents,
        long TotalCents,
        long PaidCents,
        long ChangeCents,
        string Currency,
        DateTimeOffset OccurredAt,
        DateTimeOffset LocalCreatedAt,
        long Version,
        DateTimeOffset CreatedAt);
}
