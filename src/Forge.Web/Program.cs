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

// Git Smart HTTP endpoints
app.MapMethods("/{owner}/{repo}.git/{**rest}", new[] { "GET", "POST" }, async (HttpContext context, string owner, string repo,
    [FromServices] GitHttpMiddleware git) =>
{
    await git.HandleAsync(context, owner, repo);
});

app.Run();
