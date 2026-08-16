namespace SolidPOS.PosServer.Application.Auth;

public sealed class PasswordHashingOptions
{
    public const string SectionName = "PasswordHashing";

    public int WorkFactor { get; init; } = 12;
}
