using System.Text.Json;
using System.Text.Json.Serialization;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.Diagnostics;

public sealed record SupportBundleSanitizationMetadata(
    [property: JsonPropertyName("redactedSecrets")] bool RedactedSecrets,
    [property: JsonPropertyName("redactedPii")] bool RedactedPii,
    [property: JsonPropertyName("sanitizer")] string Sanitizer);

public sealed record SupportBundleManifest(
    [property: JsonPropertyName("bundleFormatVersion")] string BundleFormatVersion,
    [property: JsonPropertyName("generatedAtUtc")] DateTimeOffset GeneratedAtUtc,
    [property: JsonPropertyName("applicationVersion")] string ApplicationVersion,
    [property: JsonPropertyName("includedSections")] IReadOnlyList<string> IncludedSections,
    [property: JsonPropertyName("isSanitized")] bool IsSanitized,
    [property: JsonPropertyName("sanitization")] SupportBundleSanitizationMetadata Sanitization);

public sealed record SupportBundleRuntimeInfo(
    [property: JsonPropertyName("applicationVersion")] string ApplicationVersion,
    [property: JsonPropertyName("runtimeVersion")] string RuntimeVersion,
    [property: JsonPropertyName("dotnetVersion")] string DotNetVersion,
    [property: JsonPropertyName("osDescription")] string OsDescription,
    [property: JsonPropertyName("processArchitecture")] string ProcessArchitecture,
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("syncContract")] string SyncContract);

public sealed record SupportBundleSqliteInfo(
    [property: JsonPropertyName("isAvailable")] bool IsAvailable,
    [property: JsonPropertyName("databasePath")] string DatabasePath,
    [property: JsonPropertyName("databaseFileName")] string DatabaseFileName,
    [property: JsonPropertyName("sqliteIntegrityCheck")] string SqliteIntegrityCheck,
    [property: JsonPropertyName("isOk")] bool IsOk,
    [property: JsonPropertyName("walPresent")] bool WalPresent,
    [property: JsonPropertyName("journalPresent")] bool JournalPresent,
    [property: JsonPropertyName("shmPresent")] bool ShmPresent,
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("isSchemaCompatible")] bool IsSchemaCompatible,
    [property: JsonPropertyName("syncContract")] string SyncContract,
    [property: JsonPropertyName("pendingOutboxEvents")] int PendingOutboxEvents,
    [property: JsonPropertyName("failedOutboxEvents")] int FailedOutboxEvents,
    [property: JsonPropertyName("deadLetterOutboxEvents")] int DeadLetterOutboxEvents,
    [property: JsonPropertyName("pendingPrintJobs")] int PendingPrintJobs,
    [property: JsonPropertyName("failedPrintJobs")] int FailedPrintJobs,
    [property: JsonPropertyName("openCashShifts")] int OpenCashShifts,
    [property: JsonPropertyName("activeSessions")] int ActiveSessions,
    [property: JsonPropertyName("recoveryJournalEntries")] int RecoveryJournalEntries,
    [property: JsonPropertyName("issues")] IReadOnlyList<LocalIntegrityIssue> Issues);

public sealed record SupportBundleSyncInfo(
    [property: JsonPropertyName("PendingCount")] int PendingCount,
    [property: JsonPropertyName("ProcessingCount")] int ProcessingCount,
    [property: JsonPropertyName("RetryPendingCount")] int RetryPendingCount,
    [property: JsonPropertyName("DeadLetterCount")] int DeadLetterCount,
    [property: JsonPropertyName("OldestPendingAtUtc")] DateTimeOffset? OldestPendingAtUtc,
    [property: JsonPropertyName("OldestRetryPendingAtUtc")] DateTimeOffset? OldestRetryPendingAtUtc,
    [property: JsonPropertyName("HasStuckProcessing")] bool HasStuckProcessing,
    [property: JsonPropertyName("RequiresRecovery")] bool RequiresRecovery,
    [property: JsonPropertyName("lastSuccessfulPullUtc")] DateTimeOffset? LastSuccessfulPullUtc,
    [property: JsonPropertyName("lastSuccessfulPushUtc")] DateTimeOffset? LastSuccessfulPushUtc,
    [property: JsonPropertyName("lastSuccessfulSyncUtc")] DateTimeOffset? LastSuccessfulSyncUtc,
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("syncContract")] string SyncContract);

