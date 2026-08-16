using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using SolidPOS.PosServer.Application.Customers;
using SolidPOS.PosServer.Contracts.Customers;
using SolidPOS.PosServer.Infrastructure.PostgreSql;

namespace SolidPOS.PosServer.Infrastructure.Customers;

public sealed class PostgreSqlCustomersRepository : ICustomersRepository
{
    private readonly string _connectionString;

    public PostgreSqlCustomersRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    }

    public async Task<CustomerResponse?> CreateAsync(Guid tenantId, CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken, transaction);

        if (!await TenantIsActiveAsync(connection, transaction, tenantId, cancellationToken) ||
            await EmailConflictsAsync(connection, transaction, tenantId, null, request.Email, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        Guid customerId = Guid.NewGuid();
        const string sql = """
            INSERT INTO pos.customers (
              id,
              tenant_id,
              name,
              email,
              phone,
              credit_limit_cents,
              status,
              attributes
            )
            VALUES (
              @id,
              @tenant_id,
              @name,
              @email,
              @phone,
              @credit_limit_cents,
              'active',
              '{}'::jsonb
            );
            """;

        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("id", customerId);
            command.Parameters.AddWithValue("tenant_id", tenantId);
            command.Parameters.AddWithValue("name", request.Name);
            AddNullableText(command, "email", request.Email);
            AddNullableText(command, "phone", request.Phone);
            command.Parameters.AddWithValue("credit_limit_cents", request.CreditLimitCents);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        CustomerResponse? created = await ReadCustomerByIdAsync(connection, transaction, tenantId, customerId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return created;
    }

    public async Task<CustomerResponse?> GetByIdAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);
        return await ReadCustomerByIdAsync(connection, null, tenantId, customerId, cancellationToken);
    }

    public async Task<CustomerResponse?> UpdateAsync(Guid tenantId, Guid customerId, UpdateCustomerRequest request, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken, transaction);

        CustomerResponse? current = await ReadCustomerByIdAsync(connection, transaction, tenantId, customerId, cancellationToken);
        if (current is null || await EmailConflictsAsync(connection, transaction, tenantId, customerId, request.Email, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        string name = request.Name ?? current.Name;
        string? email = request.Email ?? current.Email;
        string? phone = request.Phone ?? current.Phone;
        long creditLimitCents = request.CreditLimitCents ?? current.CreditLimitCents;
        string status = request.Status ?? current.Status;

        const string sql = """
            UPDATE pos.customers
            SET name = @name,
                email = @email,
                phone = @phone,
                credit_limit_cents = @credit_limit_cents,
                status = @status,
                updated_at = now()
            WHERE tenant_id = @tenant_id
              AND id = @id
              AND deleted_at IS NULL;
            """;

        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("tenant_id", tenantId);
            command.Parameters.AddWithValue("id", customerId);
            command.Parameters.AddWithValue("name", name);
            AddNullableText(command, "email", email);
            AddNullableText(command, "phone", phone);
            command.Parameters.AddWithValue("credit_limit_cents", creditLimitCents);
            command.Parameters.AddWithValue("status", status);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        CustomerResponse? updated = await ReadCustomerByIdAsync(connection, transaction, tenantId, customerId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return updated;
    }

    public async Task<IReadOnlyCollection<CustomerListItemResponse>> ListAsync(Guid tenantId, CustomerListFilters filters, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        const string sql = """
            SELECT
              c.id,
              c.tenant_id,
              c.name,
              c.email::text,
              c.phone,
              c.credit_limit_cents,
              c.status,
              COALESCE(stats.sales_count, 0)::bigint AS sales_count,
              COALESCE(stats.gross_sales_cents, 0)::bigint AS gross_sales_cents,
              COALESCE(stats.refund_cents, 0)::bigint AS refund_cents,
              COALESCE(stats.net_spent_cents, 0)::bigint AS net_spent_cents,
              CASE WHEN COALESCE(stats.sales_count, 0) = 0 THEN 0 ELSE (COALESCE(stats.net_spent_cents, 0) / stats.sales_count)::bigint END AS average_ticket_cents,
              stats.last_purchase_at,
              c.created_at,
              c.updated_at
            FROM pos.customers c
            LEFT JOIN LATERAL (
              SELECT
                COUNT(*)::bigint AS sales_count,
                COALESCE(SUM(s.total_cents), 0)::bigint AS gross_sales_cents,
                COALESCE(SUM(COALESCE(r.refund_cents, 0)), 0)::bigint AS refund_cents,
                COALESCE(SUM(s.total_cents - COALESCE(r.refund_cents, 0)), 0)::bigint AS net_spent_cents,
                MAX(s.occurred_at) AS last_purchase_at
              FROM pos.sales s
              LEFT JOIN (
                SELECT tenant_id, sale_id, SUM(refund_cents)::bigint AS refund_cents
                FROM pos.returns
                WHERE tenant_id = @tenant_id
                  AND status = 'completed'
                GROUP BY tenant_id, sale_id
              ) r ON r.tenant_id = s.tenant_id AND r.sale_id = s.id
              WHERE s.tenant_id = c.tenant_id
                AND s.customer_id = c.id
                AND s.status IN ('completed', 'partially_returned', 'returned')
                AND s.deleted_at IS NULL
            ) stats ON true
            WHERE c.tenant_id = @tenant_id
              AND c.deleted_at IS NULL
              AND (@status IS NULL OR c.status = @status)
              AND (
                @search IS NULL OR
                c.name ILIKE '%' || @search || '%' OR
                c.phone ILIKE '%' || @search || '%' OR
                c.email::text ILIKE '%' || @search || '%'
              )
            ORDER BY c.updated_at DESC, c.created_at DESC
            LIMIT @limit;
            """;

        List<CustomerListItemResponse> items = [];
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        AddNullableText(command, "search", filters.Search);
        AddNullableText(command, "status", filters.Status);
        command.Parameters.AddWithValue("limit", filters.Limit);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CustomerListItemResponse(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt64(5),
                reader.GetString(6),
                reader.GetInt64(7),
                reader.GetInt64(8),
                reader.GetInt64(9),
                reader.GetInt64(10),
                reader.GetInt64(11),
                reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
                reader.GetFieldValue<DateTimeOffset>(13),
                reader.GetFieldValue<DateTimeOffset>(14)));
        }

        return items;
    }

    public async Task<CustomerSalesHistoryResponse?> GetSalesAsync(Guid tenantId, Guid customerId, CustomerSalesFilters filters, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        CustomerResponse? customer = await ReadCustomerByIdAsync(connection, null, tenantId, customerId, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        const string sql = """
            SELECT
              s.id,
              s.store_id,
              s.terminal_id,
              s.cash_shift_id,
              s.status,
              s.total_cents,
              s.paid_cents,
              s.change_cents,
              COALESCE(r.refund_cents, 0)::bigint AS refund_cents,
              (s.total_cents - COALESCE(r.refund_cents, 0))::bigint AS net_after_returns_cents,
              s.currency::text,
              s.occurred_at,
              s.created_at
            FROM pos.sales s
            LEFT JOIN (
              SELECT tenant_id, sale_id, SUM(refund_cents)::bigint AS refund_cents
              FROM pos.returns
              WHERE tenant_id = @tenant_id
                AND status = 'completed'
              GROUP BY tenant_id, sale_id
            ) r ON r.tenant_id = s.tenant_id AND r.sale_id = s.id
            WHERE s.tenant_id = @tenant_id
              AND s.customer_id = @customer_id
              AND s.status IN ('completed', 'partially_returned', 'returned')
              AND s.deleted_at IS NULL
              AND (@from IS NULL OR s.occurred_at >= @from)
              AND (@to IS NULL OR s.occurred_at <= @to)
            ORDER BY s.occurred_at DESC, s.created_at DESC
            LIMIT @limit;
            """;

        List<CustomerSaleHistoryItemResponse> sales = [];
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("customer_id", customerId);
        AddNullableTimestamp(command, "from", filters.From);
        AddNullableTimestamp(command, "to", filters.To);
        command.Parameters.AddWithValue("limit", filters.Limit);

        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                sales.Add(new CustomerSaleHistoryItemResponse(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetGuid(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3),
                    reader.GetString(4),
                    reader.GetInt64(5),
                    reader.GetInt64(6),
                    reader.GetInt64(7),
                    reader.GetInt64(8),
                    reader.GetInt64(9),
                    reader.GetString(10),
                    reader.GetFieldValue<DateTimeOffset>(11),
                    reader.GetFieldValue<DateTimeOffset>(12)));
            }
        }

        long salesCount = sales.Count;
        long grossSalesCents = sales.Sum(x => x.TotalCents);
        long refundCents = sales.Sum(x => x.RefundCents);
        long netSpentCents = sales.Sum(x => x.NetAfterReturnsCents);
        long averageTicketCents = salesCount == 0 ? 0 : netSpentCents / salesCount;
        DateTimeOffset? lastPurchaseAt = sales.Count == 0 ? null : sales.Max(x => x.OccurredAt);

        return new CustomerSalesHistoryResponse(
            customer,
            new CustomerSalesSummaryResponse(salesCount, grossSalesCents, refundCents, netSpentCents, averageTicketCents, lastPurchaseAt),
            sales);
    }

    private static async Task<CustomerResponse?> ReadCustomerByIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid tenantId,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
              id,
              tenant_id,
              name,
              email::text,
              phone,
              credit_limit_cents,
              status,
              created_at,
              updated_at
            FROM pos.customers
            WHERE tenant_id = @tenant_id
              AND id = @id
              AND deleted_at IS NULL;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("id", customerId);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapCustomer(reader);
    }

    private static CustomerResponse MapCustomer(NpgsqlDataReader reader)
    {
        return new CustomerResponse(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetInt64(5),
            reader.GetString(6),
            reader.GetFieldValue<DateTimeOffset>(7),
            reader.GetFieldValue<DateTimeOffset>(8));
    }

    private static async Task<bool> TenantIsActiveAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
              SELECT 1
              FROM pos.tenants
              WHERE id = @tenant_id
                AND status = 'active'
                AND deleted_at IS NULL
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool isActive && isActive;
    }

    private static async Task<bool> EmailConflictsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid? currentCustomerId,
        string? email,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        const string sql = """
            SELECT EXISTS (
              SELECT 1
              FROM pos.customers
              WHERE tenant_id = @tenant_id
                AND lower(email::text) = lower(@email)
                AND deleted_at IS NULL
                AND (@current_customer_id IS NULL OR id <> @current_customer_id)
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("email", email);
        AddNullableUuid(command, "current_customer_id", currentCustomerId);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool conflicts && conflicts;
    }

    private static void AddNullableText(NpgsqlCommand command, string name, string? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.Text);
        parameter.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }

    private static void AddNullableTimestamp(NpgsqlCommand command, string name, DateTimeOffset? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.TimestampTz);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    private static void AddNullableUuid(NpgsqlCommand command, string name, Guid? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.Uuid);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }
}
