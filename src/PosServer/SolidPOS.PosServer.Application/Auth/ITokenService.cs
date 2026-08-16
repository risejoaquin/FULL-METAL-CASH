namespace SolidPOS.PosServer.Application.Auth;

public interface ITokenService
{
    string CreateAccessToken(AuthenticatedUser user, IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions, DateTimeOffset expiresAt);

    string CreateTerminalAccessToken(AuthenticatedTerminal terminal, IReadOnlyCollection<string> permissions, DateTimeOffset expiresAt);

    string CreateRefreshToken();

    string HashToken(string token);
}
