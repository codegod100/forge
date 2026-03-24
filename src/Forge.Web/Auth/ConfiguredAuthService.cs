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
        var configuredPassword = GetConfiguredPassword();
        if (string.IsNullOrWhiteSpace(_options.Username) || string.IsNullOrWhiteSpace(configuredPassword))
        {
            return false;
        }

        return string.Equals(username, _options.Username, StringComparison.Ordinal)
            && string.Equals(password, configuredPassword, StringComparison.Ordinal);
    }

    public string GetConfiguredUsername() => _options.Username;

    private string GetConfiguredPassword()
    {
        if (!string.IsNullOrWhiteSpace(_options.Password))
        {
            return _options.Password;
        }

        if (string.IsNullOrWhiteSpace(_options.PasswordFile))
        {
            return string.Empty;
        }

        try
        {
            return File.Exists(_options.PasswordFile)
                ? File.ReadAllText(_options.PasswordFile).Trim()
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
