namespace SolidPOS.PosServer.Application.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = "SolidPOS";

    public string Audience { get; init; } = "SolidPOS.Clients";

    public string SigningKey { get; init; } = string.Empty;

    public int AccessTokenMinutes { get; init; } = 15;

    public int RefreshTokenDays { get; init; } = 30;

    public int TerminalAccessTokenDays { get; init; } = 7;
}
