namespace SolidPOS.PosServer.Application.Auth;

public interface IAuthRepository
{
    Task<AuthenticatedUser?> FindLoginUserAsync(string email, Guid? tenantId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<string>> GetRoleCodesAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<string>> GetPermissionCodesAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);

    Task StoreRefreshTokenAsync(Guid tenantId, Guid userId, string refreshTokenHash, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    Task<AuthenticatedUser?> FindUserByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken cancellationToken);

    Task RotateRefreshTokenAsync(string oldRefreshTokenHash, string newRefreshTokenHash, DateTimeOffset newExpiresAt, CancellationToken cancellationToken);

    Task RevokeRefreshTokenAsync(string refreshTokenHash, CancellationToken cancellationToken);
}
