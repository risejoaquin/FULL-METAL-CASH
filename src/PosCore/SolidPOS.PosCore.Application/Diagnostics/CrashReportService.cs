using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.Diagnostics;

public sealed class CrashReportService
{
    public const string DefaultCrashDirectory = ".\\.runtime\\crashes";
    public const int DefaultMaxRetainedReports = 20;
    public const int DefaultMaxReportFileSizeBytes = 65536;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _crashDirectory;
    private readonly int _maxRetainedReports;
    private readonly int _maxReportFileSizeBytes;
    private readonly string _applicationVersion;

    public CrashReportService(
        string? crashDirectory = null,
        int maxRetainedReports = DefaultMaxRetainedReports,
        int maxReportFileSizeBytes = DefaultMaxReportFileSizeBytes,
        string? applicationVersion = null)
    {
        _crashDirectory = string.IsNullOrWhiteSpace(crashDirectory) ? DefaultCrashDirectory : crashDirectory;
        _maxRetainedReports = maxRetainedReports > 0 ? maxRetainedReports : DefaultMaxRetainedReports;
        _maxReportFileSizeBytes = maxReportFileSizeBytes > 0 ? maxReportFileSizeBytes : DefaultMaxReportFileSizeBytes;
        _applicationVersion = string.IsNullOrWhiteSpace(applicationVersion) ? ResolveAppVersion() : applicationVersion;
    }

    public string CrashDirectory => _crashDirectory;
    public int MaxRetainedReports => _maxRetainedReports;
    public int MaxReportFileSizeBytes => _maxReportFileSizeBytes;

    public CrashReport CreateReport(
        Exception exception,
        string crashSource = "manual",
        bool isFatal = false,
        string? correlationId = null,
        Guid? terminalId = null,
        Guid? tenantId = null,
        Guid? storeId = null,
        Guid? sessionId = null,
        IReadOnlyDictionary<string, string>? context = null)
    {
        var crashId = Guid.NewGuid();
        var resolvedCorrelationId = string.IsNullOrWhiteSpace(correlationId) ? $"crash-{crashId:N}" : correlationId;
        var sanitizedMessage = CrashReportSanitizer.SanitizeMessage(exception?.Message);
        var sanitizedStackTrace = CrashReportSanitizer.SanitizeStackTrace(exception?.StackTrace);
        var sanitizedMetadata = CrashReportSanitizer.SanitizeMetadata(context);

        return new CrashReport(
            CrashId: crashId,
            CorrelationId: resolvedCorrelationId,
            TimestampUtc: DateTimeOffset.UtcNow,
            ExceptionType: exception?.GetType().FullName ?? "UnknownException",
            SanitizedMessage: sanitizedMessage,
            SanitizedStackTrace: sanitizedStackTrace,
            AppVersion: _applicationVersion,
            SchemaVersion: CrashReport.AuthoritativeSchemaVersion,
            SyncContract: CrashReport.AuthoritativeSyncContract,
            TerminalId: terminalId,
            TenantId: tenantId,
            StoreId: storeId,
            SessionId: sessionId,
            CrashSource: crashSource,
            IsFatal: isFatal,
            Metadata: sanitizedMetadata);
    }

    public async Task<string?> PersistCrashReportAsync(
        CrashReport report,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(_crashDirectory);

            var fileName = $"crash-{report.TimestampUtc:yyyyMMddHHmmssfff}-{report.CrashId:N}.json";
            var filePath = Path.Combine(_crashDirectory, fileName);

            var json = SerializeBounded(report);

            await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
            PruneOldReports();
            return filePath;
        }
        catch
        {
            // Defensive failure isolation: telemetry or persistence failures must NEVER throw
            return null;
        }
    }

    public string? PersistCrashReportSynchronous(CrashReport report)
    {
        try
        {
            Directory.CreateDirectory(_crashDirectory);

            var fileName = $"crash-{report.TimestampUtc:yyyyMMddHHmmssfff}-{report.CrashId:N}.json";
            var filePath = Path.Combine(_crashDirectory, fileName);

            var json = SerializeBounded(report);

            File.WriteAllText(filePath, json);
            PruneOldReports();
            return filePath;
        }
        catch
        {
            // Defensive failure isolation
            return null;
        }
    }

    private string SerializeBounded(CrashReport report)
    {
        var json = JsonSerializer.Serialize(report, JsonOptions);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > _maxReportFileSizeBytes)
        {
            var maxFieldLength = Math.Max(128, (_maxReportFileSizeBytes - 600) / 2);
            var boundedReport = report with
            {
                SanitizedMessage = report.SanitizedMessage.Length > maxFieldLength
                    ? report.SanitizedMessage[..maxFieldLength] + "...[BOUNDED]"
                    : report.SanitizedMessage,
                SanitizedStackTrace = report.SanitizedStackTrace.Length > maxFieldLength
                    ? report.SanitizedStackTrace[..maxFieldLength] + "...[STACK_BOUNDED]"
                    : report.SanitizedStackTrace
            };
            json = JsonSerializer.Serialize(boundedReport, JsonOptions);
        }
        return json;
    }

    public IReadOnlyList<CrashReport> ListRecentReports(int limit = 20)
    {
        var list = new List<CrashReport>();
        try
        {
            if (!Directory.Exists(_crashDirectory)) return list;

            var files = new DirectoryInfo(_crashDirectory)
                .GetFiles("crash-*.json")
                .OrderByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .Take(limit);

            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file.FullName);
                    var report = JsonSerializer.Deserialize<CrashReport>(json, JsonOptions);
                    if (report is not null) list.Add(report);
                }
                catch
                {
                    // Ignore corrupted report read
                }
            }
        }
        catch
        {
            // Defensive isolation
        }

        return list;
    }

    public void PruneOldReports()
    {
        try
        {
            if (!Directory.Exists(_crashDirectory)) return;

            var files = new DirectoryInfo(_crashDirectory)
                .GetFiles("crash-*.json")
                .OrderByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count > _maxRetainedReports)
            {
                var filesToDelete = files.Skip(_maxRetainedReports);
                foreach (var file in filesToDelete)
                {
                    try
                    {
                        file.Delete();
                    }
                    catch
                    {
                        // Best-effort cleanup
                    }
                }
            }
        }
        catch
        {
            // Best-effort cleanup isolation
        }
    }

    private static string ResolveAppVersion()
    {
        return Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
            ?? "1.1.0";
    }
}
