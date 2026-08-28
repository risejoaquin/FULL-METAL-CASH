using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using SolidPOS.PosServer.Application.Returns;
using SolidPOS.PosServer.Contracts.Returns;
using SolidPOS.PosServer.Infrastructure.PostgreSql;

namespace SolidPOS.PosServer.Infrastructure.Returns;

public sealed class PostgreSqlReturnsRepository : IReturnsRepository
{
    private readonly string _connectionString;

    public PostgreSqlReturnsRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    }

    public async Task<ReturnResponse?> CreateAsync(Guid tenantId, Guid storeId, Guid terminalId, CreateReturnRequest request, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken, transaction);

        ReturnResponse? existing = await ReadReturnByLocalIdAsync(connection, transaction, tenantId, request.SaleId, request.LocalReturnId, cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return existing;
        }

        ReturnableSale? sale = await ReadReturnableSaleAsync(connection, transaction, tenantId, storeId, terminalId, request.SaleId, cancellationToken);
        if (sale is null || sale.Status is not ("completed" or "partially_returned"))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (!await OpenCashShiftIsValidAsync(connection, transaction, tenantId, sale.CashShiftId, storeId, terminalId, request.CreatedByUserId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        List<PreparedReturnLine> preparedLines = [];
        foreach (CreateReturnLineRequest requestedLine in request.Lines)
        {
            SaleLineForReturn? saleLine = await ReadSaleLineForReturnAsync(connection, transaction, tenantId, request.SaleId, requestedLine.SaleLineId, cancellationToken);
            if (saleLine is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            decimal quantity = decimal.Parse(requestedLine.Quantity, CultureInfo.InvariantCulture);
            decimal alreadyReturned = await ReadReturnedQuantityAsync(connection, transaction, tenantId, requestedLine.SaleLineId, cancellationToken);
            if (alreadyReturned + quantity > saleLine.Quantity)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            long lineTotalCents = ToCents((decimal)saleLine.TotalCents * quantity / saleLine.Quantity);
            preparedLines.Add(new PreparedReturnLine(saleLine, quantity, lineTotalCents));
        }

        long totalCents = preparedLines.Sum(x => x.TotalCents);
        long refundCents = request.Refunds.Sum(x => x.AmountCents);
        if (refundCents != totalCents)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        List<PreparedRefund> preparedRefunds = [];
        foreach (CreateReturnRefundRequest refund in request.Refunds)
        {
            PaymentMethod? method = await ReadPaymentMethodAsync(connection, transaction, tenantId, refund.MethodCode, cancellationToken);
            if (method is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            preparedRefunds.Add(new PreparedRefund(method, refund.AmountCents, refund.Reference));
        }

        Guid returnId = await InsertReturnAsync(connection, transaction, tenantId, sale, request, totalCents, refundCents, cancellationToken);

        foreach (PreparedReturnLine line in preparedLines)
        {
            await InsertReturnLineAsync(connection, transaction, tenantId, returnId, line, cancellationToken);
            await InsertInventoryCompensationAsync(connection, transaction, tenantId, returnId, sale, line, request, cancellationToken);
        }

        foreach (PreparedRefund refund in preparedRefunds)
        {
            await InsertRefundAsync(connection, transaction, tenantId, returnId, refund, cancellationToken);
        }

        long cashRefundCents = preparedRefunds.Where(x => x.Method.MethodType == "cash").Sum(x => x.AmountCents);
        if (cashRefundCents > 0)
        {
            await IncreaseExpectedCashAsync(connection, transaction, tenantId, sale.CashShiftId, -cashRefundCents, cancellationToken);
            await InsertCashRefundMovementAsync(connection, transaction, tenantId, sale.CashShiftId, request.CreatedByUserId, cashRefundCents, returnId, request.Reason, cancellationToken);
        }

        await UpdateSaleReturnStatusAsync(connection, transaction, tenantId, request.SaleId, cancellationToken);

        ReturnResponse? created = await ReadReturnByIdAsync(connection, transaction, tenantId, returnId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return created;
    }

    public async Task<ReturnResponse?> GetByIdAsync(Guid tenantId, Guid returnId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken, transaction);
        ReturnResponse? item = await ReadReturnByIdAsync(connection, transaction, tenantId, returnId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return item;
    }

    public async Task<IReadOnlyCollection<ReturnListItemResponse>> ListAsync(Guid tenantId, ReturnListFilters filters, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
              r.id, r.sale_id, s.store_id, s.terminal_id, r.cash_shift_id, r.local_return_id,
              r.status, r.reason, r.total_cents, r.refund_cents,
              (SELECT COUNT(*)::int FROM pos.return_lines rl WHERE rl.tenant_id = r.tenant_id AND rl.return_id = r.id) AS line_count,
              (SELECT COUNT(*)::int FROM pos.return_refunds rr WHERE rr.tenant_id = r.tenant_id AND rr.return_id = r.id) AS refund_count,
              r.occurred_at, r.created_at
            FROM pos.returns r
            JOIN pos.sales s ON s.tenant_id = r.tenant_id AND s.id = r.sale_id
            WHERE r.tenant_id = @tenant_id
              AND (@sale_id IS NULL OR r.sale_id = @sale_id)
              AND (@from IS NULL OR r.occurred_at >= @from)
              AND (@to IS NULL OR r.occurred_at <= @to)
            ORDER BY r.occurred_at DESC, r.created_at DESC
            LIMIT @limit;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken, transaction);

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        AddNullableGuid(command, "sale_id", filters.SaleId);
        AddNullableDateTimeOffset(command, "from", filters.From);
        AddNullableDateTimeOffset(command, "to", filters.To);
        command.Parameters.AddWithValue("limit", filters.Limit <= 0 ? 50 : filters.Limit);

        List<ReturnListItemResponse> items = [];
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new ReturnListItemResponse(
                    reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3), reader.GetGuid(4), reader.GetGuid(5),
                    reader.GetString(6), reader.GetString(7), reader.GetInt64(8), reader.GetInt64(9), reader.GetInt32(10), reader.GetInt32(11),
                    reader.GetFieldValue<DateTimeOffset>(12), reader.GetFieldValue<DateTimeOffset>(13)));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return items;
    }

    private static async Task<ReturnResponse?> ReadReturnByIdAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid returnId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT r.id, r.tenant_id, r.sale_id, s.store_id, s.terminal_id, r.cash_shift_id,
                   r.local_return_id, r.status, r.reason, r.subtotal_cents, r.tax_cents, r.total_cents,
                   r.refund_cents, r.created_by_user_id, r.occurred_at, r.created_at
            FROM pos.returns r
            JOIN pos.sales s ON s.tenant_id = r.tenant_id AND s.id = r.sale_id
            WHERE r.tenant_id = @tenant_id AND r.id = @return_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("return_id", returnId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var response = new ReturnHeader(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3), reader.GetGuid(4), reader.GetGuid(5),
            reader.GetGuid(6), reader.GetString(7), reader.GetString(8), reader.GetInt64(9), reader.GetInt64(10), reader.GetInt64(11),
            reader.GetInt64(12), reader.GetGuid(13), reader.GetFieldValue<DateTimeOffset>(14), reader.GetFieldValue<DateTimeOffset>(15));
        await reader.DisposeAsync();

        IReadOnlyCollection<ReturnLineResponse> lines = await ReadReturnLinesAsync(connection, transaction, tenantId, returnId, cancellationToken);
        IReadOnlyCollection<ReturnRefundResponse> refunds = await ReadReturnRefundsAsync(connection, transaction, tenantId, returnId, cancellationToken);
        IReadOnlyCollection<ReturnInventoryMovementResponse> movements = await ReadReturnInventoryMovementsAsync(connection, transaction, tenantId, returnId, cancellationToken);

        return new ReturnResponse(response.Id, response.TenantId, response.SaleId, response.StoreId, response.TerminalId, response.CashShiftId,
            response.LocalReturnId, response.Status, response.Reason, response.SubtotalCents, response.TaxCents, response.TotalCents,
            response.RefundCents, response.CreatedByUserId, response.OccurredAt, response.CreatedAt, lines, refunds, movements);
    }

    private static async Task<ReturnResponse?> ReadReturnByLocalIdAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid saleId, Guid localReturnId, CancellationToken cancellationToken)
    {
        const string sql = """SELECT id FROM pos.returns WHERE tenant_id = @tenant_id AND sale_id = @sale_id AND local_return_id = @local_return_id;""";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("sale_id", saleId);
        command.Parameters.AddWithValue("local_return_id", localReturnId);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid returnId ? await ReadReturnByIdAsync(connection, transaction, tenantId, returnId, cancellationToken) : null;
    }

    private static async Task<ReturnableSale?> ReadReturnableSaleAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid storeId, Guid terminalId, Guid saleId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, store_id, terminal_id, cash_shift_id, status, total_cents, occurred_at
            FROM pos.sales
            WHERE tenant_id = @tenant_id
              AND id = @sale_id
              AND store_id = @store_id
              AND terminal_id = @terminal_id
              AND deleted_at IS NULL;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("sale_id", saleId);
        command.Parameters.AddWithValue("store_id", storeId);
        command.Parameters.AddWithValue("terminal_id", terminalId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ReturnableSale(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3), reader.GetString(4), reader.GetInt64(5), reader.GetFieldValue<DateTimeOffset>(6))
            : null;
    }

    private static async Task<bool> OpenCashShiftIsValidAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid cashShiftId, Guid storeId, Guid terminalId, Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
              SELECT 1
              FROM pos.cash_shifts
              WHERE tenant_id = @tenant_id
                AND id = @cash_shift_id
                AND store_id = @store_id
                AND terminal_id = @terminal_id
                AND opened_by_user_id = @user_id
                AND status = 'open'
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("cash_shift_id", cashShiftId);
        command.Parameters.AddWithValue("store_id", storeId);
        command.Parameters.AddWithValue("terminal_id", terminalId);
        command.Parameters.AddWithValue("user_id", userId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<SaleLineForReturn?> ReadSaleLineForReturnAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid saleId, Guid saleLineId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, line_number, description, quantity, total_cents
            FROM pos.sale_lines
            WHERE tenant_id = @tenant_id AND sale_id = @sale_id AND id = @sale_line_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("sale_id", saleId);
        command.Parameters.AddWithValue("sale_line_id", saleLineId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new SaleLineForReturn(reader.GetGuid(0), reader.GetInt32(1), reader.GetString(2), reader.GetFieldValue<decimal>(3), reader.GetInt64(4))
            : null;
    }

    private static async Task<decimal> ReadReturnedQuantityAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid saleLineId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COALESCE(SUM(rl.quantity), 0)::numeric
            FROM pos.return_lines rl
            JOIN pos.returns r ON r.tenant_id = rl.tenant_id AND r.id = rl.return_id
            WHERE rl.tenant_id = @tenant_id
              AND rl.sale_line_id = @sale_line_id
              AND r.status = 'completed';
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("sale_line_id", saleLineId);
        return (decimal)(await command.ExecuteScalarAsync(cancellationToken) ?? 0m);
    }

    private static async Task<PaymentMethod?> ReadPaymentMethodAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, string code, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, code, method_type
            FROM pos.payment_methods
            WHERE tenant_id = @tenant_id AND code = @code AND status = 'active' AND deleted_at IS NULL;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("code", code.Trim());
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new PaymentMethod(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)) : null;
    }

    private static async Task<Guid> InsertReturnAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, ReturnableSale sale, CreateReturnRequest request, long totalCents, long refundCents, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.returns (
              tenant_id, sale_id, cash_shift_id, local_return_id, status, reason,
              subtotal_cents, tax_cents, total_cents, refund_cents, created_by_user_id, occurred_at
            ) VALUES (
              @tenant_id, @sale_id, @cash_shift_id, @local_return_id, 'completed', @reason,
              @subtotal_cents, 0, @total_cents, @refund_cents, @created_by_user_id, @occurred_at
            )
            RETURNING id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("sale_id", sale.Id);
        command.Parameters.AddWithValue("cash_shift_id", sale.CashShiftId);
        command.Parameters.AddWithValue("local_return_id", request.LocalReturnId);
        command.Parameters.AddWithValue("reason", request.Reason.Trim());
        command.Parameters.AddWithValue("subtotal_cents", totalCents);
        command.Parameters.AddWithValue("total_cents", totalCents);
        command.Parameters.AddWithValue("refund_cents", refundCents);
        command.Parameters.AddWithValue("created_by_user_id", request.CreatedByUserId);
        command.Parameters.AddWithValue("occurred_at", request.OccurredAt);
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException("Return insert did not return id."));
    }

    private static async Task InsertReturnLineAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid returnId, PreparedReturnLine line, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.return_lines (tenant_id, return_id, sale_line_id, quantity, total_cents)
            VALUES (@tenant_id, @return_id, @sale_line_id, @quantity, @total_cents);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("return_id", returnId);
        command.Parameters.AddWithValue("sale_line_id", line.SaleLine.Id);
        command.Parameters.AddWithValue("quantity", line.Quantity);
        command.Parameters.AddWithValue("total_cents", line.TotalCents);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertRefundAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid returnId, PreparedRefund refund, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.return_refunds (tenant_id, return_id, payment_method_id, method_code, method_type, amount_cents, currency, status, reference)
            VALUES (@tenant_id, @return_id, @payment_method_id, @method_code, @method_type, @amount_cents, 'MXN', 'approved', @reference);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("return_id", returnId);
        command.Parameters.AddWithValue("payment_method_id", refund.Method.Id);
        command.Parameters.AddWithValue("method_code", refund.Method.Code);
        command.Parameters.AddWithValue("method_type", refund.Method.MethodType);
        command.Parameters.AddWithValue("amount_cents", refund.AmountCents);
        command.Parameters.AddWithValue("reference", string.IsNullOrWhiteSpace(refund.Reference) ? (object)DBNull.Value : refund.Reference);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertInventoryCompensationAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid returnId, ReturnableSale sale, PreparedReturnLine line, CreateReturnRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.inventory_ledger (
              tenant_id, store_id, terminal_id, product_id, variant_id, movement_type,
              quantity_delta, unit_id, reference_type, reference_id, source_event_id,
              local_occurred_at, metadata
            )
            SELECT
              il.tenant_id,
              il.store_id,
              il.terminal_id,
              il.product_id,
              il.variant_id,
              'return',
              ABS(il.quantity_delta) * @ratio,
              il.unit_id,
              'return',
              @return_id,
              @return_id,
              @occurred_at,
              jsonb_build_object(
                'originalSaleId', @sale_id,
                'originalSaleLineId', @sale_line_id,
                'lineNumber', @line_number,
                'returnReason', @reason,
                'effect', COALESCE(il.metadata ->> 'effect', 'return_inventory_restock')
              )
            FROM pos.inventory_ledger il
            WHERE il.tenant_id = @tenant_id
              AND il.reference_type = 'sale'
              AND il.reference_id = @sale_id
              AND (il.metadata ->> 'lineNumber')::int = @line_number
              AND il.quantity_delta < 0;
            """;
        decimal ratio = line.Quantity / line.SaleLine.Quantity;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("return_id", returnId);
        command.Parameters.AddWithValue("sale_id", sale.Id);
        command.Parameters.AddWithValue("sale_line_id", line.SaleLine.Id);
        command.Parameters.AddWithValue("line_number", line.SaleLine.LineNumber);
        command.Parameters.AddWithValue("ratio", ratio);
        command.Parameters.AddWithValue("occurred_at", request.OccurredAt);
        command.Parameters.AddWithValue("reason", request.Reason.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertCashRefundMovementAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid cashShiftId, Guid userId, long amountCents, Guid returnId, string reason, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.cash_movements (
              tenant_id, cash_shift_id, movement_type, amount_cents, reason, authorized_by_user_id, created_by_user_id
            ) VALUES (
              @tenant_id, @cash_shift_id, 'cash_out', @amount_cents, @reason, @user_id, @user_id
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("cash_shift_id", cashShiftId);
        command.Parameters.AddWithValue("amount_cents", amountCents);
        command.Parameters.AddWithValue("reason", $"Return refund {returnId}: {reason.Trim()}");
        command.Parameters.AddWithValue("user_id", userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task IncreaseExpectedCashAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid cashShiftId, long cashDeltaCents, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pos.cash_shifts
            SET expected_cash_cents = expected_cash_cents + @cash_delta_cents,
                updated_at = now()
            WHERE tenant_id = @tenant_id AND id = @cash_shift_id AND status = 'open';
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("cash_shift_id", cashShiftId);
        command.Parameters.AddWithValue("cash_delta_cents", cashDeltaCents);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateSaleReturnStatusAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid saleId, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH sold AS (
              SELECT COALESCE(SUM(quantity), 0)::numeric AS quantity
              FROM pos.sale_lines
              WHERE tenant_id = @tenant_id AND sale_id = @sale_id
            ), returned AS (
              SELECT COALESCE(SUM(rl.quantity), 0)::numeric AS quantity
              FROM pos.return_lines rl
              JOIN pos.returns r ON r.tenant_id = rl.tenant_id AND r.id = rl.return_id
              WHERE rl.tenant_id = @tenant_id AND r.sale_id = @sale_id AND r.status = 'completed'
            )
            UPDATE pos.sales
            SET status = CASE WHEN returned.quantity >= sold.quantity THEN 'returned' ELSE 'partially_returned' END,
                updated_at = now(),
                version = version + 1
            FROM sold, returned
            WHERE tenant_id = @tenant_id AND id = @sale_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("sale_id", saleId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyCollection<ReturnLineResponse>> ReadReturnLinesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid returnId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT rl.id, rl.sale_line_id, sl.line_number, sl.description, rl.quantity, rl.total_cents
            FROM pos.return_lines rl
            JOIN pos.sale_lines sl ON sl.tenant_id = rl.tenant_id AND sl.id = rl.sale_line_id
            WHERE rl.tenant_id = @tenant_id AND rl.return_id = @return_id
            ORDER BY sl.line_number;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("return_id", returnId);
        List<ReturnLineResponse> items = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new ReturnLineResponse(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2), reader.GetString(3), reader.GetFieldValue<decimal>(4).ToString("0.####", CultureInfo.InvariantCulture), reader.GetInt64(5)));
        }
        return items;
    }

    private static async Task<IReadOnlyCollection<ReturnRefundResponse>> ReadReturnRefundsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid returnId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, method_code, method_type, amount_cents, currency::text, status, reference, created_at
            FROM pos.return_refunds
            WHERE tenant_id = @tenant_id AND return_id = @return_id
            ORDER BY created_at;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("return_id", returnId);
        List<ReturnRefundResponse> items = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new ReturnRefundResponse(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7)));
        }
        return items;
    }

    private static async Task<IReadOnlyCollection<ReturnInventoryMovementResponse>> ReadReturnInventoryMovementsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid returnId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT il.id, il.store_id, il.terminal_id, il.product_id, il.variant_id, p.sku, p.name,
                   il.movement_type, il.quantity_delta, il.unit_id, u.code,
                   il.metadata ->> 'effect',
                   NULLIF(il.metadata ->> 'originalSaleId', '')::uuid,
                   NULLIF(il.metadata ->> 'originalSaleLineId', '')::uuid,
                   il.local_occurred_at, il.created_at
            FROM pos.inventory_ledger il
            JOIN pos.products p ON p.tenant_id = il.tenant_id AND p.id = il.product_id
            LEFT JOIN pos.units u ON u.tenant_id = il.tenant_id AND u.id = il.unit_id
            WHERE il.tenant_id = @tenant_id AND il.reference_type = 'return' AND il.reference_id = @return_id
            ORDER BY il.created_at;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("return_id", returnId);
        List<ReturnInventoryMovementResponse> items = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new ReturnInventoryMovementResponse(
                reader.GetGuid(0), reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetGuid(2), reader.GetGuid(3), reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7),
                reader.GetFieldValue<decimal>(8).ToString("0.####", CultureInfo.InvariantCulture), reader.GetGuid(9), reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11), reader.IsDBNull(12) ? null : reader.GetGuid(12), reader.IsDBNull(13) ? null : reader.GetGuid(13),
                reader.GetFieldValue<DateTimeOffset>(14), reader.GetFieldValue<DateTimeOffset>(15)));
        }
        return items;
    }

    private static long ToCents(decimal value) => (long)Math.Round(value, 0, MidpointRounding.AwayFromZero);

    private static void AddNullableGuid(NpgsqlCommand command, string name, Guid? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.Uuid);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    private static void AddNullableDateTimeOffset(NpgsqlCommand command, string name, DateTimeOffset? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.TimestampTz);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    private sealed record ReturnableSale(Guid Id, Guid StoreId, Guid TerminalId, Guid CashShiftId, string Status, long TotalCents, DateTimeOffset OccurredAt);
    private sealed record SaleLineForReturn(Guid Id, int LineNumber, string Description, decimal Quantity, long TotalCents);
    private sealed record PreparedReturnLine(SaleLineForReturn SaleLine, decimal Quantity, long TotalCents);
    private sealed record PaymentMethod(Guid Id, string Code, string MethodType);
    private sealed record PreparedRefund(PaymentMethod Method, long AmountCents, string? Reference);
    private sealed record ReturnHeader(Guid Id, Guid TenantId, Guid SaleId, Guid StoreId, Guid TerminalId, Guid CashShiftId, Guid LocalReturnId, string Status, string Reason, long SubtotalCents, long TaxCents, long TotalCents, long RefundCents, Guid CreatedByUserId, DateTimeOffset OccurredAt, DateTimeOffset CreatedAt);
}
