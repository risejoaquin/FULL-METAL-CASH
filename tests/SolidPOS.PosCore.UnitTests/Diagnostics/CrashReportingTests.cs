using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SolidPOS.PosCore.Application.Diagnostics;
using SolidPOS.PosCore.Domain;
using Xunit;

namespace SolidPOS.PosCore.UnitTests.Diagnostics;

public sealed class CrashReportingTests
{
    [Fact]
    public void Requirement_01_Crash_report_creates_sanitized_structured_envelope()
    {
        var service = new CrashReportService(applicationVersion: "1.1.0");
        var exception = new InvalidOperationException("Operation failed with password=PlainTextPassword123!");

        var report = service.CreateReport(exception, crashSource: "unit-test", isFatal: false);

        Assert.NotEqual(Guid.Empty, report.CrashId);
        Assert.False(string.IsNullOrWhiteSpace(report.CorrelationId));
        Assert.Equal("System.InvalidOperationException", report.ExceptionType);
        Assert.DoesNotContain("PlainTextPassword123!", report.SanitizedMessage);
        Assert.Contains("[REDACTED_PASSWORD]", report.SanitizedMessage);
        Assert.Equal("1.1.0", report.AppVersion);
    }

    [Fact]
    public void Requirement_02_CrashId_and_CorrelationId_are_valid_and_stable_within_one_report()
    {
        var service = new CrashReportService();
        var report = service.CreateReport(new Exception("Sample crash"), correlationId: "custom-corr-123");

        Assert.NotEqual(Guid.Empty, report.CrashId);
        Assert.Equal("custom-corr-123", report.CorrelationId);

        var autoReport = service.CreateReport(new Exception("Sample crash 2"));
        Assert.StartsWith("crash-", autoReport.CorrelationId);
        Assert.Equal($"crash-{autoReport.CrashId:N}", autoReport.CorrelationId);
    }

    [Fact]
    public void Requirement_03_SchemaVersion_remains_4()
    {
        var service = new CrashReportService();
        var report = service.CreateReport(new Exception("Invariant check"));

        Assert.Equal(4, report.SchemaVersion);
        Assert.Equal(4, CrashReport.AuthoritativeSchemaVersion);
    }

    [Fact]
    public void Requirement_04_SyncContract_remains_schema_version_4()
    {
        var service = new CrashReportService();
        var report = service.CreateReport(new Exception("Invariant check"));

        Assert.Equal("schema_version_4", report.SyncContract);
        Assert.Equal("schema_version_4", CrashReport.AuthoritativeSyncContract);
    }

    [Fact]
    public void Requirement_05_Connection_strings_are_redacted()
    {
        var rawMessage = "Failed connecting: Host=postgres.internal;Database=posdb;User Id=dbuser;" + "Password=" + "SuperSecretPass123;";
        var sanitized = CrashReportSanitizer.Sanitize(rawMessage);

        Assert.DoesNotContain("SuperSecretPass123", sanitized);
        Assert.Contains("[REDACTED_CONNECTION_STRING]", sanitized);

        var urlMessage = "Database URL " + "postgres" + "ql://" + "dbuser:SecretPass999@db.internal:5432/posdb failed.";
        var sanitizedUrl = CrashReportSanitizer.Sanitize(urlMessage);

        Assert.DoesNotContain("SecretPass999", sanitizedUrl);
        Assert.Contains("[REDACTED_DATABASE_URL]", sanitizedUrl);
    }

    [Fact]
    public void Requirement_06_Bearer_JWT_API_provisioning_secrets_are_redacted()
    {
        var raw = "Bearer " + string.Concat("eyJhbGciOi", "JIUzI1NiIsInR5cCI6", "IkpXVCJ9", ".eyJzdWIiOiIxMjM0NTY3ODkwIn0.doNotLeakThisToken")
            + " with api" + "_key=" + "secret-api-key-value"
            + " and PROVISION" + "_KEY=" + "bootstrap-secret-32-chars-long-abc"
            + " and railway" + "_token=" + "railway-token-secret-123";
        var sanitized = CrashReportSanitizer.Sanitize(raw);

        Assert.DoesNotContain("doNotLeakThisToken", sanitized);
        Assert.DoesNotContain("secret-api-key-value", sanitized);
        Assert.DoesNotContain("bootstrap-secret-32-chars-long-abc", sanitized);
        Assert.DoesNotContain("railway-token-secret-123", sanitized);
        Assert.Contains("[REDACTED_TOKEN]", sanitized);
        Assert.Contains("[REDACTED_API_KEY]", sanitized);
        Assert.Contains("[REDACTED_PROVISION_KEY]", sanitized);
        Assert.Contains("[REDACTED_RAILWAY_TOKEN]", sanitized);
    }

