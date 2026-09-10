using System.Diagnostics;
using Npgsql;
using NpgsqlTypes;

namespace SolidPOS.PosServer.Infrastructure.PostgreSql;

public sealed record PostgreSqlReadinessResult(
    bool IsAvailable,
    string Status,
    string Detail,
    string Database,
    string? ErrorCode = null,
    IReadOnlyCollection<string>? MissingTables = null,
    string? ConnectionStringSource = null,
    long? DatabaseLatencyMs = null,
    int? SchemaVersion = null,
    string? SyncContract = null,
    string? SchemaCompatibility = null,
    string? SyncReadiness = null,
    string? StorageReadiness = null,
    IReadOnlyCollection<PostgreSqlReadinessDependencyResult>? Dependencies = null);

public sealed record PostgreSqlReadinessDependencyResult(
    string Name,
    string Status,
    long? LatencyMs = null,
    string? Detail = null,
    string? ErrorCode = null);

public sealed class PostgreSqlReadinessProbe
{
    public const int ExpectedSchemaVersion = 4;
    public const string ExpectedSyncContract = "schema_version_4";
    private const long DatabaseLatencyDegradedThresholdMs = 1200;

    private static readonly string[] RequiredTables =
    [
        "pos.tenants",
        "pos.stores",
        "pos.users",
        "pos.sales",
        "pos.payments",
        "pos.inventory_ledger",
        "pos.sync_inbox_events",
        "pos.sync_conflicts",
        "pos.audit_events",
        "pos.builder_projects",
        "pos.update_releases",
        "pos.production_bootstrap_runs"
    ];

    private static readonly (string Table, string Column)[] RequiredSchemaColumns =
    [
        ("sync_inbox_events", "batch_id"),
        ("sync_inbox_events", "schema_version"),
        ("sync_inbox_events", "sequence_number"),
        ("sync_inbox_events", "attempts"),
        ("sync_inbox_events", "max_attempts"),
        ("sync_inbox_events", "next_retry_at"),
        ("sync_inbox_events", "dead_lettered_at"),
        ("sync_conflicts", "resolved_by_user_id"),
        ("sync_conflicts", "resolution_note"),
        ("sync_conflicts", "updated_at")
    ];

    private readonly PostgreSqlConnectionStringResolution _connectionStringResolution;

    public PostgreSqlReadinessProbe(PostgreSqlConnectionStringResolution connectionStringResolution)
    {
        _connectionStringResolution = connectionStringResolution;
    }

