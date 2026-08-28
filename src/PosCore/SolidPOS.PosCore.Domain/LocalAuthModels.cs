namespace SolidPOS.PosCore.Domain;

public sealed record LocalUser(
    Guid UserId,
    Guid TenantId,
    Guid StoreId,
    string Email,
    string DisplayName,
    string PasswordHash,
    string RoleCode,
    bool IsActive,
    DateTimeOffset LastSyncedAtUtc,
    int MaxOfflineHours);

public sealed record LocalUserPermission(
    Guid UserId,
    string PermissionCode,
    DateTimeOffset SyncedAtUtc);

public sealed record LocalSession(
    Guid SessionId,
    Guid UserId,
    Guid TenantId,
    Guid StoreId,
    string Email,
    string DisplayName,
    string RoleCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string Status);

public sealed record LocalAuditEvent(
    Guid Id,
    Guid TenantId,
    Guid StoreId,
    Guid? UserId,
    Guid? SessionId,
    string EventType,
    string Message,
    DateTimeOffset OccurredAtUtc);

public sealed record LocalAuthSummary(
    int UserCount,
    int PermissionCount,
    int ActiveSessionCount,
    int AuditEventCount,
    DateTimeOffset? LastSyncedAtUtc);
