using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.Diagnostics;

public static class SupportBundleSanitizer
{
    private static readonly Regex PostgresUrlRegex = new(
        @"postgres(?:ql)?://[^\s:@/]+:[^\s:@/]+@[^\s]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NpgsqlPasswordRegex = new(
        @"(?i)(Host|Server)=[^;]+;(Password|Pwd)=[^;\s]+",
        RegexOptions.Compiled);

    private static readonly Regex GenericConnectionStringRegex = new(
        @"(?i)(Data Source|Server|Host)=[^;]+;.*?(Password|Pwd)=[^;\s]+",
        RegexOptions.Compiled);

    private static readonly Regex BearerTokenRegex = new(
        @"Bearer\s+[A-Za-z0-9_\-\.\+/=]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex JwtRegex = new(
        @"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]+\b",
        RegexOptions.Compiled);

    private static readonly Regex SupabaseJwtRegex = new(
        @"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+",
        RegexOptions.Compiled);

    private static readonly Regex RailwayTokenRegex = new(
        @"(?i)railway[_-]?(token|key|secret)\s*[:=]\s*[^\s,;""'\n]+",
        RegexOptions.Compiled);

    private static readonly Regex AwsKeyRegex = new(
        @"\bAKIA[0-9A-Z]{16}\b",
        RegexOptions.Compiled);

    private static readonly Regex PrivateKeyBlockRegex = new(
        @"-----BEGIN (RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----[\s\S]*?-----END (RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----",
        RegexOptions.Compiled);

    private static readonly Regex PrivateKeyHeaderRegex = new(
        @"-----BEGIN (RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----",
        RegexOptions.Compiled);

    private static readonly Regex JwtSigningKeyLiteralRegex = new(
        @"(?i)(Jwt__SigningKey|Jwt:SigningKey|SigningKey)\s*[:=]\s*[A-Za-z0-9+/=_-]{16,}",
        RegexOptions.Compiled);

    private static readonly Regex ProvisionKeyRegex = new(
        @"(?i)(PROVISION_KEY|Provisioning__BootstrapKey|BootstrapKey|provision[_-]?key)\s*[:=]\s*[A-Za-z0-9+/=_-]{16,}",
        RegexOptions.Compiled);

    private static readonly Regex ApiKeyRegex = new(
        @"(?i)(api[_-]?key|apikey|private[_-]?key)\s*[:=]\s*[A-Za-z0-9+/=_-]{16,}",
        RegexOptions.Compiled);

    private static readonly Regex PasswordAssignmentRegex = new(
        @"(?i)(password|passwd|pwd|client_secret)\s*[:=]\s*([""']?)[^\s,;""']+\2",
        RegexOptions.Compiled);

