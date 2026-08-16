using Microsoft.Extensions.Configuration;
using Npgsql;
using SolidPOS.PosServer.Application.Observability;
using SolidPOS.PosServer.Contracts.Observability;

namespace SolidPOS.PosServer.Infrastructure.Observability;

public sealed class PostgreSqlOperationalMetricsRepository : IOperationalMetricsRepository
{
    private static readonly string[] RequiredTables =
    [
        "pos.tenants",
        "pos.stores",
        "pos.users",
        "pos.sales",
        "pos.payments",
        "pos.inventory_ledger",
        "pos.sync_inbox_events",
        "pos.audit_events",
        "pos.builder_projects",
        "pos.update_releases"
    ];

    private readonly string? _connectionString;

    public PostgreSqlOperationalMetricsRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres");
    }

    public async Task<DatabaseMetricsResponse> GetDatabaseMetricsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            return new DatabaseMetricsResponse(false, string.Empty, string.Empty, 0, false, RequiredTables);
        }

        await using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);

        string databaseName = connection.Database;
        string serverVersion = connection.PostgreSqlVersion.ToString();
        int activeConnections = await ScalarAsync<int>(connection, null, "SELECT count(*)::int FROM pg_stat_activity WHERE datname = current_database();", cancellationToken);

        List<string> missingTables = [];
        foreach (string table in RequiredTables)
        {
            await using NpgsqlCommand command = new("SELECT to_regclass(@table_name) IS NOT NULL;", connection);
            command.Parameters.AddWithValue("table_name", table);
            bool exists = Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
            if (!exists)
            {
                missingTables.Add(table);
            }
        }

        return new DatabaseMetricsResponse(true, databaseName, serverVersion, activeConnections, missingTables.Count == 0, missingTables);
    }

    public async Task<SyncMetricsResponse> GetSyncMetricsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);

        Dictionary<string, long> inboxByStatus = new(StringComparer.OrdinalIgnoreCase);
        await using (NpgsqlCommand command = new("""
            SELECT status, count(*)::bigint
            FROM pos.sync_inbox_events
            WHERE tenant_id = @tenant_id
            GROUP BY status;
            """, connection))
        {
            command.Parameters.AddWithValue("tenant_id", tenantId);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                inboxByStatus[reader.GetString(0)] = reader.GetInt64(1);
            }
        }

        long pendingConflicts = await ScalarTenantAsync<long>(connection, tenantId,
            "SELECT count(*)::bigint FROM pos.sync_conflicts WHERE tenant_id = @tenant_id AND status = 'pending';", cancellationToken);
        long resolvedConflicts = await ScalarTenantAsync<long>(connection, tenantId,
            "SELECT count(*)::bigint FROM pos.sync_conflicts WHERE tenant_id = @tenant_id AND status = 'resolved';", cancellationToken);
        long deadLetters = inboxByStatus.TryGetValue("dead_letter", out long dlq) ? dlq : 0;
        long retryPending = inboxByStatus.TryGetValue("retry_pending", out long retry) ? retry : 0;

        return new SyncMetricsResponse(inboxByStatus, pendingConflicts, resolvedConflicts, deadLetters, retryPending);
    }

    public async Task<SalesLatencyMetricsResponse> GetSalesMetricsAsync(Guid tenantId, double apiAverageLatencyMs, double apiP95LatencyMs, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using NpgsqlCommand command = new("""
            SELECT
              count(*)::bigint,
              COALESCE(avg(EXTRACT(EPOCH FROM (created_at - occurred_at)) * 1000), 0)::double precision
            FROM pos.sales
            WHERE tenant_id = @tenant_id
              AND created_at >= now() - interval '24 hours';
            """, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new SalesLatencyMetricsResponse(reader.GetInt64(0), Math.Round(reader.GetDouble(1), 2), Math.Round(apiAverageLatencyMs, 2), Math.Round(apiP95LatencyMs, 2));
    }

    public async Task<PaymentMetricsResponse> GetPaymentMetricsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        long declined = await ScalarTenantAsync<long>(connection, tenantId,
            "SELECT count(*)::bigint FROM pos.payments WHERE tenant_id = @tenant_id AND status = 'declined' AND created_at >= now() - interval '24 hours';", cancellationToken);
        long failed = await ScalarTenantAsync<long>(connection, tenantId,
            "SELECT count(*)::bigint FROM pos.payments WHERE tenant_id = @tenant_id AND status IN ('declined', 'voided') AND created_at >= now() - interval '24 hours';", cancellationToken);
        return new PaymentMetricsResponse(failed, declined);
    }

    public async Task<InventoryRiskMetricsResponse> GetInventoryMetricsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        long negative = await ScalarTenantAsync<long>(connection, tenantId, """
            SELECT count(*)::bigint
            FROM (
              SELECT store_id, product_id, variant_id, sum(quantity_delta) AS quantity_on_hand
              FROM pos.inventory_ledger
              WHERE tenant_id = @tenant_id
              GROUP BY store_id, product_id, variant_id
              HAVING sum(quantity_delta) < 0
            ) stock;
            """, cancellationToken);

        long lowStock = await ScalarTenantAsync<long>(connection, tenantId, """
            SELECT count(*)::bigint
            FROM (
              SELECT l.store_id, l.product_id, l.variant_id, sum(l.quantity_delta) AS quantity_on_hand, max(t.reorder_point) AS reorder_point
              FROM pos.inventory_ledger l
              LEFT JOIN pos.inventory_low_stock_thresholds t
                ON t.tenant_id = l.tenant_id
               AND t.store_id = l.store_id
               AND t.product_id = l.product_id
               AND (t.variant_id IS NOT DISTINCT FROM l.variant_id)
              WHERE l.tenant_id = @tenant_id
              GROUP BY l.store_id, l.product_id, l.variant_id
              HAVING sum(l.quantity_delta) >= 0 AND max(t.reorder_point) IS NOT NULL AND sum(l.quantity_delta) <= max(t.reorder_point)
            ) stock;
            """, cancellationToken);

        return new InventoryRiskMetricsResponse(negative, lowStock);
    }

    public async Task<AuditTrailMetricsResponse> GetAuditMetricsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using NpgsqlCommand command = new("""
            SELECT
              count(*)::bigint,
              max(occurred_at)
            FROM pos.audit_events
            WHERE tenant_id = @tenant_id
              AND occurred_at >= now() - interval '24 hours';
            """, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        DateTimeOffset? lastAudit = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1);
        return new AuditTrailMetricsResponse(reader.GetInt64(0), lastAudit);
    }

    private async Task<NpgsqlConnection> OpenTenantConnectionAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:Postgres is required.");
        }

        NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlCommand command = new("SELECT set_config('app.tenant_id', @tenant_id, false);", connection);
        command.Parameters.AddWithValue("tenant_id", tenantId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static async Task<T> ScalarTenantAsync<T>(NpgsqlConnection connection, Guid tenantId, string sql, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return (T)Convert.ChangeType(result ?? 0, typeof(T));
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = new(sql, connection, transaction);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return (T)Convert.ChangeType(result ?? 0, typeof(T));
    }
}
