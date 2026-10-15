using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Storage;

namespace SolidPOS.PosCore.Application.Auth;

/// <summary>
/// Application-internal data from a trusted, authenticated role authority.
/// This is NOT a public wire contract or the existing /auth/login response.
/// A production authority adapter must authenticate and bind tenant, store,
/// terminal and user to a server-issued version before returning this object.
/// </summary>
public sealed record VerifiedPermissionSnapshot(
    Guid TenantId,
    Guid StoreId,
    Guid TerminalId,
    Guid UserId,
    long AuthorityVersion,
    DateTimeOffset IssuedAtUtc,
    bool IsActive,
    string RoleCode,
    IReadOnlyCollection<string> EffectivePermissions);

public interface IVerifiedPermissionSnapshotSource
{
    Task<VerifiedPermissionSnapshot> FetchVerifiedAsync(
        Guid tenantId, Guid storeId, Guid terminalId, Guid userId,
        CancellationToken cancellationToken = default);
}

/// <summary>Only an opt-in storage implementation may atomically replace a cached role grant.</summary>
public interface IAuthoritativePermissionCacheStore
{
    Task ApplyVerifiedPermissionSnapshotAsync(
        VerifiedPermissionSnapshot snapshot, DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The source must already validate central authentication. We refuse
/// mismatched / stale / malformed responses BEFORE they reach SQLite.
/// </summary>
public sealed class AuthoritativePermissionRefreshService
{
    private readonly IVerifiedPermissionSnapshotSource _source;
    private readonly IAuthoritativePermissionCacheStore _cache;
    private readonly IClock _clock;

    public AuthoritativePermissionRefreshService(
        IVerifiedPermissionSnapshotSource source,
        IAuthoritativePermissionCacheStore cache,
        IClock clock)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task RefreshAsync(
        Guid tenantId, Guid storeId, Guid terminalId, Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || storeId == Guid.Empty ||
            terminalId == Guid.Empty || userId == Guid.Empty)
            throw new ArgumentException("Complete tenant/store/terminal/user scope is required.");

        VerifiedPermissionSnapshot snapshot =
            await _source.FetchVerifiedAsync(tenantId, storeId, terminalId, userId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Trusted permission authority did not return a snapshot.");

        DateTimeOffset now = _clock.UtcNow.ToUniversalTime();
        if (snapshot.TenantId != tenantId || snapshot.StoreId != storeId ||
            snapshot.TerminalId != terminalId || snapshot.UserId != userId)
            throw new UnauthorizedAccessException("Permission snapshot scope does not match the authenticated request.");
        if (snapshot.AuthorityVersion <= 0)
            throw new UnauthorizedAccessException("Permission snapshot lacks a monotonic authority version.");
        if (snapshot.IssuedAtUtc > now.AddMinutes(2) ||
            snapshot.IssuedAtUtc < now.AddMinutes(-2))
            throw new UnauthorizedAccessException("Permission snapshot is not recent enough to refresh local authority.");
        if (string.IsNullOrWhiteSpace(snapshot.RoleCode) || snapshot.RoleCode.Length > 64)
            throw new UnauthorizedAccessException("Role code is invalid.");
        if (snapshot.EffectivePermissions is null || snapshot.EffectivePermissions.Count > 128)
            throw new UnauthorizedAccessException("Authority permissions are missing or excessively large.");

        var normalized = new HashSet<string>(StringComparer.Ordinal);
        foreach (string permission in snapshot.EffectivePermissions)
        {
            if (string.IsNullOrWhiteSpace(permission) || permission.Length > 128 ||
                permission != permission.Trim().ToLowerInvariant() ||
                !permission.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-'))
                throw new UnauthorizedAccessException("Authority supplied an invalid permission code.");
            if (!normalized.Add(permission))
                throw new UnauthorizedAccessException("Authority supplied duplicate permissions.");
        }
        if (!snapshot.IsActive && normalized.Count != 0)
            throw new UnauthorizedAccessException("Inactive users cannot be granted effective permissions.");

        await _cache.ApplyVerifiedPermissionSnapshotAsync(
            snapshot, now, cancellationToken).ConfigureAwait(false);
    }
}
