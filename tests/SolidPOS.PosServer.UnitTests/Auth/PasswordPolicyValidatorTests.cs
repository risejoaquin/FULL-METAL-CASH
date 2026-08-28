using Microsoft.Extensions.Options;
using SolidPOS.PosServer.Application.Auth;
using SolidPOS.PosServer.Infrastructure.Auth;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Auth;

public sealed class PasswordPolicyValidatorTests
{
    [Fact]
    public void Validate_rejects_common_demo_password()
    {
        PasswordPolicyValidator validator = new(Options.Create(new PasswordPolicyOptions()));

        PasswordPolicyResult result = validator.Validate("Admin123!");

        Assert.False(result.IsValid);
        Assert.Contains("PASSWORD_TOO_SHORT", result.Errors);
        Assert.Contains("PASSWORD_COMMON_OR_DEMO_VALUE", result.Errors);
    }

    [Fact]
    public void Validate_accepts_strong_password()
    {
        PasswordPolicyValidator validator = new(Options.Create(new PasswordPolicyOptions()));

        PasswordPolicyResult result = validator.Validate("CorrectHorseBatteryStaple2026!");

        Assert.True(result.IsValid);
    }
}