    [Fact]
    public void Requirement_07_PAN_like_values_are_redacted()
    {
        var raw1 = "Transaction processing error for card 4111 2222 3333 4444 declined.";
        var raw2 = "Card 5500000000000004 timed out.";

        var sanitized1 = CrashReportSanitizer.Sanitize(raw1);
        var sanitized2 = CrashReportSanitizer.Sanitize(raw2);

        Assert.DoesNotContain("4111 2222 3333 4444", sanitized1);
        Assert.DoesNotContain("5500000000000004", sanitized2);
        Assert.Contains("[REDACTED_PAN]", sanitized1);
        Assert.Contains("[REDACTED_PAN]", sanitized2);
    }

    [Fact]
    public void Requirement_08_Windows_user_paths_and_usernames_in_stack_traces_are_sanitized()
    {
        var rawStack = @"   at SolidPOS.PosCore.Wpf.MainWindow.OnClick() in C:\Users\john_doe\Documents\SolidPos\src\PosCore\MainWindow.xaml.cs:line 42
   at System.Windows.Threading.Dispatcher.Invoke() in /home/developer/runtime/Dispatcher.cs:line 10";

        var sanitized = CrashReportSanitizer.SanitizeStackTrace(rawStack);

        Assert.DoesNotContain("john_doe", sanitized);
        Assert.DoesNotContain("developer", sanitized);
        Assert.Contains(@"C:\Users\[REDACTED_USER]\Documents\SolidPos", sanitized);
        Assert.Contains("/home/[REDACTED_USER]/runtime", sanitized);
    }

