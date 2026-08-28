using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SolidPOS.PosServer.Application.Auth;
using SolidPOS.PosServer.Application.Abstractions.Time;
using SolidPOS.PosServer.Contracts.Auth;

namespace SolidPOS.PosServer.Infrastructure.Auth;

public sealed class AuthService : IAuthService
{
    private readonly IAuthRepository _repository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IClock _clock;
    private readonly JwtOptions _jwtOptions;
    private readonly AccountSecurityOptions _accountSecurityOptions;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IAuthRepository repository,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IClock clock,
        IOptions<JwtOptions> jwtOptions,
        IOptions<AccountSecurityOptions> accountSecurityOptions,
        ILogger<AuthService> logger)
    {
        _repository = repository;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _clock = clock;
        _jwtOptions = jwtOptions.Value;
        _accountSecurityOptions = accountSecurityOptions.Value;
        _logger = logger;
    }

    public async Task<AuthSessionResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        AuthenticatedUser? user = await _repository.FindLoginUserAsync(request.Email, request.TenantId, cancellationToken);

        if (user is null)
        {
            _passwordHasher.VerifyDummy(request.Password);
            _logger.LogWarning("Login failed");
            return null;
        }

        if (user.LockedUntil is not null && user.LockedUntil > _clock.UtcNow)
        {
            _passwordHasher.VerifyDummy(request.Password);
            _logger.LogWarning("Login rejected for locked account {UserId} tenant {TenantId}", user.UserId, user.TenantId);
            return null;
        }

        if (user.UserStatus != "active" || user.TenantStatus != "active")
        {
            _passwordHasher.VerifyDummy(request.Password);
            _logger.LogWarning("Login failed");
            return null;
        }

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            DateTimeOffset lockoutUntil = _clock.UtcNow.AddMinutes(Math.Max(1, _accountSecurityOptions.LockoutMinutes));
            await _repository.RecordFailedLoginAsync(
                user.TenantId,
                user.UserId,
                Math.Max(1, _accountSecurityOptions.MaxFailedLoginAttempts),
                lockoutUntil,
                cancellationToken);
            _logger.LogWarning("Login failed for user {UserId} tenant {TenantId}", user.UserId, user.TenantId);
            return null;
        }

        await _repository.ResetLoginFailuresAsync(user.TenantId, user.UserId, cancellationToken);

        IReadOnlyCollection<string> roles = await _repository.GetRoleCodesAsync(user.TenantId, user.UserId, cancellationToken);
        IReadOnlyCollection<string> permissions = await _repository.GetPermissionCodesAsync(user.TenantId, user.UserId, cancellationToken);

        AuthSessionResponse session = await CreateSessionAsync(user, roles, permissions, cancellationToken);
        _logger.LogInformation("Login succeeded for user {UserId} tenant {TenantId}", user.UserId, user.TenantId);
        return session;
    }

    public async Task<AuthSessionResponse?> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        if (!request.TenantId.HasValue)
        {
            _logger.LogWarning("Refresh token rejected because tenantId was not supplied");
            return null;
        }

        string oldHash = _tokenService.HashToken(request.RefreshToken);
        string refreshToken = _tokenService.CreateRefreshToken();
        string newHash = _tokenService.HashToken(refreshToken);
        DateTimeOffset refreshExpiresAt = _clock.UtcNow.AddDays(_jwtOptions.RefreshTokenDays);

        AuthenticatedUser? user = await _repository.RotateRefreshTokenAndFindUserAsync(
            request.TenantId.Value,
            oldHash,
            newHash,
            refreshExpiresAt,
            cancellationToken);

        if (user is null || user.UserStatus != "active" || user.TenantStatus != "active")
        {
            _logger.LogWarning("Refresh token rejected for tenant {TenantId}", request.TenantId.Value);
            return null;
        }

        if (user.LockedUntil is not null && user.LockedUntil > _clock.UtcNow)
        {
            _logger.LogWarning("Refresh token rejected for locked account {UserId} tenant {TenantId}", user.UserId, user.TenantId);
            return null;
        }

        IReadOnlyCollection<string> roles = await _repository.GetRoleCodesAsync(user.TenantId, user.UserId, cancellationToken);
        IReadOnlyCollection<string> permissions = await _repository.GetPermissionCodesAsync(user.TenantId, user.UserId, cancellationToken);

        DateTimeOffset accessExpiresAt = _clock.UtcNow.AddMinutes(_jwtOptions.AccessTokenMinutes);
        string accessToken = _tokenService.CreateAccessToken(user, roles, permissions, accessExpiresAt);

        _logger.LogInformation("Refresh token rotated for user {UserId} tenant {TenantId}", user.UserId, user.TenantId);

        return new AuthSessionResponse(
            accessToken,
            refreshToken,
            accessExpiresAt,
            new AuthUserResponse(user.UserId, user.Email, user.Name, roles, permissions),
            new AuthTenantResponse(user.TenantId, user.TenantName, user.TenantStatus));
    }

    public async Task LogoutAsync(LogoutRequest request, CancellationToken cancellationToken)
    {
        string hash = _tokenService.HashToken(request.RefreshToken);
        await _repository.RevokeRefreshTokenAsync(request.TenantId, hash, cancellationToken);
        _logger.LogInformation("Logout completed");
    }

    private async Task<AuthSessionResponse> CreateSessionAsync(
        AuthenticatedUser user,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        DateTimeOffset accessExpiresAt = _clock.UtcNow.AddMinutes(_jwtOptions.AccessTokenMinutes);
        DateTimeOffset refreshExpiresAt = _clock.UtcNow.AddDays(_jwtOptions.RefreshTokenDays);
        string accessToken = _tokenService.CreateAccessToken(user, roles, permissions, accessExpiresAt);
        string refreshToken = _tokenService.CreateRefreshToken();
        string refreshHash = _tokenService.HashToken(refreshToken);

        await _repository.StoreRefreshTokenAsync(user.TenantId, user.UserId, refreshHash, refreshExpiresAt, cancellationToken);

        return new AuthSessionResponse(
            accessToken,
            refreshToken,
            accessExpiresAt,
            new AuthUserResponse(user.UserId, user.Email, user.Name, roles, permissions),
            new AuthTenantResponse(user.TenantId, user.TenantName, user.TenantStatus));
    }
}
