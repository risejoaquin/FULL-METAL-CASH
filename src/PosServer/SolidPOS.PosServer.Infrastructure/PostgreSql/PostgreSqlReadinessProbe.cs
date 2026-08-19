using Npgsql;

namespace SolidPOS.PosServer.Infrastructure.PostgreSql;

public sealed record PostgreSqlReadinessResult(
    bool IsReady,
    string Detail,
    string Database,
    string? ErrorCode = null,
    IReadOnlyCollection<string>? MissingTables = null,
    string? ConnectionStringSource = null);

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

    private readonly PostgreSqlConnectionStringResolution _connectionStringResolution;

    public PostgreSqlReadinessProbe(PostgreSqlConnectionStringResolution connectionStringResolution)
    {
        _connectionStringResolution = connectionStringResolution;
    }

    public async Task<PostgreSqlReadinessResult> CheckAsync(CancellationToken cancellationToken)
    {
        if (!_connectionStringResolution.IsConfigured)
        {
            return new PostgreSqlReadinessResult(
                false,
                _connectionStringResolution.ErrorMessage ?? "PostgreSQL connection string is not configured.",
                "not_configured",
                _connectionStringResolution.ErrorCode ?? "POSTGRES_CONNECTION_STRING_MISSING",
                ConnectionStringSource: _connectionStringResolution.Source);
        }

        if (!_connectionStringResolution.IsValid || string.IsNullOrWhiteSpace(_connectionStringResolution.ConnectionString))
        {
            return new PostgreSqlReadinessResult(
                false,
                _connectionStringResolution.ErrorMessage ?? "PostgreSQL connection string is invalid.",
                "invalid_configuration",
                _connectionStringResolution.ErrorCode ?? "POSTGRES_CONNECTION_STRING_INVALID",
                ConnectionStringSource: _connectionStringResolution.Source);
        }

        try
        {
            await using var connection = new NpgsqlConnection(_connectionStringResolution.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using (var command = new NpgsqlCommand("SELECT 1;", connection))
            {
                object? result = await command.ExecuteScalarAsync(cancellationToken);
                if (Convert.ToInt32(result) != 1)
                {
                    return new PostgreSqlReadinessResult(
                        false,
                        "PostgreSQL readiness query returned an unexpected result.",
                        "unavailable",
                        "POSTGRES_READINESS_QUERY_FAILED",
                        ConnectionStringSource: _connectionStringResolution.Source);
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
                return new PostgreSqlReadinessResult(
                    false,
                    $"PostgreSQL connected but required tables are missing: {string.Join(", ", missingTables)}.",
                    "missing_migrations",
                    "POSTGRES_REQUIRED_TABLES_MISSING",
                    missingTables,
                    _connectionStringResolution.Source);
            }

            return new PostgreSqlReadinessResult(
                true,
                "PostgreSQL connection and required runtime tables ready.",
                "ready",
                ConnectionStringSource: _connectionStringResolution.Source);
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException or InvalidOperationException or ArgumentException or FormatException)
        {
            return new PostgreSqlReadinessResult(
                false,
                ex.Message,
                "unavailable",
                "POSTGRES_READINESS_CHECK_FAILED",
                ConnectionStringSource: _connectionStringResolution.Source);
        }
    }
}
