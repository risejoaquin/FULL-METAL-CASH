namespace SolidPOS.PosServer.Contracts.Terminals;

public sealed record TerminalHeartbeatRequest(
    string? AppVersion = null,
    int? LocalDbVersion = null,
    int? PendingOutboxCount = null,
    string? LastSyncCursor = null,
    TerminalDeviceHealthDto? DeviceHealth = null);
