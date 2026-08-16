using Npgsql;

namespace SolidPOS.PosServer.Infrastructure.PostgreSql;

public sealed class PostgreSqlReadinessProbe
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

    public PostgreSqlReadinessProbe(string? connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<(bool IsReady, string Detail)> CheckAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            return (false, "Connection string 'Postgres' is not configured.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using (var command = new NpgsqlCommand("SELECT 1;", connection))
            {
                object? result = await command.ExecuteScalarAsync(cancellationToken);
                if (Convert.ToInt32(result) != 1)
                {
                    return (false, "PostgreSQL readiness query returned an unexpected result.");
                }
            }

            List<string> missingTables = [];
            foreach (string table in RequiredTables)
            {
                await using var tableCommand = new NpgsqlCommand("SELECT to_regclass(@table_name) IS NOT NULL;", connection);
                tableCommand.Parameters.AddWithValue("table_name", table);
                bool exists = Convert.ToBoolean(await tableCommand.ExecuteScalarAsync(cancellationToken));
                if (!exists)
                {
                    missingTables.Add(table);
                }
            }

            if (missingTables.Count > 0)
            {
                return (false, $"PostgreSQL connected but required tables are missing: {string.Join(", ", missingTables)}.");
            }

            return (true, "PostgreSQL connection and required runtime tables ready.");
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            return (false, ex.Message);
        }
    }
}
