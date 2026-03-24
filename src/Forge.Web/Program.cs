using Forge.Web.Components;
using Forge.Web.GitHttp;
using Forge.Data;
using Forge.Data.Services;
using Forge.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options => options.DetailedErrors = true);

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
        reposRoot
    )
);

var app = builder.Build();

// Global error handling for development
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] {ex.Message}");
        Console.WriteLine($"[ERROR] {ex.StackTrace}");
        throw;
    }
});

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ForgeDbContext>();
    db.Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Git Smart HTTP endpoints
app.MapGet("/{owner}/{repo}.git/info/refs", async (HttpContext context, string owner, string repo, 
    [FromServices] GitHttpMiddleware git) =>
{
    var service = context.Request.Query["service"].ToString();
    if (service == "git-upload-pack" || service == "git-receive-pack")
    {
        await git.HandleInfoRefsAsync(context, owner, repo, service);
    }
    else
    {
        context.Response.StatusCode = 400;
    }
});

app.MapPost("/{owner}/{repo}.git/git-upload-pack", async (HttpContext context, string owner, string repo,
    [FromServices] GitHttpMiddleware git) =>
{
    await git.HandleServiceAsync(context, owner, repo, "git-upload-pack");
});

app.MapPost("/{owner}/{repo}.git/git-receive-pack", async (HttpContext context, string owner, string repo,
    [FromServices] GitHttpMiddleware git) =>
{
    await git.HandleServiceAsync(context, owner, repo, "git-receive-pack");
});

app.Run();
