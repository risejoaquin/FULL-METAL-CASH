namespace SolidPOS.PosServer.Contracts.Terminals;

public sealed record TerminalDetailResponse(
    Guid Id,
    Guid TenantId,
    Guid StoreId,
    string Name,
    string Fingerprint,
    string Status,
    string? AppVersion,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? HardLockedAt = null,
    string? HardLockReason = null,
    TerminalDeviceHealthDto? DeviceHealth = null,
    TerminalRemoteConfigMetadata? RemoteConfig = null);