    public async Task<PostgreSqlReadinessResult> CheckAsync(CancellationToken cancellationToken)
    {
        PostgreSqlReadinessDependencyResult storage = CheckStorageReadiness();

        if (!_connectionStringResolution.IsConfigured)
        {
            return Unavailable(
                "PostgreSQL connection string is not configured.",
                "not_configured",
                _connectionStringResolution.ErrorCode ?? "POSTGRES_CONNECTION_STRING_MISSING",
                storage);
        }

        if (!_connectionStringResolution.IsValid || string.IsNullOrWhiteSpace(_connectionStringResolution.ConnectionString))
        {
            return Unavailable(
                "PostgreSQL connection string configuration is invalid.",
                "invalid_configuration",
                _connectionStringResolution.ErrorCode ?? "POSTGRES_CONNECTION_STRING_INVALID",
                storage);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            await using var connection = new NpgsqlConnection(_connectionStringResolution.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            const string diagnosticsSql = """
                SELECT required.table_name
                FROM unnest(@required_tables::text[]) AS required(table_name)
                WHERE to_regclass(required.table_name) IS NULL
                ORDER BY required.table_name;

                WITH required(table_name, column_name) AS (
                    VALUES
                        ('sync_inbox_events', 'batch_id'),
                        ('sync_inbox_events', 'schema_version'),
                        ('sync_inbox_events', 'sequence_number'),
                        ('sync_inbox_events', 'attempts'),
                        ('sync_inbox_events', 'max_attempts'),
                        ('sync_inbox_events', 'next_retry_at'),
                        ('sync_inbox_events', 'dead_lettered_at'),
                        ('sync_conflicts', 'resolved_by_user_id'),
                        ('sync_conflicts', 'resolution_note'),
                        ('sync_conflicts', 'updated_at')
                )
                SELECT required.table_name || '.' || required.column_name
                FROM required
                LEFT JOIN information_schema.columns c
                  ON c.table_schema = 'pos'
                 AND c.table_name = required.table_name
                 AND c.column_name = required.column_name
                WHERE c.column_name IS NULL
                ORDER BY required.table_name, required.column_name;
                """;

            List<string> missingTables = [];
            List<string> missingColumns = [];
            await using (var command = new NpgsqlCommand(diagnosticsSql, connection))
            {
                command.Parameters.AddWithValue("required_tables", NpgsqlDbType.Array | NpgsqlDbType.Text, RequiredTables);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    missingTables.Add(reader.GetString(0));
                }

                await reader.NextResultAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    missingColumns.Add(reader.GetString(0));
                }
            }

            stopwatch.Stop();
            long latencyMs = stopwatch.ElapsedMilliseconds;
            bool schemaCompatible = missingTables.Count == 0 && missingColumns.Count == 0;
            bool syncReady = schemaCompatible
                && RequiredSchemaColumns.All(required =>
                    !missingColumns.Contains($"{required.Table}.{required.Column}", StringComparer.Ordinal));

            PostgreSqlReadinessDependencyResult database = new(
                "database",
                latencyMs > DatabaseLatencyDegradedThresholdMs ? "degraded" : "ready",
                latencyMs,
                latencyMs > DatabaseLatencyDegradedThresholdMs
                    ? "PostgreSQL responded above the approved readiness latency threshold."
                    : "PostgreSQL connection and catalog query ready.");

            PostgreSqlReadinessDependencyResult schema = schemaCompatible
                ? new("schema", "ready", Detail: $"Runtime schema is compatible with schemaVersion {ExpectedSchemaVersion}.")
                : new("schema", "unavailable", Detail: BuildSchemaFailureDetail(missingTables, missingColumns), ErrorCode: "POSTGRES_SCHEMA_INCOMPATIBLE");

            PostgreSqlReadinessDependencyResult sync = syncReady
                ? new("sync", "ready", Detail: $"Sync persistence contract {ExpectedSyncContract} is available.")
                : new("sync", "unavailable", Detail: "Required sync persistence structures are unavailable.", ErrorCode: "SYNC_STORAGE_SCHEMA_INCOMPATIBLE");

            string overallStatus = !schemaCompatible || !syncReady
                ? "unavailable"
                : database.Status == "degraded" || storage.Status == "degraded"
                    ? "degraded"
                    : "ready";

            bool isAvailable = overallStatus != "unavailable";
            string detail = overallStatus switch
            {
                "ready" => "All production readiness dependencies are ready.",
                "degraded" => "Service is available but one or more readiness dependencies are degraded.",
                _ => BuildSchemaFailureDetail(missingTables, missingColumns)
            };

            return new PostgreSqlReadinessResult(
                isAvailable,
                overallStatus,
                detail,
                schemaCompatible ? "ready" : "missing_migrations",
                schemaCompatible ? null : "POSTGRES_SCHEMA_INCOMPATIBLE",
                missingTables.Count == 0 ? null : missingTables,
                _connectionStringResolution.Source,
                latencyMs,
                schemaCompatible ? ExpectedSchemaVersion : null,
                schemaCompatible ? ExpectedSyncContract : null,
                schemaCompatible ? "compatible" : "incompatible",
                syncReady ? "ready" : "unavailable",
                storage.Status,
                [database, schema, sync, storage]);
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException or InvalidOperationException or ArgumentException or FormatException)
        {
            stopwatch.Stop();
            return Unavailable(
                "PostgreSQL readiness check failed.",
                "unavailable",
                "POSTGRES_READINESS_CHECK_FAILED",
                storage,
                stopwatch.ElapsedMilliseconds);
        }
    }

    private PostgreSqlReadinessResult Unavailable(
        string detail,
        string databaseState,
        string errorCode,
        PostgreSqlReadinessDependencyResult storage,
        long? latencyMs = null)
    {
        var database = new PostgreSqlReadinessDependencyResult("database", "unavailable", latencyMs, detail, errorCode);
        var schema = new PostgreSqlReadinessDependencyResult("schema", "unavailable", Detail: "Schema compatibility could not be verified.", ErrorCode: "SCHEMA_COMPATIBILITY_NOT_VERIFIED");
        var sync = new PostgreSqlReadinessDependencyResult("sync", "unavailable", Detail: "Sync readiness could not be verified.", ErrorCode: "SYNC_READINESS_NOT_VERIFIED");

        return new PostgreSqlReadinessResult(
            false,
            "unavailable",
            detail,
            databaseState,
            errorCode,
            ConnectionStringSource: _connectionStringResolution.Source,
            DatabaseLatencyMs: latencyMs,
            SchemaCompatibility: "unknown",
            SyncReadiness: "unknown",
            StorageReadiness: storage.Status,
            Dependencies: [database, schema, sync, storage]);
    }

    private static PostgreSqlReadinessDependencyResult CheckStorageReadiness()
    {
        string? probeFile = null;
        try
        {
            probeFile = Path.Combine(Path.GetTempPath(), $"solidpos-readiness-{Guid.NewGuid():N}.tmp");
            using (File.Create(probeFile, 1, FileOptions.DeleteOnClose))
            {
            }

            return new PostgreSqlReadinessDependencyResult("storage", "ready", Detail: "Runtime temporary storage is writable.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new PostgreSqlReadinessDependencyResult("storage", "degraded", Detail: "Runtime temporary storage write probe failed.", ErrorCode: "RUNTIME_STORAGE_WRITE_FAILED");
        }
        finally
        {
            if (probeFile is not null && File.Exists(probeFile))
            {
                try
                {
                    File.Delete(probeFile);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static string BuildSchemaFailureDetail(IReadOnlyCollection<string> missingTables, IReadOnlyCollection<string> missingColumns)
    {
        List<string> problems = [];
        if (missingTables.Count > 0)
        {
            problems.Add($"missing required tables: {string.Join(", ", missingTables)}");
        }

        if (missingColumns.Count > 0)
        {
            problems.Add($"missing required schema columns: {string.Join(", ", missingColumns)}");
        }

        return problems.Count == 0
            ? "Runtime schema is incompatible with the expected contract."
            : $"PostgreSQL connected but runtime schema is incompatible ({string.Join("; ", problems)}).";
    }
}
