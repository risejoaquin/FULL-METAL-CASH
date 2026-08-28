using SolidPOS.PosServer.Contracts.Auth;

namespace SolidPOS.PosServer.Application.Auth;

public interface IAuthService
{
    Task<AuthSessionResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    Task<AuthSessionResponse?> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken);

    Task LogoutAsync(LogoutRequest request, CancellationToken cancellationToken);
}
