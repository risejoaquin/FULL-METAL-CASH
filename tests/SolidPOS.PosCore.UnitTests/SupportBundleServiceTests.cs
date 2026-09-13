using System.IO.Compression;
using System.Text.Json;
using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Diagnostics;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

public sealed class SupportBundleServiceTests
{
    [Fact]
    public async Task Bundle_creation_succeeds_and_all_required_files_exist()
    {
        var repository = new TestSupportRepository();
        var service = new SupportBundleService(repository, repository, "test-database.sqlite", new FixedTestClock(DateTimeOffset.UtcNow));
        var tempZipPath = Path.Combine(Path.GetTempPath(), $"support-bundle-test-{Guid.NewGuid():N}.zip");

        try
        {
            var result = await service.ExportToZipAsync(tempZipPath, new SupportBundleExportOptions
            {
                ApplicationVersion = "1.1.0"
            });

            Assert.True(File.Exists(tempZipPath));
            Assert.True(result.Bytes > 0);
            Assert.Equal("zip", result.Format);
            Assert.Equal(4, result.SchemaVersion);
            Assert.Equal("schema_version_4", result.SyncContract);

            using var zipStream = File.OpenRead(tempZipPath);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

            Assert.NotNull(archive.GetEntry("manifest.json"));
            Assert.NotNull(archive.GetEntry("runtime.json"));
            Assert.NotNull(archive.GetEntry("sqlite.json"));
            Assert.NotNull(archive.GetEntry("sync.json"));
            Assert.NotNull(archive.GetEntry("hardware.json"));
            Assert.NotNull(archive.GetEntry("logs/sanitized-poscore.log"));
        }
        finally
        {
            if (File.Exists(tempZipPath)) File.Delete(tempZipPath);
        }
    }

    [Fact]
    public async Task Manifest_contains_application_version_and_metadata()
    {
        var repository = new TestSupportRepository();
        var service = new SupportBundleService(repository, repository, "test.sqlite", new FixedTestClock(DateTimeOffset.UtcNow));
        var bundle = await service.GenerateBundleDataAsync(new SupportBundleExportOptions { ApplicationVersion = "1.1.0-rc1" });

        Assert.Equal("1.0", bundle.Manifest.BundleFormatVersion);
        Assert.Equal("1.1.0-rc1", bundle.Manifest.ApplicationVersion);
        Assert.True(bundle.Manifest.IsSanitized);
        Assert.True(bundle.Manifest.Sanitization.RedactedSecrets);
        Assert.True(bundle.Manifest.Sanitization.RedactedPii);
        Assert.Contains("manifest.json", bundle.Manifest.IncludedSections);
        Assert.Contains("runtime.json", bundle.Manifest.IncludedSections);
        Assert.Contains("sqlite.json", bundle.Manifest.IncludedSections);
        Assert.Contains("sync.json", bundle.Manifest.IncludedSections);
        Assert.Contains("hardware.json", bundle.Manifest.IncludedSections);
        Assert.Contains("logs", bundle.Manifest.IncludedSections);
    }

    [Fact]
    public async Task Runtime_diagnostics_include_app_version_schema_version_and_sync_contract()
    {
        var repository = new TestSupportRepository();
        var service = new SupportBundleService(repository, repository, "test.sqlite", new FixedTestClock(DateTimeOffset.UtcNow));
        var bundle = await service.GenerateBundleDataAsync(new SupportBundleExportOptions { ApplicationVersion = "1.1.0" });

        Assert.Equal("1.1.0", bundle.Runtime.ApplicationVersion);
        Assert.Equal(4, bundle.Runtime.SchemaVersion);
        Assert.Equal("schema_version_4", bundle.Runtime.SyncContract);
        Assert.False(string.IsNullOrWhiteSpace(bundle.Runtime.DotNetVersion));
        Assert.False(string.IsNullOrWhiteSpace(bundle.Runtime.OsDescription));
        Assert.False(string.IsNullOrWhiteSpace(bundle.Runtime.ProcessArchitecture));
    }