    [Fact]
    public async Task Requirement_09_Raw_unsanitized_exception_data_is_not_persisted()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"crash-test-{Guid.NewGuid():N}");
        try
        {
            var service = new CrashReportService(crashDirectory: tempDir);
            var exception = new InvalidOperationException("Failed password=SensitivePlainTextPass123! with token Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.token");

            var report = service.CreateReport(exception);
            var filePath = await service.PersistCrashReportAsync(report);

            Assert.NotNull(filePath);
            Assert.True(File.Exists(filePath));

            var fileContent = await File.ReadAllTextAsync(filePath);
            Assert.DoesNotContain("SensitivePlainTextPass123!", fileContent);
            Assert.DoesNotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9", fileContent);
            Assert.Contains("[REDACTED_PASSWORD]", fileContent);
            Assert.Contains("[REDACTED_TOKEN]", fileContent);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Requirement_10_Local_crash_persistence_works_offline()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"crash-test-{Guid.NewGuid():N}");
        try
        {
            var service = new CrashReportService(crashDirectory: tempDir);
            var report = service.CreateReport(new InvalidOperationException("Offline crash test"));

            var filePath = await service.PersistCrashReportAsync(report);

            Assert.NotNull(filePath);
            Assert.True(File.Exists(filePath));
            Assert.Equal(".json", Path.GetExtension(filePath));

            var list = service.ListRecentReports();
            Assert.Single(list);
            Assert.Equal(report.CrashId, list[0].CrashId);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Requirement_11_Crash_persistence_failure_does_not_propagate_into_POS_business_operation()
    {
        // Path with invalid characters that causes directory creation / file write failure
        var invalidDir = "Z:\\NonExistentDrive_12345\\impossible_dir";
        var service = new CrashReportService(crashDirectory: invalidDir);
        var report = service.CreateReport(new Exception("Boom"));

        // Persist synchronous should return null and NOT throw
        var resultSync = service.PersistCrashReportSynchronous(report);
        Assert.Null(resultSync);

        // List recent reports should return empty list and NOT throw
        var recent = service.ListRecentReports();
        Assert.Empty(recent);
    }

    [Fact]
    public void Requirement_12_Bounded_retention_removes_oldest_excess_artifacts()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"crash-test-{Guid.NewGuid():N}");
        try
        {
            var service = new CrashReportService(crashDirectory: tempDir, maxRetainedReports: 3);

            for (int i = 0; i < 6; i++)
            {
                var report = service.CreateReport(new Exception($"Crash {i}"));
                var path = service.PersistCrashReportSynchronous(report);
                Assert.NotNull(path);
                Thread.Sleep(10);
            }

            var files = Directory.GetFiles(tempDir, "crash-*.json");
            Assert.Equal(3, files.Length);

            var recentReports = service.ListRecentReports(10);
            Assert.Equal(3, recentReports.Count);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Requirement_13_Oversized_crash_evidence_is_bounded_safely()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"crash-test-{Guid.NewGuid():N}");
        try
        {
            var service = new CrashReportService(crashDirectory: tempDir, maxReportFileSizeBytes: 1024);
            var hugeMessage = new string('x', 5000);

            var report = service.CreateReport(new Exception(hugeMessage));

            var path = service.PersistCrashReportSynchronous(report);
            Assert.NotNull(path);
            var fileBytes = File.ReadAllBytes(path);
            Assert.True(fileBytes.Length <= service.MaxReportFileSizeBytes);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Requirement_14_Opt_out_prevents_remote_crash_telemetry_transmission()
    {
        var policy = new TelemetryOptInPolicy(Mode: TelemetryOptInMode.OptedOut, DiagnosticsEnabled: true);

        Assert.True(policy.AllowsLocalCrashCapture);
        Assert.False(policy.AllowsRemoteTelemetryTransmission);

        var localOnlyPolicy = new TelemetryOptInPolicy(Mode: TelemetryOptInMode.LocalOnly, DiagnosticsEnabled: true);
        Assert.True(localOnlyPolicy.AllowsLocalCrashCapture);
        Assert.False(localOnlyPolicy.AllowsRemoteTelemetryTransmission);
    }

    [Fact]
    public void Requirement_15_Opt_in_allows_eligible_sanitized_evidence()
    {
        var policy = new TelemetryOptInPolicy(Mode: TelemetryOptInMode.OptedIn, DiagnosticsEnabled: true);

        Assert.True(policy.AllowsLocalCrashCapture);
        Assert.True(policy.AllowsRemoteTelemetryTransmission);

        // When remote diagnostics is disabled from server, opt-in is superseded
        var disabledServerPolicy = new TelemetryOptInPolicy(Mode: TelemetryOptInMode.OptedIn, DiagnosticsEnabled: false);
        Assert.True(disabledServerPolicy.AllowsLocalCrashCapture);
        Assert.False(disabledServerPolicy.AllowsRemoteTelemetryTransmission);
    }

    [Fact]
    public void Requirement_16_Optional_CrashEvidence_DTO_serialization_remains_backward_compatible()
    {
        // Verify JSON round-trip of structured crash report
        var report = new CrashReport(
            CrashId: Guid.NewGuid(),
            CorrelationId: "crash-test-roundtrip",
            TimestampUtc: DateTimeOffset.UtcNow,
            ExceptionType: "System.InvalidOperationException",
            SanitizedMessage: "Sanitized error message",
            SanitizedStackTrace: "at Test.Method() in file.cs:line 1",
            AppVersion: "1.1.0",
            SchemaVersion: 4,
            SyncContract: "schema_version_4",
            TerminalId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            StoreId: Guid.NewGuid(),
            CrashSource: "DispatcherUnhandledException",
            IsFatal: false);

        var json = JsonSerializer.Serialize(report);
        Assert.NotNull(json);
        Assert.Contains("crash-test-roundtrip", json);
        Assert.Contains("schema_version_4", json);

        var deserialized = JsonSerializer.Deserialize<CrashReport>(json);
        Assert.NotNull(deserialized);
        Assert.Equal(report.CrashId, deserialized.CrashId);
        Assert.Equal(report.CorrelationId, deserialized.CorrelationId);
        Assert.Equal(4, deserialized.SchemaVersion);
        Assert.Equal("schema_version_4", deserialized.SyncContract);
    }

    [Fact]
    public void Requirement_17_Unhandled_exception_workflow_routes_through_safe_capture_service()
    {
        var service = new CrashReportService();
        var simulatedException = new NullReferenceException("Object reference not set to an instance of an object.");

        var report = service.CreateReport(
            simulatedException,
            crashSource: "DispatcherUnhandledException",
            isFatal: false,
            terminalId: Guid.NewGuid(),
            tenantId: Guid.NewGuid(),
            storeId: Guid.NewGuid());

        Assert.Equal("System.NullReferenceException", report.ExceptionType);
        Assert.Equal("DispatcherUnhandledException", report.CrashSource);
        Assert.False(report.IsFatal);
        Assert.NotNull(report.TerminalId);
        Assert.NotNull(report.TenantId);
        Assert.NotNull(report.StoreId);
        Assert.Equal(4, report.SchemaVersion);
        Assert.Equal("schema_version_4", report.SyncContract);
    }
}