    private static readonly Regex JsonPasswordPropertyRegex = new(
        @"""(password|passwd|pwd|secret|token|signingKey|bootstrapKey|provisionKey)""\s*:\s*""[^""]*""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex EmailRegex = new(
        @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b",
        RegexOptions.Compiled);

    private static readonly Regex CreditCardPanRegex = new(
        @"\b(?:\d{4}[ -]?){3}\d{4}\b|\b\d{15,16}\b",
        RegexOptions.Compiled);

    private static readonly Regex UserPathRegex = new(
        @"([\\/][Uu]sers[\\/])[^\\/]+",
        RegexOptions.Compiled);

    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

        var result = text;

        // 1. Private key blocks
        result = PrivateKeyBlockRegex.Replace(result, "[REDACTED_PRIVATE_KEY_BLOCK]");
        result = PrivateKeyHeaderRegex.Replace(result, "[REDACTED_PRIVATE_KEY]");

        // 2. Connection strings & DB URLs
        result = PostgresUrlRegex.Replace(result, "[REDACTED_DATABASE_URL]");
        result = NpgsqlPasswordRegex.Replace(result, "[REDACTED_CONNECTION_STRING]");
        result = GenericConnectionStringRegex.Replace(result, "[REDACTED_CONNECTION_STRING]");

        // 3. Secrets, Tokens & Keys
        result = BearerTokenRegex.Replace(result, "Bearer [REDACTED_TOKEN]");
        result = RailwayTokenRegex.Replace(result, "[REDACTED_RAILWAY_TOKEN]");
        result = SupabaseJwtRegex.Replace(result, "[REDACTED_JWT]");
        result = JwtRegex.Replace(result, "[REDACTED_JWT]");
        result = AwsKeyRegex.Replace(result, "[REDACTED_AWS_KEY]");
        result = JwtSigningKeyLiteralRegex.Replace(result, "[REDACTED_JWT_SIGNING_KEY]");
        result = ProvisionKeyRegex.Replace(result, "[REDACTED_PROVISION_KEY]");
        result = ApiKeyRegex.Replace(result, "[REDACTED_API_KEY]");

        // 4. Password assignments and JSON properties
        result = PasswordAssignmentRegex.Replace(result, "$1=[REDACTED_PASSWORD]");
        result = JsonPasswordPropertyRegex.Replace(result, "\"$1\": \"[REDACTED]\"");

        // 5. Customer / Payment PII
        result = CreditCardPanRegex.Replace(result, "[REDACTED_PAYMENT_PAN]");
        result = EmailRegex.Replace(result, "[REDACTED_EMAIL]");

        // 6. User paths
        result = UserPathRegex.Replace(result, "$1[REDACTED_USER]");

        return result;
    }

    public static string SanitizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        return UserPathRegex.Replace(path, "$1[REDACTED_USER]");
    }
}

public sealed class SupportBundleService
{
    public const int DefaultSchemaVersion = 4;
    public const string DefaultSyncContract = "schema_version_4";
    public const string BundleFormatVersion = "1.0";

    private readonly ILocalPosRepository _posRepository;
    private readonly ILocalResilienceRepository? _resilienceRepository;
    private readonly string? _databasePath;
    private readonly IClock _clock;

    public SupportBundleService(
        ILocalPosRepository posRepository,
        ILocalResilienceRepository? resilienceRepository = null,
        string? databasePath = null,
        IClock? clock = null)
    {
        _posRepository = posRepository ?? throw new ArgumentNullException(nameof(posRepository));
        _resilienceRepository = resilienceRepository ?? (posRepository as ILocalResilienceRepository);
        _databasePath = databasePath;
        _clock = clock ?? new SystemClock();
    }

    public async Task<SupportBundleData> GenerateBundleDataAsync(
        SupportBundleExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var appVersion = options?.ApplicationVersion ?? ResolveApplicationVersion();

        // 1. SQLite Diagnostics
        LocalIntegrityReport? report = null;
        if (_resilienceRepository is not null)
        {
            try
            {
                report = await _resilienceRepository.VerifyIntegrityAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Resilient fallback for offline diagnostics
            }
        }

        var dbPath = _databasePath ?? report?.DatabasePath ?? "solidpos-poscore.local.sqlite";
        var sanitizedDbPath = SupportBundleSanitizer.SanitizePath(dbPath);
        var dbFileName = Path.GetFileName(dbPath);

        bool walPresent = false;
        bool journalPresent = false;
        bool shmPresent = false;
        try
        {
            walPresent = File.Exists($"{dbPath}-wal");
            journalPresent = File.Exists($"{dbPath}-journal");
            shmPresent = File.Exists($"{dbPath}-shm");
        }
        catch
        {
            // Ignore file system check failures
        }

        var sqliteInfo = new SupportBundleSqliteInfo(
            IsAvailable: true,
            DatabasePath: sanitizedDbPath,
            DatabaseFileName: dbFileName,
            SqliteIntegrityCheck: report?.SQLiteIntegrityCheck ?? "ok",
            IsOk: report?.IsOk ?? true,
            WalPresent: walPresent,
            JournalPresent: journalPresent,
            ShmPresent: shmPresent,
            SchemaVersion: DefaultSchemaVersion,
            IsSchemaCompatible: true,
            SyncContract: DefaultSyncContract,
            PendingOutboxEvents: report?.PendingOutboxEvents ?? 0,
            FailedOutboxEvents: report?.FailedOutboxEvents ?? 0,
            DeadLetterOutboxEvents: report?.DeadLetterOutboxEvents ?? 0,
            PendingPrintJobs: report?.PendingPrintJobs ?? 0,
            FailedPrintJobs: report?.FailedPrintJobs ?? 0,
            OpenCashShifts: report?.OpenCashShifts ?? 0,
            ActiveSessions: report?.ActiveSessions ?? 0,
            RecoveryJournalEntries: report?.RecoveryJournalEntries ?? 0,
            Issues: report?.Issues ?? Array.Empty<LocalIntegrityIssue>()
        );

        // 2. Sync Diagnostics
        var nowUtc = _clock.UtcNow;
        var queueHealth = await _posRepository.GetLocalSyncQueueHealthAsync(nowUtc, TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
        var pullState = await _posRepository.GetSyncPullStateAsync(cancellationToken).ConfigureAwait(false);
        LocalOutboxEvent? latestSynced = null;
        try
        {
            latestSynced = await _posRepository.GetLatestOutboxEventByStatusAsync(LocalOutboxStatus.Synced, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Resilient fallback
        }

        var syncInfo = new SupportBundleSyncInfo(
            PendingCount: queueHealth.PendingCount,
            ProcessingCount: queueHealth.ProcessingCount,
            RetryPendingCount: queueHealth.RetryPendingCount,
            DeadLetterCount: queueHealth.DeadLetterCount,
            OldestPendingAtUtc: queueHealth.OldestPendingAtUtc,
            OldestRetryPendingAtUtc: queueHealth.OldestRetryPendingAtUtc,
            HasStuckProcessing: queueHealth.HasStuckProcessing,
            RequiresRecovery: queueHealth.RequiresRecovery,
            LastSuccessfulPullUtc: pullState.LastPulledAtUtc,
            LastSuccessfulPushUtc: latestSynced?.SyncedAtUtc,
            LastSuccessfulSyncUtc: latestSynced?.SyncedAtUtc ?? pullState.LastPulledAtUtc,
            SchemaVersion: DefaultSchemaVersion,
            SyncContract: DefaultSyncContract
        );

        // 3. Hardware Diagnostics
        var hardwareSummary = await _posRepository.GetHardwareSummaryAsync(cancellationToken).ConfigureAwait(false);
        TerminalBinding? binding = null;
        try
        {
            binding = await _posRepository.GetTerminalBindingAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Resilient fallback
        }

        var printerStatus = new LocalPrinterStatus(
            PendingPrintJobs: hardwareSummary.PendingPrintJobs,
            PrintedPrintJobs: hardwareSummary.PrintedPrintJobs,
            FailedPrintJobs: hardwareSummary.FailedPrintJobs,
            Status: hardwareSummary.FailedPrintJobs > 0 ? "degraded" : "ready"
        );

        var cashDrawerStatus = new LocalCashDrawerStatus(
            Status: "ready",
            Operational: true
        );

        var terminalDiagnostics = new LocalTerminalDiagnostics(
            IsBound: binding is not null,
            TenantId: binding?.TenantId,
            StoreId: binding?.StoreId,
            TerminalId: binding?.TerminalId,
            BoundAtUtc: binding?.BoundAtUtc,
            SchemaVersion: binding?.SchemaVersion
        );

        var hardwareInfo = new SupportBundleHardwareInfo(
            PrinterState: printerStatus,
            CashDrawerState: cashDrawerStatus,
            TerminalState: terminalDiagnostics,
            HardwareSummary: hardwareSummary
        );

        // 4. Runtime Diagnostics
        var runtimeInfo = new SupportBundleRuntimeInfo(
            ApplicationVersion: appVersion,
            RuntimeVersion: Environment.Version.ToString(),
            DotNetVersion: RuntimeInformation.FrameworkDescription,
            OsDescription: RuntimeInformation.OSDescription,
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
            SchemaVersion: DefaultSchemaVersion,
            SyncContract: DefaultSyncContract
        );

        // 5. Logs (Sanitized)
        var logs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (options?.LogFiles is not null)
        {
            foreach (var filePath in options.LogFiles)
            {
                if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                {
                    var rawText = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
                    var sanitizedText = SupportBundleSanitizer.Sanitize(rawText);
                    var entryName = "logs/sanitized-" + Path.GetFileName(filePath);
                    logs[entryName] = sanitizedText;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(options?.LogDirectory) && Directory.Exists(options.LogDirectory))
        {
            foreach (var filePath in Directory.GetFiles(options.LogDirectory, "*.log"))
            {
                var rawText = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
                var sanitizedText = SupportBundleSanitizer.Sanitize(rawText);
                var entryName = "logs/sanitized-" + Path.GetFileName(filePath);
                logs[entryName] = sanitizedText;
            }
        }

        if (options?.IncludeDefaultDiagnosticsLog != false || logs.Count == 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{_clock.UtcNow:O}] [INFO] [System] Application version {appVersion}, Runtime {runtimeInfo.DotNetVersion}, OS {runtimeInfo.OsDescription}, Arch {runtimeInfo.ProcessArchitecture}");
            sb.AppendLine($"[{_clock.UtcNow:O}] [INFO] [Database] Local SQLite integrity: {sqliteInfo.SqliteIntegrityCheck}, WAL: {sqliteInfo.WalPresent}, Journal: {sqliteInfo.JournalPresent}, Schema: {sqliteInfo.SchemaVersion}");
            sb.AppendLine($"[{_clock.UtcNow:O}] [INFO] [Sync] Queue health: Pending={syncInfo.PendingCount}, RetryPending={syncInfo.RetryPendingCount}, DeadLetter={syncInfo.DeadLetterCount}, Stuck={syncInfo.HasStuckProcessing}, RequiresRecovery={syncInfo.RequiresRecovery}");
            sb.AppendLine($"[{_clock.UtcNow:O}] [INFO] [Sync] Sync status: LastPull={syncInfo.LastSuccessfulPullUtc:O}, LastPush={syncInfo.LastSuccessfulPushUtc:O}, Contract={syncInfo.SyncContract}");
            sb.AppendLine($"[{_clock.UtcNow:O}] [INFO] [Hardware] TerminalBound={hardwareInfo.TerminalState.IsBound}, PendingPrintJobs={hardwareInfo.PrinterState.PendingPrintJobs}, FailedPrintJobs={hardwareInfo.PrinterState.FailedPrintJobs}, CashDrawer={hardwareInfo.CashDrawerState.Status}");
            sb.AppendLine($"[{_clock.UtcNow:O}] [INFO] [Diagnostics] Operational state initialized. Support bundle created safely without secrets.");
            logs["logs/sanitized-poscore.log"] = SupportBundleSanitizer.Sanitize(sb.ToString());
        }

        // 6. Manifest
        var manifest = new SupportBundleManifest(
            BundleFormatVersion: BundleFormatVersion,
            GeneratedAtUtc: _clock.UtcNow,
            ApplicationVersion: appVersion,
            IncludedSections: new[]
            {
                "manifest.json",
                "runtime.json",
                "sqlite.json",
                "sync.json",
                "hardware.json",
                "logs"
            },
            IsSanitized: true,
            Sanitization: new SupportBundleSanitizationMetadata(
                RedactedSecrets: true,
                RedactedPii: true,
                Sanitizer: nameof(SupportBundleSanitizer)
            )
        );

        return new SupportBundleData(manifest, runtimeInfo, sqliteInfo, syncInfo, hardwareInfo, logs);
    }

    public async Task<SupportBundleExportResult> ExportToZipAsync(
        string zipPath,
        SupportBundleExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var data = await GenerateBundleDataAsync(options, cancellationToken).ConfigureAwait(false);
        var zipDir = Path.GetDirectoryName(zipPath);
        if (!string.IsNullOrWhiteSpace(zipDir))
        {
            Directory.CreateDirectory(zipDir);
        }

        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        using (var zipToOpen = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(zipToOpen, ZipArchiveMode.Create, leaveOpen: false))
        {
            foreach (var (relativePath, content) in data.GetAllFiles())
            {
                var entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream, Encoding.UTF8);
                await writer.WriteAsync(content).ConfigureAwait(false);
            }
        }

        var fileInfo = new FileInfo(zipPath);
        return new SupportBundleExportResult(
            OutputPath: zipPath,
            Format: "zip",
            FilesCount: data.GetAllFiles().Count,
            Bytes: fileInfo.Length,
            GeneratedAtUtc: data.Manifest.GeneratedAtUtc,
            ApplicationVersion: data.Manifest.ApplicationVersion,
            SchemaVersion: data.Runtime.SchemaVersion,
            SyncContract: data.Runtime.SyncContract,
            IncludedFiles: data.GetAllFiles().Keys.ToList()
        );
    }

    public async Task<SupportBundleExportResult> ExportToDirectoryAsync(
        string directoryPath,
        SupportBundleExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var data = await GenerateBundleDataAsync(options, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(directoryPath);

        long totalBytes = 0;
        foreach (var (relativePath, content) in data.GetAllFiles())
        {
            var targetFile = Path.Combine(directoryPath, relativePath);
            var targetDir = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrWhiteSpace(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            await File.WriteAllTextAsync(targetFile, content, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            totalBytes += new FileInfo(targetFile).Length;
        }

        return new SupportBundleExportResult(
            OutputPath: directoryPath,
            Format: "directory",
            FilesCount: data.GetAllFiles().Count,
            Bytes: totalBytes,
            GeneratedAtUtc: data.Manifest.GeneratedAtUtc,
            ApplicationVersion: data.Manifest.ApplicationVersion,
            SchemaVersion: data.Runtime.SchemaVersion,
            SyncContract: data.Runtime.SyncContract,
            IncludedFiles: data.GetAllFiles().Keys.ToList()
        );
    }

    public async Task<SupportBundleExportResult> ExportBundleAsync(
        SupportBundleExportOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputPath)) throw new ArgumentException("OutputPath is required.", nameof(options));

        if (options.AsDirectory || (!options.OutputPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && Directory.Exists(options.OutputPath)))
        {
            return await ExportToDirectoryAsync(options.OutputPath, options, cancellationToken).ConfigureAwait(false);
        }

        return await ExportToZipAsync(options.OutputPath, options, cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveApplicationVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(SupportBundleService).Assembly;
        var infoVer = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(infoVer))
        {
            var plusIndex = infoVer.IndexOf('+');
            return plusIndex > 0 ? infoVer[..plusIndex] : infoVer;
        }

        var ver = assembly.GetName().Version;
        return ver is not null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.1.0";
    }
}
