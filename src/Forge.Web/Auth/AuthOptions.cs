namespace Forge.Web.Auth;

public class AuthOptions
{
    public const string SectionName = "Auth";

    public string Username { get; set; } = "admin";
    public string Password { get; set; } = "";
    public string PasswordFile { get; set; } = "";
}
