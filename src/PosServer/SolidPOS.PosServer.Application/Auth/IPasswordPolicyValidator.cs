namespace SolidPOS.PosServer.Application.Auth;

public interface IPasswordPolicyValidator
{
    PasswordPolicyResult Validate(string password);
}
