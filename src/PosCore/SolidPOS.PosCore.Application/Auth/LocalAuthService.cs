using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.Auth;

public sealed class LocalAuthService
{
    private readonly ILocalPosRepository _repository;
    private readonly IClock _clock;
    private readonly OfflineAccessPolicyService _policy = new();

    public LocalAuthService(ILocalPosRepository repository, IClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<LocalSession> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var user = await _repository.GetLocalUserByEmailAsync(email, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Invalid local credentials.");
        var now = _clock.UtcNow;
        if (!_policy.IsWithinOfflineWindow(user, now))
        {
            await _repository.LogLocalAuditEventAsync(new LocalAuditEvent(Guid.NewGuid(), user.TenantId, user.StoreId, user.UserId, null, "local.auth.blocked_offline_window", $"Offline window expired for {user.Email}.", now), cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("Offline login blocked because local auth cache is older than the allowed offline window.");
        }
        if (!LocalPasswordHasher.Verify(password, user.PasswordHash))
        {
            await _repository.LogLocalAuditEventAsync(new LocalAuditEvent(Guid.NewGuid(), user.TenantId, user.StoreId, user.UserId, null, "local.auth.failed", $"Invalid local credentials for {user.Email}.", now), cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("Invalid local credentials.");
        }

        var session = new LocalSession(Guid.NewGuid(), user.UserId, user.TenantId, user.StoreId, user.Email, user.DisplayName, user.RoleCode, now, now.AddHours(Math.Min(user.MaxOfflineHours, 12)), "active");
        await _repository.CreateLocalSessionAsync(session, cancellationToken).ConfigureAwait(false);
        await _repository.LogLocalAuditEventAsync(new LocalAuditEvent(Guid.NewGuid(), user.TenantId, user.StoreId, user.UserId, session.SessionId, "local.auth.login", $"Local login succeeded for {user.Email}.", now), cancellationToken).ConfigureAwait(false);
        return session;
    }

    public async Task RequirePermissionAsync(Guid sessionId, string permissionCode, CancellationToken cancellationToken = default)
    {
        var session = await _repository.GetLocalSessionAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Local session not found: {sessionId}.");
        if (session.Status != "active" || session.ExpiresAtUtc <= _clock.UtcNow)
        {
            throw new InvalidOperationException("Local session is not active.");
        }

        bool allowed = await _repository.LocalUserHasPermissionAsync(session.UserId, permissionCode, cancellationToken).ConfigureAwait(false);
        if (!allowed)
        {
            throw new InvalidOperationException($"Local permission denied: {permissionCode}.");
        }
    }

    public async Task LogoutAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _repository.GetLocalSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        await _repository.CloseLocalSessionAsync(sessionId, _clock.UtcNow, cancellationToken).ConfigureAwait(false);
        if (session is not null)
        {
            await _repository.LogLocalAuditEventAsync(new LocalAuditEvent(Guid.NewGuid(), session.TenantId, session.StoreId, session.UserId, session.SessionId, "local.auth.logout", $"Local logout for {session.Email}.", _clock.UtcNow), cancellationToken).ConfigureAwait(false);
        }
    }
}
