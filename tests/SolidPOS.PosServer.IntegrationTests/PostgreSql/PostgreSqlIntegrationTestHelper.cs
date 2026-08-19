using Npgsql;

namespace SolidPOS.PosServer.IntegrationTests.PostgreSql;

internal static class PostgreSqlIntegrationTestHelper
{
    public static string? ConnectionString =>
        Environment.GetEnvironmentVariable("SOLIDPOS_TEST_POSTGRES");

    public static bool IsEnabled => !string.IsNullOrWhiteSpace(ConnectionString);

    public static string RepositoryRoot => FindRepositoryRoot();

    public static async Task ResetAndApplyMigrationsAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return;
        }

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await ExecuteSqlAsync(connection, "DROP SCHEMA IF EXISTS pos CASCADE;", cancellationToken);

        string[] files =
        [
            "001_initial_schema_postgresql.sql",
            "002_seed_permissions.sql",
            "003_seed_mvp_defaults.sql",
            "005_sync_push_runtime.sql",
            "006_sync_processing_runtime.sql",
            "007_modifier_inventory_semantics.sql",
            "008_digital_receipts_runtime.sql",
            "009_returns_refunds_runtime.sql",
            "010_customers_runtime.sql",
            "011_discounts_promotions_runtime.sql",
            "012_inventory_control_hardening.sql",
            "013_sync_conflict_resolution_runtime.sql",
            "014_builder_updates_runtime.sql",
            "015_security_auth_hardening.sql"
        ];

        foreach (string file in files)
        {
            string path = Path.Combine(RepositoryRoot, "database", "postgresql", file);
            string sql = await File.ReadAllTextAsync(path, cancellationToken);
            await ExecuteSqlAsync(connection, sql, cancellationToken);
        }
    }

    public static async Task<object?> ExecuteScalarAsync(string sql, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return null;
        }

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    public static async Task ExecuteSqlAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "database"))
                && Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
