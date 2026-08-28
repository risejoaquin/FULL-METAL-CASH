using Npgsql;
using NpgsqlTypes;

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
        "pos.update_releases",
        "pos.production_bootstrap_runs"
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

            // A successful OpenAsync plus this single catalog query proves database connectivity
            // and validates every required runtime table in one round trip. The previous readiness
            // implementation issued SELECT 1 plus one to_regclass query per table (12 commands),
            // which amplified connection/CPU pressure during concurrent readiness probes.
            const string requiredTablesSql = """
                SELECT required.table_name
                FROM unnest(@required_tables::text[]) AS required(table_name)
                WHERE to_regclass(required.table_name) IS NULL
                ORDER BY required.table_name;
                """;

            List<string> missingTables = [];
            await using (var command = new NpgsqlCommand(requiredTablesSql, connection))
            {
                command.Parameters.AddWithValue("required_tables", NpgsqlDbType.Array | NpgsqlDbType.Text, RequiredTables);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    missingTables.Add(reader.GetString(0));
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
