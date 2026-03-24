using Microsoft.Extensions.Options;

namespace Forge.Web.Auth;

public class ConfiguredAuthService : IAuthService
{
    private readonly AuthOptions _options;

    public ConfiguredAuthService(IOptions<AuthOptions> options)
    {
        _options = options.Value;
    }

    public bool ValidateCredentials(string? username, string? password)
    {
        if (string.IsNullOrWhiteSpace(_options.Username) || string.IsNullOrWhiteSpace(_options.Password))
        {
            return false;
        }

        return string.Equals(username, _options.Username, StringComparison.Ordinal)
            && string.Equals(password, _options.Password, StringComparison.Ordinal);
    }

    public string GetConfiguredUsername() => _options.Username;
}
