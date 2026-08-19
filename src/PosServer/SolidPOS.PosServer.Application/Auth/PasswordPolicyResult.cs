namespace SolidPOS.PosServer.Application.Auth;

public sealed record PasswordPolicyResult(bool IsValid, IReadOnlyCollection<string> Errors)
{
    public static PasswordPolicyResult Valid { get; } = new(true, Array.Empty<string>());
}
