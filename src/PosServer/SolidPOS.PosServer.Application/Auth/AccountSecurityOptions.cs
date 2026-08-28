namespace SolidPOS.PosServer.Application.Auth;

public sealed class AccountSecurityOptions
{
    public const string SectionName = "AccountSecurity";

    public int MaxFailedLoginAttempts { get; init; } = 5;

    public int LockoutMinutes { get; init; } = 15;
}
