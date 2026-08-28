using Microsoft.Extensions.Options;
using SolidPOS.PosServer.Application.Auth;

namespace SolidPOS.PosServer.Infrastructure.Auth;

public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private readonly PasswordHashingOptions _options;
    private readonly string _dummyHash;

    public BCryptPasswordHasher(IOptions<PasswordHashingOptions> options)
    {
        _options = options.Value;
        _dummyHash = BCrypt.Net.BCrypt.HashPassword("solidpos-dummy-password", _options.WorkFactor);
    }

    public string Hash(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, _options.WorkFactor);
    }

    public bool Verify(string password, string passwordHash)
    {
        return BCrypt.Net.BCrypt.Verify(password, passwordHash);
    }

    public void VerifyDummy(string password)
    {
        BCrypt.Net.BCrypt.Verify(password, _dummyHash);
    }
}
