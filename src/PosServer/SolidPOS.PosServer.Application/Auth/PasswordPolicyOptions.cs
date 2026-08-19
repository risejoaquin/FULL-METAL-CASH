namespace SolidPOS.PosServer.Application.Auth;

public sealed class PasswordPolicyOptions
{
    public const string SectionName = "PasswordPolicy";

    public int MinLength { get; init; } = 12;

    public bool RequireUppercase { get; init; } = true;

    public bool RequireLowercase { get; init; } = true;

    public bool RequireDigit { get; init; } = true;

    public bool RequireNonAlphanumeric { get; init; } = true;

    public bool BlockCommonPasswords { get; init; } = true;
}
