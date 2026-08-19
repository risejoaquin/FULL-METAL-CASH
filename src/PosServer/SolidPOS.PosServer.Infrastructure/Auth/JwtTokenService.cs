using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SolidPOS.PosServer.Application.Auth;

namespace SolidPOS.PosServer.Infrastructure.Auth;

public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public string CreateAccessToken(
        AuthenticatedUser user,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions,
        DateTimeOffset expiresAt)
    {
        SymmetricSecurityKey key = new(Encoding.UTF8.GetBytes(_options.SigningKey));
        SigningCredentials credentials = new(key, SecurityAlgorithms.HmacSha256);

        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new("user_id", user.UserId.ToString()),
            new("tenant_id", user.TenantId.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email)
        ];

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(roles.Select(role => new Claim("roles", role)));
        claims.AddRange(permissions.Select(permission => new Claim("permissions", permission)));

        JwtSecurityToken token = new(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string CreateTerminalAccessToken(
        AuthenticatedTerminal terminal,
        IReadOnlyCollection<string> permissions,
        DateTimeOffset expiresAt)
    {
        SymmetricSecurityKey key = new(Encoding.UTF8.GetBytes(_options.SigningKey));
        SigningCredentials credentials = new(key, SecurityAlgorithms.HmacSha256);

        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, terminal.TerminalId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new("terminal_id", terminal.TerminalId.ToString()),
            new("tenant_id", terminal.TenantId.ToString()),
            new("store_id", terminal.StoreId.ToString()),
            new("terminal_name", terminal.Name)
        ];

        claims.AddRange(permissions.Select(permission => new Claim("permissions", permission)));

        JwtSecurityToken token = new(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string CreateRefreshToken()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    public string HashToken(string token)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