    [Fact]
    public async Task Sqlite_diagnostics_include_sqlite_state_integrity_and_wal_status()
    {
        var repository = new TestSupportRepository();
        var service = new SupportBundleService(repository, repository, @"C:\Users\Lucilfer\Documents\SolidPos\solidpos-poscore.local.sqlite", new FixedTestClock(DateTimeOffset.UtcNow));
        var bundle = await service.GenerateBundleDataAsync();

        Assert.True(bundle.Sqlite.IsAvailable);
        Assert.Equal("ok", bundle.Sqlite.SqliteIntegrityCheck);
        Assert.True(bundle.Sqlite.IsOk);
        Assert.Equal(4, bundle.Sqlite.SchemaVersion);
        Assert.True(bundle.Sqlite.IsSchemaCompatible);
        Assert.Equal("schema_version_4", bundle.Sqlite.SyncContract);
        Assert.Equal("solidpos-poscore.local.sqlite", bundle.Sqlite.DatabaseFileName);
        Assert.DoesNotContain("Lucilfer", bundle.Sqlite.DatabasePath);
        Assert.Contains("[REDACTED_USER]", bundle.Sqlite.DatabasePath);
    }

    [Fact]
    public async Task Sync_diagnostics_include_queue_summary_and_last_sync_timestamp()
    {
        var now = DateTimeOffset.UtcNow;
        var lastPulled = now.AddMinutes(-10);
        var lastSynced = now.AddMinutes(-5);

        var repository = new TestSupportRepository(
            queueHealth: new LocalSyncQueueHealthSummary(
                PendingCount: 2,
                ProcessingCount: 1,
                RetryPendingCount: 3,
                DeadLetterCount: 0,
                OldestPendingAtUtc: now.AddHours(-1),
                OldestRetryPendingAtUtc: now.AddMinutes(-30),
                HasStuckProcessing: false,
                RequiresRecovery: true),
            pullState: new LocalSyncPullState("cursor-123", lastPulled, 10, 5),
            latestSyncedEvent: new LocalOutboxEvent(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                "sale.completed", 4, 100, "{}", LocalOutboxStatus.Synced, now.AddHours(-2), lastSynced)
        );

        var service = new SupportBundleService(repository, repository, "test.sqlite", new FixedTestClock(now));
        var bundle = await service.GenerateBundleDataAsync();

        Assert.Equal(2, bundle.Sync.PendingCount);
        Assert.Equal(1, bundle.Sync.ProcessingCount);
        Assert.Equal(3, bundle.Sync.RetryPendingCount);
        Assert.Equal(0, bundle.Sync.DeadLetterCount);
        Assert.NotNull(bundle.Sync.OldestPendingAtUtc);
        Assert.NotNull(bundle.Sync.OldestRetryPendingAtUtc);
        Assert.False(bundle.Sync.HasStuckProcessing);
        Assert.True(bundle.Sync.RequiresRecovery);
        Assert.Equal(lastPulled, bundle.Sync.LastSuccessfulPullUtc);
        Assert.Equal(lastSynced, bundle.Sync.LastSuccessfulPushUtc);
        Assert.Equal(lastSynced, bundle.Sync.LastSuccessfulSyncUtc);
        Assert.Equal(4, bundle.Sync.SchemaVersion);
        Assert.Equal("schema_version_4", bundle.Sync.SyncContract);
    }

    [Fact]
    public async Task Hardware_diagnostics_include_hardware_state_and_summary()
    {
        var tenantId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var terminalId = Guid.NewGuid();

        var repository = new TestSupportRepository(
            hardwareSummary: new LocalHardwareSummary(
                PendingPrintJobs: 1,
                PrintedPrintJobs: 25,
                FailedPrintJobs: 0,
                HardwareEventCount: 42,
                LatestEvent: new LocalHardwareEvent(Guid.NewGuid(), tenantId, storeId, terminalId, LocalHardwareDeviceTypes.ReceiptPrinter, "print.ok", "Printed", DateTimeOffset.UtcNow)
            ),
            terminalBinding: new TerminalBinding(tenantId, storeId, terminalId, "fingerprint-123", "secret-terminal-token-xyz", DateTimeOffset.UtcNow, 4)
        );

        var service = new SupportBundleService(repository, repository, "test.sqlite", new FixedTestClock(DateTimeOffset.UtcNow));
        var bundle = await service.GenerateBundleDataAsync();

        Assert.Equal(1, bundle.Hardware.PrinterState.PendingPrintJobs);
        Assert.Equal(25, bundle.Hardware.PrinterState.PrintedPrintJobs);
        Assert.Equal(0, bundle.Hardware.PrinterState.FailedPrintJobs);
        Assert.Equal("ready", bundle.Hardware.PrinterState.Status);
        Assert.Equal("ready", bundle.Hardware.CashDrawerState.Status);
        Assert.True(bundle.Hardware.TerminalState.IsBound);
        Assert.Equal(tenantId, bundle.Hardware.TerminalState.TenantId);
        Assert.Equal(storeId, bundle.Hardware.TerminalState.StoreId);
        Assert.Equal(terminalId, bundle.Hardware.TerminalState.TerminalId);
        Assert.Equal(4, bundle.Hardware.TerminalState.SchemaVersion);

        // Crucial security check: Terminal token must never be in serialized hardware json
        var allFiles = bundle.GetAllFiles();
        var hardwareJson = allFiles["hardware.json"];
        Assert.DoesNotContain("secret-terminal-token-xyz", hardwareJson);
    }