public sealed record LocalPrinterStatus(
    [property: JsonPropertyName("pendingPrintJobs")] int PendingPrintJobs,
    [property: JsonPropertyName("printedPrintJobs")] int PrintedPrintJobs,
    [property: JsonPropertyName("failedPrintJobs")] int FailedPrintJobs,
    [property: JsonPropertyName("status")] string Status);

public sealed record LocalCashDrawerStatus(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("operational")] bool Operational);

public sealed record LocalTerminalDiagnostics(
    [property: JsonPropertyName("isBound")] bool IsBound,
    [property: JsonPropertyName("tenantId")] Guid? TenantId,
    [property: JsonPropertyName("storeId")] Guid? StoreId,
    [property: JsonPropertyName("terminalId")] Guid? TerminalId,
    [property: JsonPropertyName("boundAtUtc")] DateTimeOffset? BoundAtUtc,
    [property: JsonPropertyName("schemaVersion")] int? SchemaVersion);

public sealed record SupportBundleHardwareInfo(
    [property: JsonPropertyName("printerState")] LocalPrinterStatus PrinterState,
    [property: JsonPropertyName("cashDrawerState")] LocalCashDrawerStatus CashDrawerState,
    [property: JsonPropertyName("terminalState")] LocalTerminalDiagnostics TerminalState,
    [property: JsonPropertyName("hardwareSummary")] LocalHardwareSummary HardwareSummary);

public sealed class SupportBundleExportOptions
{
    public string OutputPath { get; set; } = string.Empty;
    public bool AsDirectory { get; set; }
    public string? LogDirectory { get; set; }
    public IReadOnlyList<string>? LogFiles { get; set; }
    public bool IncludeDefaultDiagnosticsLog { get; set; } = true;
    public string? ApplicationVersion { get; set; }
}

public sealed record SupportBundleExportResult(
    string OutputPath,
    string Format,
    int FilesCount,
    long Bytes,
    DateTimeOffset GeneratedAtUtc,
    string ApplicationVersion,
    int SchemaVersion,
    string SyncContract,
    IReadOnlyList<string> IncludedFiles);

public sealed class SupportBundleData
{
    public SupportBundleManifest Manifest { get; }
    public SupportBundleRuntimeInfo Runtime { get; }
    public SupportBundleSqliteInfo Sqlite { get; }
    public SupportBundleSyncInfo Sync { get; }
    public SupportBundleHardwareInfo Hardware { get; }
    public IReadOnlyDictionary<string, string> Logs { get; }

    public SupportBundleData(
        SupportBundleManifest manifest,
        SupportBundleRuntimeInfo runtime,
        SupportBundleSqliteInfo sqlite,
        SupportBundleSyncInfo sync,
        SupportBundleHardwareInfo hardware,
        IReadOnlyDictionary<string, string> logs)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        Sqlite = sqlite ?? throw new ArgumentNullException(nameof(sqlite));
        Sync = sync ?? throw new ArgumentNullException(nameof(sync));
        Hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
        Logs = logs ?? throw new ArgumentNullException(nameof(logs));
    }

    public IReadOnlyDictionary<string, string> GetAllFiles(JsonSerializerOptions? serializerOptions = null)
    {
        var options = serializerOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["manifest.json"] = JsonSerializer.Serialize(Manifest, options),
            ["runtime.json"] = JsonSerializer.Serialize(Runtime, options),
            ["sqlite.json"] = JsonSerializer.Serialize(Sqlite, options),
            ["sync.json"] = JsonSerializer.Serialize(Sync, options),
            ["hardware.json"] = JsonSerializer.Serialize(Hardware, options)
        };

        foreach (var (logPath, logContent) in Logs)
        {
            files[logPath] = logContent;
        }

        return files;
    }
}
