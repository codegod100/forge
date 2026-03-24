namespace Forge.Web.Auth;

public interface IAuthService
{
    bool ValidateCredentials(string? username, string? password);
    string GetConfiguredUsername();
}
