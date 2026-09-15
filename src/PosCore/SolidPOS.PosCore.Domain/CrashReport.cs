using System;
using System.Collections.Generic;

namespace SolidPOS.PosCore.Domain;

public sealed record CrashReport(
    Guid CrashId,
    string CorrelationId,
    DateTimeOffset TimestampUtc,
    string ExceptionType,
    string SanitizedMessage,
    string SanitizedStackTrace,
    string AppVersion,
    int SchemaVersion = 4,
    string SyncContract = "schema_version_4",
    Guid? TerminalId = null,
    Guid? TenantId = null,
    Guid? StoreId = null,
    Guid? SessionId = null,
    string? CrashSource = null,
    bool IsFatal = false,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public const int AuthoritativeSchemaVersion = 4;
    public const string AuthoritativeSyncContract = "schema_version_4";
}

public enum TelemetryOptInMode
{
    OptedOut = 0,
    LocalOnly = 1,
    OptedIn = 2
}

public sealed record TelemetryOptInPolicy(
    TelemetryOptInMode Mode = TelemetryOptInMode.LocalOnly,
    bool DiagnosticsEnabled = true,
    DateTimeOffset? ConfiguredAtUtc = null)
{
    public bool AllowsLocalCrashCapture => true;

    public bool AllowsRemoteTelemetryTransmission =>
        Mode == TelemetryOptInMode.OptedIn && DiagnosticsEnabled;
}
