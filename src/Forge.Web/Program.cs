using Forge.Web.Components;
using Forge.Web.GitHttp;
using Forge.Web.Auth;
using Forge.Web.Services;
using Forge.Data;
using Forge.Data.Services;
using Forge.Core.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Fido2NetLib;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options => options.DetailedErrors = true);
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.AddSingleton<IAuthService, ConfiguredAuthService>();
builder.Services.AddSingleton<MarkdownService>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

// Configure database
var dbPath = builder.Configuration.GetValue<string>("Database:Path") 
    ?? Path.Combine(builder.Environment.ContentRootPath, "forge.db");
builder.Services.AddDbContext<ForgeDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Configure git repositories root
var reposRoot = builder.Configuration.GetValue<string>("Repositories:Root")
    ?? Path.Combine(builder.Environment.ContentRootPath, "repositories");
Directory.CreateDirectory(reposRoot);

// Register services
builder.Services.AddSingleton<IGitService>(sp => new GitService(reposRoot));
builder.Services.AddScoped<IRepositoryService, RepositoryService>();

// Configure Fido2
var baseUrl = builder.Configuration["BaseUrl"] ?? "http://localhost:5128";
var fido2Config = new Fido2Configuration
{
    ServerDomain = new Uri(baseUrl).Host,
    ServerName = "Forge",
    Origins = new HashSet<string> { baseUrl.TrimEnd('/') },
    TimestampDriftTolerance = 300000
};
builder.Services.AddSingleton<IFido2>(sp => new Fido2NetLib.Fido2(fido2Config));
builder.Services.AddScoped<IFido2Service, Fido2Service>();

// Git HTTP middleware
builder.Services.AddScoped<GitHttpMiddleware>(sp => 
    new GitHttpMiddleware(
        sp.GetRequiredService<IRepositoryService>(),
        sp.GetRequiredService<IGitService>(),
        sp.GetRequiredService<IAuthService>(),
        reposRoot
    )
);

var app = builder.Build();

// Ensure database is created and validate repositories
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ForgeDbContext>();
    db.Database.EnsureCreated();
    
    // Ensure PasskeyCredentials table exists (for existing databases)
    var connection = db.Database.GetDbConnection();
    await connection.OpenAsync();
    using var cmd = connection.CreateCommand();
    cmd.CommandText = """
        CREATE TABLE IF NOT EXISTS PasskeyCredentials (
            Id TEXT PRIMARY KEY,
            Username TEXT NOT NULL,
            CredentialId BLOB NOT NULL UNIQUE,
            PublicKey BLOB NOT NULL,
            SignCount INTEGER NOT NULL,
            Name TEXT,
            CreatedAt TEXT NOT NULL,
            LastUsedAt TEXT
        );
        CREATE INDEX IF NOT EXISTS IX_PasskeyCredentials_Username ON PasskeyCredentials (Username);
        CREATE INDEX IF NOT EXISTS IX_PasskeyCredentials_CredentialId ON PasskeyCredentials (CredentialId);
        """;
    await cmd.ExecuteNonQueryAsync();

    // Validate all repos in DB exist on disk, repair any missing
    var gitService = scope.ServiceProvider.GetRequiredService<IGitService>();
    var repoService = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
    var repos = await repoService.GetAllAsync();
    var repaired = await gitService.ValidateAndRepairRepositoriesAsync(repos);
    if (repaired > 0)
    {
        Console.WriteLine($"[Forge] Repaired {repaired} missing repositories");
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapPost("/auth/login", async (HttpContext context, [FromServices] IAuthService authService) =>
{
    var form = await context.Request.ReadFormAsync();
    var username = form["username"].ToString();
    var password = form["password"].ToString();
    var returnUrl = form["returnUrl"].ToString();

    if (!authService.ValidateCredentials(username, password))
    {
        var target = string.IsNullOrWhiteSpace(returnUrl) ? "/login?error=true" : $"/login?error=true&returnUrl={Uri.EscapeDataString(returnUrl)}";
        return Results.LocalRedirect(target);
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.Name, username),
        new(ClaimTypes.Role, "Admin")
    };

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);

    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

    if (!string.IsNullOrWhiteSpace(returnUrl) && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative) && returnUrl.StartsWith('/'))
    {
        return Results.LocalRedirect(returnUrl);
    }

    return Results.LocalRedirect("/admin");
});

app.MapPost("/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("/");
});

// WebAuthn / Passkey endpoints
app.MapPost("/auth/passkey/register/start", async (
    HttpContext context,
    [FromServices] IFido2Service fido2Service,
    [FromServices] IAuthService authService) =>
{
    var username = authService.GetConfiguredUsername();
    if (string.IsNullOrEmpty(username))
    {
        return Results.BadRequest(new { error = "No user configured" });
    }
    
    var options = await fido2Service.StartRegistrationAsync(username);
    
    // Return options as JSON (Fido2NetLib handles serialization)
    return Results.Json(options);
});