    [Fact]
    public async Task Logs_are_sanitized_and_technical_usefulness_preserved()
    {
        var repository = new TestSupportRepository();
        var service = new SupportBundleService(repository, repository, "test.sqlite", new FixedTestClock(DateTimeOffset.UtcNow));

        var tempLogDir = Path.Combine(Path.GetTempPath(), $"poscore-log-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempLogDir);
        var customLogFile = Path.Combine(tempLogDir, "poscore-audit.log");

        try
        {
            var rawContent = string.Join("\n", new[]
            {
                "2026-09-12T12:00:00Z [INFO] Processing sale checkout.",
                "2026-09-12T12:00:01Z [DEBUG] Auth header: " + "Bearer " + "secret_token_value_abc123",
                "2026-09-12T12:00:02Z [DEBUG] Connection string: " + "Server=10.0.0.1; " + "Password=" + "my_secret_pass; Database=pos",
                "2026-09-12T12:00:03Z [INFO] Customer receipt emailed to user@customerdomain.com"
            });
            await File.WriteAllTextAsync(customLogFile, rawContent);

            var bundle = await service.GenerateBundleDataAsync(new SupportBundleExportOptions
            {
                LogDirectory = tempLogDir
            });

            Assert.Contains("logs/sanitized-poscore-audit.log", bundle.Logs.Keys);
            var sanitizedLog = bundle.Logs["logs/sanitized-poscore-audit.log"];

            Assert.Contains("Processing sale checkout", sanitizedLog);
            Assert.Contains("Bearer [REDACTED_TOKEN]", sanitizedLog);
            Assert.DoesNotContain("secret_token_value_abc123", sanitizedLog);
            Assert.DoesNotContain("my_secret_pass", sanitizedLog);
            Assert.DoesNotContain("user@customerdomain.com", sanitizedLog);
            Assert.Contains("[REDACTED_EMAIL]", sanitizedLog);
        }
        finally
        {
            if (Directory.Exists(tempLogDir)) Directory.Delete(tempLogDir, true);
        }
    }

    [Fact]
    public void Sanitizer_redacts_password_like_values()
    {
        var input1 = "password=SuperSecretPassword123";
        var input2 = "{\"password\": \"AnotherSecretPass456\"}";
        var input3 = "pwd: mySecretPassword";

        var sanitized1 = SupportBundleSanitizer.Sanitize(input1);
        var sanitized2 = SupportBundleSanitizer.Sanitize(input2);
        var sanitized3 = SupportBundleSanitizer.Sanitize(input3);

        Assert.DoesNotContain("SuperSecretPassword123", sanitized1);
        Assert.Contains("password=[REDACTED_PASSWORD]", sanitized1);

        Assert.DoesNotContain("AnotherSecretPass456", sanitized2);
        Assert.Contains("\"password\": \"[REDACTED]\"", sanitized2);

        Assert.DoesNotContain("mySecretPassword", sanitized3);
        Assert.Contains("pwd=[REDACTED_PASSWORD]", sanitized3);
    }

    [Fact]
    public void Sanitizer_redacts_bearer_tokens()
    {
        var input = "Authorization: " + "Bearer " + string.Concat("eyJhbGciOiJIUzI1Ni", "IsInR5cCI6IkpXVCJ9", ".e30.t-IDcSemACt8x4iTMCda8Yhe3iZaWbvV5XKSTbuAn0M");
        var sanitized = SupportBundleSanitizer.Sanitize(input);

        Assert.DoesNotContain("t-IDcSemACt8x4iTMCda8Yhe3iZaWbvV5XKSTbuAn0M", sanitized);
        Assert.Contains("Bearer [REDACTED_TOKEN]", sanitized);
    }

    [Fact]
    public void Sanitizer_redacts_jwt_like_values()
    {
        var input = "Token is " + string.Concat("eyJhbGciOiJIUzI1Ni", "IsInR5cCI6IkpXVCJ9", ".eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U");
        var sanitized = SupportBundleSanitizer.Sanitize(input);

        Assert.DoesNotContain("dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U", sanitized);
        Assert.Contains("[REDACTED_JWT]", sanitized);
    }

    [Fact]
    public void Sanitizer_redacts_connection_strings()
    {
        var input1 = "postgres" + "ql://" + "admin:" + "topsecretpassword" + "@localhost:5432/solidpos";
        var input2 = "Host=localhost;Port=5432;" + "Password=" + "mysecretnpgsqlpass;Database=solidpos";

        var sanitized1 = SupportBundleSanitizer.Sanitize(input1);
        var sanitized2 = SupportBundleSanitizer.Sanitize(input2);

        Assert.DoesNotContain("topsecretpassword", sanitized1);
        Assert.Contains("[REDACTED_DATABASE_URL]", sanitized1);

        Assert.DoesNotContain("mysecretnpgsqlpass", sanitized2);
        Assert.Contains("[REDACTED_CONNECTION_STRING]", sanitized2);
    }

    [Fact]
    public void Sanitizer_redacts_api_keys_private_keys_and_provision_keys()
    {
        var railwayInput = "railway" + "_token=" + "rw_live_1234567890abcdef";
        var awsInput = "AWS key: " + "AKIA" + "IOSFODNN7EXAMPLE";
        var privKeyInput = "-----BEGIN " + "RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEA0...\n-----END " + "RSA PRIVATE KEY-----";
        var provisionInput = "PROVISION" + "_KEY=" + "bootstrap_provision_secret_99999999";
        var apiKeyInput = "api" + "_key=" + "live_api_key_secret_12345678";

        Assert.Contains("[REDACTED_RAILWAY_TOKEN]", SupportBundleSanitizer.Sanitize(railwayInput));
        Assert.Contains("[REDACTED_AWS_KEY]", SupportBundleSanitizer.Sanitize(awsInput));
        Assert.Contains("[REDACTED_PRIVATE_KEY_BLOCK]", SupportBundleSanitizer.Sanitize(privKeyInput));
        Assert.Contains("[REDACTED_PROVISION_KEY]", SupportBundleSanitizer.Sanitize(provisionInput));
        Assert.Contains("[REDACTED_API_KEY]", SupportBundleSanitizer.Sanitize(apiKeyInput));
    }

    [Fact]
    public async Task Bundle_generation_does_not_mutate_operational_data()
    {
        var repository = new TestSupportRepository();
        var initialPendingCount = (await repository.GetPendingOutboxEventsAsync(100)).Count;
        var initialHardwareSummary = await repository.GetHardwareSummaryAsync();

        var service = new SupportBundleService(repository, repository, "test.sqlite", new FixedTestClock(DateTimeOffset.UtcNow));
        var bundle = await service.GenerateBundleDataAsync();

        var afterPendingCount = (await repository.GetPendingOutboxEventsAsync(100)).Count;
        var afterHardwareSummary = await repository.GetHardwareSummaryAsync();

        Assert.Equal(initialPendingCount, afterPendingCount);
        Assert.Equal(initialHardwareSummary.PendingPrintJobs, afterHardwareSummary.PendingPrintJobs);
        Assert.Equal(initialHardwareSummary.PrintedPrintJobs, afterHardwareSummary.PrintedPrintJobs);
        Assert.Equal(initialHardwareSummary.HardwareEventCount, afterHardwareSummary.HardwareEventCount);
    }

    [Fact]
    public async Task Bundle_works_offline()
    {
        var repository = new TestSupportRepository();
        // Repository has no network / offline-only
        var service = new SupportBundleService(repository, repository, "test.sqlite", new FixedTestClock(DateTimeOffset.UtcNow));
        var tempZipPath = Path.Combine(Path.GetTempPath(), $"offline-bundle-{Guid.NewGuid():N}.zip");

        try
        {
            var result = await service.ExportToZipAsync(tempZipPath);
            Assert.True(File.Exists(tempZipPath));
            Assert.True(result.Bytes > 0);
        }
        finally
        {
            if (File.Exists(tempZipPath)) File.Delete(tempZipPath);
        }
    }

    [Fact]
    public async Task Bundle_can_be_generated_without_restarting_poscore()
    {
        var repository = new TestSupportRepository();
        var service = new SupportBundleService(repository, repository, "test.sqlite", new FixedTestClock(DateTimeOffset.UtcNow));

        // Generate bundle multiple times consecutively on active instance
        var bundle1 = await service.GenerateBundleDataAsync();
        var bundle2 = await service.GenerateBundleDataAsync();
        var bundle3 = await service.GenerateBundleDataAsync();

        Assert.NotNull(bundle1);
        Assert.NotNull(bundle2);
        Assert.NotNull(bundle3);
        Assert.Equal(4, bundle1.Runtime.SchemaVersion);
        Assert.Equal(4, bundle2.Runtime.SchemaVersion);
        Assert.Equal(4, bundle3.Runtime.SchemaVersion);
    }

    [Fact]
    public async Task Export_to_directory_creates_matching_file_structure()
    {
        var repository = new TestSupportRepository();
        var service = new SupportBundleService(repository, repository, "test.sqlite", new FixedTestClock(DateTimeOffset.UtcNow));
        var tempDirPath = Path.Combine(Path.GetTempPath(), $"support-bundle-dir-{Guid.NewGuid():N}");

        try
        {
            var result = await service.ExportToDirectoryAsync(tempDirPath);

            Assert.True(Directory.Exists(tempDirPath));
            Assert.Equal("directory", result.Format);
            Assert.True(File.Exists(Path.Combine(tempDirPath, "manifest.json")));
            Assert.True(File.Exists(Path.Combine(tempDirPath, "runtime.json")));
            Assert.True(File.Exists(Path.Combine(tempDirPath, "sqlite.json")));
            Assert.True(File.Exists(Path.Combine(tempDirPath, "sync.json")));
            Assert.True(File.Exists(Path.Combine(tempDirPath, "hardware.json")));
            Assert.True(File.Exists(Path.Combine(tempDirPath, "logs", "sanitized-poscore.log")));

            var manifestText = await File.ReadAllTextAsync(Path.Combine(tempDirPath, "manifest.json"));
            var manifest = JsonSerializer.Deserialize<SupportBundleManifest>(manifestText, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(manifest);
            Assert.True(manifest.IsSanitized);
        }
        finally
        {
            if (Directory.Exists(tempDirPath)) Directory.Delete(tempDirPath, true);
        }
    }

    private sealed class FixedTestClock : IClock
    {
        public DateTimeOffset UtcNow { get; }
        public FixedTestClock(DateTimeOffset utcNow) => UtcNow = utcNow;
    }

    private sealed class TestSupportRepository : ILocalPosRepository, ILocalResilienceRepository
    {
        private readonly LocalSyncQueueHealthSummary _queueHealth;
        private readonly LocalSyncPullState _pullState;
        private readonly LocalOutboxEvent? _latestSyncedEvent;
        private readonly LocalHardwareSummary _hardwareSummary;
        private readonly TerminalBinding? _terminalBinding;
        private readonly LocalIntegrityReport _integrityReport;

        public TestSupportRepository(
            LocalSyncQueueHealthSummary? queueHealth = null,
            LocalSyncPullState? pullState = null,
            LocalOutboxEvent? latestSyncedEvent = null,
            LocalHardwareSummary? hardwareSummary = null,
            TerminalBinding? terminalBinding = null,
            LocalIntegrityReport? integrityReport = null)
        {
            _queueHealth = queueHealth ?? new LocalSyncQueueHealthSummary(0, 0, 0, 0, null, null, false, false);
            _pullState = pullState ?? new LocalSyncPullState(null, null, 0, 0);
            _latestSyncedEvent = latestSyncedEvent;
            _hardwareSummary = hardwareSummary ?? new LocalHardwareSummary(0, 0, 0, 0, null);
            _terminalBinding = terminalBinding;
            _integrityReport = integrityReport ?? new LocalIntegrityReport(
                @"C:\Users\Lucilfer\Documents\SolidPos\solidpos-poscore.local.sqlite",
                true,
                "ok",
                0, 0, 0, 0, 0, 1, 1, 0,
                Array.Empty<LocalIntegrityIssue>());
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveTerminalBindingAsync(TerminalBinding binding, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TerminalBinding?> GetTerminalBindingAsync(CancellationToken cancellationToken = default) => Task.FromResult(_terminalBinding);
        public Task SaveOfflineSaleAsync(OfflineSaleDraft sale, LocalOutboxEvent outboxEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveOfflineSaleWithInventoryAsync(OfflineSaleDraft sale, LocalOutboxEvent outboxEvent, IReadOnlyCollection<LocalInventoryMovement> movements, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveCatalogProductsAsync(IReadOnlyCollection<LocalCatalogProduct> products, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveInventoryRecipeCacheAsync(IReadOnlyCollection<LocalInventoryRecipe> recipes, IReadOnlyCollection<LocalInventoryRecipeItem> recipeItems, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LocalInventoryRecipe?> GetRecipeForOutputAsync(Guid productId, Guid? variantId, CancellationToken cancellationToken = default) => Task.FromResult<LocalInventoryRecipe?>(null);
        public Task<IReadOnlyList<LocalInventoryRecipeItem>> GetRecipeItemsAsync(Guid recipeId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalInventoryRecipeItem>>(Array.Empty<LocalInventoryRecipeItem>());
        public Task<IReadOnlyList<LocalInventoryMovement>> GetInventoryMovementsByLocalSaleIdAsync(Guid localSaleId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalInventoryMovement>>(Array.Empty<LocalInventoryMovement>());
        public Task<int> CountInventoryRecipesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> CountInventoryRecipeItemsAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<LocalCatalogProduct?> GetCatalogProductBySkuAsync(string sku, CancellationToken cancellationToken = default) => Task.FromResult<LocalCatalogProduct?>(null);
        public Task<int> CountCatalogProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task SaveOutboxEventAsync(LocalOutboxEvent outboxEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<LocalOutboxEvent>> GetPendingOutboxEventsAsync(int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalOutboxEvent>>(Array.Empty<LocalOutboxEvent>());
        public Task<LocalOutboxEvent?> GetLatestOutboxEventByStatusAsync(LocalOutboxStatus status, CancellationToken cancellationToken = default) => Task.FromResult(status == LocalOutboxStatus.Synced ? _latestSyncedEvent : null);
        public Task MarkOutboxSyncedAsync(IEnumerable<Guid> eventIds, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MarkOutboxFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResetOutboxEventToPendingAsync(Guid eventId, string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> RetryFailedOutboxEventsAsync(int maxAttempts, string reason, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task SaveSyncAcknowledgementsAsync(IEnumerable<LocalSyncAcknowledgement> acknowledgements, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> CountOutboxByStatusAsync(LocalOutboxStatus status, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<LocalSyncQueueHealthSummary> GetLocalSyncQueueHealthAsync(DateTimeOffset nowUtc, TimeSpan processingTimeout, CancellationToken cancellationToken = default) => Task.FromResult(_queueHealth);
        public Task<LocalSyncPullState> GetSyncPullStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(_pullState);
        public Task<LocalHardwareSummary> GetHardwareSummaryAsync(CancellationToken cancellationToken = default) => Task.FromResult(_hardwareSummary);

        public Task<LocalIntegrityReport> VerifyIntegrityAsync(CancellationToken cancellationToken = default) => Task.FromResult(_integrityReport);
        public Task<LocalRecoveryResult> RepairRuntimeAsync(string reason, bool createBackup, CancellationToken cancellationToken = default) => Task.FromResult(new LocalRecoveryResult(Guid.NewGuid(), 0, 0, 0, 0, false, null, "ok"));
        public Task<LocalBackupResult> CreateBackupAsync(string destinationDirectory, CancellationToken cancellationToken = default) => Task.FromResult(new LocalBackupResult("backup", 0, DateTimeOffset.UtcNow));
        public Task<IReadOnlyList<LocalRecoveryJournalEntry>> GetRecoveryJournalAsync(int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalRecoveryJournalEntry>>(Array.Empty<LocalRecoveryJournalEntry>());
    }
}
