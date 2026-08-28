using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using SolidPOS.PosServer.Application.Auth;
using SolidPOS.PosServer.Infrastructure.Auth;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Auth;

public sealed class JwtTokenServiceTests
{
    [Fact]
    public void Create_access_token_contains_required_tenant_and_user_claims()
    {
        JwtTokenService service = new(Options.Create(new JwtOptions
        {
            Issuer = "SolidPOS.Tests",
            Audience = "SolidPOS.Tests",
            SigningKey = "unit-test-signing-key-with-enough-length"
        }));

        AuthenticatedUser user = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "owner@example.com",
            "Owner",
            "Tenant",
            "active",
            "active",
            "hash");

        string token = service.CreateAccessToken(
            user,
            ["owner"],
            ["tenant.manage"],
            DateTimeOffset.UtcNow.AddMinutes(15));

        JwtSecurityToken parsed = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Contains(parsed.Claims, claim => claim.Type == "tenant_id" && claim.Value == user.TenantId.ToString());
        Assert.Contains(parsed.Claims, claim => claim.Type == "user_id" && claim.Value == user.UserId.ToString());
        Assert.Contains(parsed.Claims, claim => claim.Type == "permissions" && claim.Value == "tenant.manage");
    }

    [Fact]
    public void Create_terminal_access_token_contains_required_terminal_claims()
    {
        JwtTokenService service = new(Options.Create(new JwtOptions
        {
            Issuer = "SolidPOS.Tests",
            Audience = "SolidPOS.Tests",
            SigningKey = "unit-test-signing-key-with-enough-length"
        }));

        AuthenticatedTerminal terminal = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Caja 01",
            "active");

        string token = service.CreateTerminalAccessToken(
            terminal,
            ["sync.push", "sync.pull"],
            DateTimeOffset.UtcNow.AddDays(7));

        JwtSecurityToken parsed = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Contains(parsed.Claims, claim => claim.Type == "tenant_id" && claim.Value == terminal.TenantId.ToString());
        Assert.Contains(parsed.Claims, claim => claim.Type == "store_id" && claim.Value == terminal.StoreId.ToString());
        Assert.Contains(parsed.Claims, claim => claim.Type == "terminal_id" && claim.Value == terminal.TerminalId.ToString());
        Assert.Contains(parsed.Claims, claim => claim.Type == "permissions" && claim.Value == "sync.push");
    }
    [Fact]
    public void Create_refresh_token_uses_url_safe_transport_encoding()
    {
        JwtTokenService service = new(Options.Create(new JwtOptions
        {
            Issuer = "SolidPOS.Tests",
            Audience = "SolidPOS.Tests",
            SigningKey = "unit-test-signing-key-with-enough-length"
        }));

        string token = service.CreateRefreshToken();

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.DoesNotContain("+", token);
        Assert.DoesNotContain("/", token);
        Assert.DoesNotContain("=", token);
        Assert.Matches("^[A-Za-z0-9_-]+$", token);
    }

}
