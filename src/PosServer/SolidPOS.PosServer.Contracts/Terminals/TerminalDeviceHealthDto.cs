namespace SolidPOS.PosServer.Contracts.Terminals;

public sealed record UpdateHealthEvidenceDto(
    string State,
    string CurrentVersion,
    string? TargetVersion = null,
    string? Channel = null,
    string? PackageFileName = null,
    string? PackageSha256 = null,
    bool? IsSigned = null,
    string? SigningThumbprint = null,
    string? ErrorMessage = null,
    DateTimeOffset? AttemptedAtUtc = null,
    DateTimeOffset? CompletedAtUtc = null,
    string? RollbackVersion = null,
    string? RollbackReason = null);

public sealed record CrashReportEvidenceDto(
    Guid CrashId,
    string CorrelationId,
    DateTimeOffset OccurredAtUtc,
    string ExceptionType,
    string SanitizedMessage,
    string? CrashSource = null,
    bool? IsFatal = null);

public sealed record TerminalDeviceHealthDto(
    string? BatteryStatus = null,
    int? BatteryLevelPercent = null,
    long? AvailableDiskSpaceBytes = null,
    long? TotalDiskSpaceBytes = null,
    long? MemoryUsageBytes = null,
    string? CpuArchitecture = null,
    string? OsVersion = null,
    bool? IsStorageHealthy = null,
    DateTimeOffset? ReportedAtUtc = null,
    UpdateHealthEvidenceDto? UpdateHealth = null,
    CrashReportEvidenceDto? CrashEvidence = null,
    IReadOnlyList<CrashReportEvidenceDto>? RecentCrashes = null);
