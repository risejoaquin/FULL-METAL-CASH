using Microsoft.Extensions.Options;
using SolidPOS.PosServer.Application.Auth;

namespace SolidPOS.PosServer.Infrastructure.Auth;

public sealed class PasswordPolicyValidator : IPasswordPolicyValidator
{
    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "password1",
        "admin",
        "admin123",
        "admin123!",
        "qwerty",
        "qwerty123",
        "letmein",
        "welcome",
        "solidpos",
        "solidpos123"
    };

    private readonly PasswordPolicyOptions _options;

    public PasswordPolicyValidator(IOptions<PasswordPolicyOptions> options)
    {
        _options = options.Value;
    }

    public PasswordPolicyResult Validate(string password)
    {
        List<string> errors = [];

        if (string.IsNullOrWhiteSpace(password))
        {
            errors.Add("PASSWORD_REQUIRED");
            return new PasswordPolicyResult(false, errors);
        }

        if (password.Length < _options.MinLength)
        {
            errors.Add("PASSWORD_TOO_SHORT");
        }

        if (_options.RequireUppercase && !password.Any(char.IsUpper))
        {
            errors.Add("PASSWORD_REQUIRES_UPPERCASE");
        }

        if (_options.RequireLowercase && !password.Any(char.IsLower))
        {
            errors.Add("PASSWORD_REQUIRES_LOWERCASE");
        }

        if (_options.RequireDigit && !password.Any(char.IsDigit))
        {
            errors.Add("PASSWORD_REQUIRES_DIGIT");
        }

        if (_options.RequireNonAlphanumeric && !password.Any(ch => !char.IsLetterOrDigit(ch)))
        {
            errors.Add("PASSWORD_REQUIRES_NON_ALPHANUMERIC");
        }

        if (_options.BlockCommonPasswords && CommonPasswords.Contains(password.Trim()))
        {
            errors.Add("PASSWORD_COMMON_OR_DEMO_VALUE");
        }

        return errors.Count == 0 ? PasswordPolicyResult.Valid : new PasswordPolicyResult(false, errors);
    }
}
