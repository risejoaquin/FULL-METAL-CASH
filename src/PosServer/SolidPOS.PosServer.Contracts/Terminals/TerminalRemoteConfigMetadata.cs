namespace SolidPOS.PosServer.Contracts.Terminals;

public sealed record TerminalRemoteConfigMetadata(
    int HeartbeatIntervalSeconds = 60,
    bool DiagnosticsEnabled = true,
    string LogLevel = "Information",
    int SyncPollingIntervalSeconds = 30,
    int OfflineGracePeriodMinutes = 1440,
    DateTimeOffset? UpdatedAtUtc = null);
