namespace SolidPOS.PosServer.Contracts.Terminals;

public sealed record TerminalDeviceHealthDto(
    string? BatteryStatus = null,
    int? BatteryLevelPercent = null,
    long? AvailableDiskSpaceBytes = null,
    long? TotalDiskSpaceBytes = null,
    long? MemoryUsageBytes = null,
    string? CpuArchitecture = null,
    string? OsVersion = null,
    bool? IsStorageHealthy = null,
    DateTimeOffset? ReportedAtUtc = null);
