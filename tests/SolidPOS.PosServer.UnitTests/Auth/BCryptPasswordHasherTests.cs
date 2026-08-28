using Microsoft.Extensions.Options;
using SolidPOS.PosServer.Application.Auth;
using SolidPOS.PosServer.Infrastructure.Auth;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Auth;

public sealed class BCryptPasswordHasherTests
{
    [Fact]
    public void Hash_and_verify_password_roundtrip()
    {
        BCryptPasswordHasher hasher = new(Options.Create(new PasswordHashingOptions
        {
            WorkFactor = 10
        }));

        string hash = hasher.Hash("CorrectHorseBatteryStaple!");

        Assert.True(hasher.Verify("CorrectHorseBatteryStaple!", hash));
        Assert.False(hasher.Verify("wrong-password", hash));
    }
}