app.MapPost("/auth/passkey/register/complete", async (
    HttpContext context,
    [FromServices] IFido2Service fido2Service,
    [FromServices] IAuthService authService) =>
{
    var username = authService.GetConfiguredUsername();
    if (string.IsNullOrEmpty(username))
    {
        return Results.BadRequest(new { error = "No user configured" });
    }
    
    try
    {
        // Parse JSON manually to avoid base64url conversion issues
        using var reader = new StreamReader(context.Request.Body);
        var json = await reader.ReadToEndAsync();
        var node = System.Text.Json.Nodes.JsonNode.Parse(json);
        
        var response = new Fido2NetLib.AuthenticatorAttestationRawResponse
        {
            Id = node!["id"]!.GetValue<string>(),
            RawId = Base64Url.Decode(node["rawId"]!.GetValue<string>()),
            Type = Fido2NetLib.Objects.PublicKeyCredentialType.PublicKey,
            Response = new Fido2NetLib.AuthenticatorAttestationRawResponse.AttestationResponse
            {
                ClientDataJson = Base64Url.Decode(node["response"]!["clientDataJSON"]!.GetValue<string>()),
                AttestationObject = Base64Url.Decode(node["response"]!["attestationObject"]!.GetValue<string>()),
                Transports = []
            },
            ClientExtensionResults = new Fido2NetLib.Objects.AuthenticationExtensionsClientOutputs()
        };
        
        var deviceName = node["deviceName"]?.GetValue<string>();
        var credential = await fido2Service.CompleteRegistrationAsync(username, response, deviceName);
        return Results.Json(new { success = true, credentialId = credential.Id });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/auth/passkey/authenticate/start", async (
    [FromServices] IFido2Service fido2Service) =>
{
    var options = await fido2Service.StartAuthenticationAsync();
    return Results.Json(options);
});

app.MapPost("/auth/passkey/authenticate/complete", async (
    HttpContext context,
    [FromServices] IFido2Service fido2Service,
    [FromServices] IAuthService authService) =>
{
    try
    {
        // Parse JSON manually to avoid base64url conversion issues
        using var reader = new StreamReader(context.Request.Body);
        var json = await reader.ReadToEndAsync();
        var node = System.Text.Json.Nodes.JsonNode.Parse(json);
        
        var response = new Fido2NetLib.AuthenticatorAssertionRawResponse
        {
            Id = node!["id"]!.GetValue<string>(),
            RawId = Base64Url.Decode(node["rawId"]!.GetValue<string>()),
            Type = Fido2NetLib.Objects.PublicKeyCredentialType.PublicKey,
            Response = new Fido2NetLib.AuthenticatorAssertionRawResponse.AssertionResponse
            {
                ClientDataJson = Base64Url.Decode(node["response"]!["clientDataJSON"]!.GetValue<string>()),
                AuthenticatorData = Base64Url.Decode(node["response"]!["authenticatorData"]!.GetValue<string>()),
                Signature = Base64Url.Decode(node["response"]!["signature"]!.GetValue<string>()),
                UserHandle = node["response"]?["userHandle"]?.GetValue<string>() is string uh ? Base64Url.Decode(uh) : null
            },
            ClientExtensionResults = new Fido2NetLib.Objects.AuthenticationExtensionsClientOutputs()
        };
        
        var username = await fido2Service.CompleteAuthenticationAsync(response);
        
        if (username == null)
        {
            return Results.Json(new { success = false, error = "Authentication failed" });
        }
        
        // Sign the user in
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Role, "Admin")
        };
        
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        
        return Results.Json(new { success = true });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/auth/passkey/credentials", async (
    [FromServices] IFido2Service fido2Service,
    [FromServices] IAuthService authService) =>
{
    var username = authService.GetConfiguredUsername();
    if (string.IsNullOrEmpty(username))
    {
        return Results.BadRequest(new { error = "No user configured" });
    }
    
    var credentials = await fido2Service.GetCredentialsAsync(username);
    return Results.Json(credentials.Select(c => new {
        c.Id,
        c.Name,
        c.CreatedAt,
        c.LastUsedAt
    }));
});

app.MapDelete("/auth/passkey/credentials/{id}", async (
    Guid id,
    [FromServices] IFido2Service fido2Service,
    [FromServices] IAuthService authService) =>
{
    var username = authService.GetConfiguredUsername();
    if (string.IsNullOrEmpty(username))
    {
        return Results.BadRequest(new { error = "No user configured" });
    }
    
    await fido2Service.DeleteCredentialAsync(id, username);
    return Results.Json(new { success = true });
});

// Git Smart HTTP endpoints
app.MapMethods("/{owner}/{repo}.git/{**rest}", new[] { "GET", "POST" }, async (HttpContext context, string owner, string repo,
    [FromServices] GitHttpMiddleware git) =>
{
    await git.HandleAsync(context, owner, repo);
});

app.Run();

file static class Base64Url
{
    public static byte[] Decode(string s) => 
        Convert.FromBase64String(s.Replace('-', '+').Replace('_', '/').PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
}
