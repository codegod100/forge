using System.Diagnostics;
using System.Text;
using Forge.Data.Services;

namespace Forge.Web.GitHttp;

public class GitHttpMiddleware
{
    private readonly IRepositoryService _repoService;
    private readonly string _repositoriesRoot;

    public GitHttpMiddleware(IRepositoryService repoService, string repositoriesRoot)
    {
        _repoService = repoService;
        _repositoriesRoot = repositoriesRoot;
    }

    public async Task HandleInfoRefsAsync(HttpContext context, string owner, string repoName, string service)
    {
        var repoPath = Path.Combine(_repositoriesRoot, owner, $"{repoName}.git");
        Console.WriteLine($"[Git] InfoRefs: {repoPath}");
        
        if (!Directory.Exists(repoPath))
        {
            Console.WriteLine($"[Git] Repo not found: {repoPath}");
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync("Repository not found");
            return;
        }

        var repo = await _repoService.GetByOwnerAndNameAsync(owner, repoName);
        if (repo == null)
        {
            Console.WriteLine($"[Git] Repo not in database: {owner}/{repoName}");
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync("Repository not found in database");
            return;
        }

        if (service == "git-receive-pack" && !IsAuthenticated(context))
        {
            context.Response.StatusCode = 401;
            context.Response.Headers.WWWAuthenticate = "Basic realm=\"Forge Git\"";
            return;
        }

        context.Response.ContentType = $"application/x-{service}-advertisement";
        context.Response.Headers.CacheControl = "no-cache";

        // Write header
        await context.Response.Body.WriteAsync(FormatPacketLine($"# service={service}\n"));
        await context.Response.Body.WriteAsync(Encoding.UTF8.GetBytes("0000"));

        // Run git and write output directly to response
        await RunGitToStreamAsync(repoPath, service, "--advertise-refs", context.Response.Body);
    }

    public async Task HandleServiceAsync(HttpContext context, string owner, string repoName, string service)
    {
        var repoPath = Path.Combine(_repositoriesRoot, owner, $"{repoName}.git");
        
        if (!Directory.Exists(repoPath))
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync("Repository not found");
            return;
        }

        var repo = await _repoService.GetByOwnerAndNameAsync(owner, repoName);
        if (repo == null)
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync("Repository not found in database");
            return;
        }

        if (service == "git-receive-pack" && !IsAuthenticated(context))
        {
            context.Response.StatusCode = 401;
            context.Response.Headers.WWWAuthenticate = "Basic realm=\"Forge Git\"";
            return;
        }

        context.Response.ContentType = $"application/x-{service}-result";
        context.Response.Headers.CacheControl = "no-cache";

        using var inputMs = new MemoryStream();
        await context.Request.Body.CopyToAsync(inputMs);
        var input = inputMs.ToArray();

        var result = await RunGitWithInputAsync(repoPath, service, "--stateless-rpc", input);
        await context.Response.Body.WriteAsync(result);
    }

    private bool IsAuthenticated(HttpContext context)
    {
        var authHeader = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Basic "))
            return false;

        try
        {
            var encoded = authHeader["Basic ".Length..].Trim();
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var parts = decoded.Split(':', 2);
            return parts.Length == 2 && !string.IsNullOrEmpty(parts[1]);
        }
        catch
        {
            return false;
        }
    }

    private async Task RunGitToStreamAsync(string repoPath, string command, string args, Stream output)
    {
        // git-upload-pack -> upload-pack, git-receive-pack -> receive-pack
        var subcommand = command.Replace("git-", "");
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"{subcommand} {args} \"{repoPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.EnvironmentVariables["GIT_HTTP_EXPORT_ALL"] = "1";

        Console.WriteLine($"[Git] Running: git {subcommand} {args} \"{repoPath}\"");

        using var process = Process.Start(psi);
        if (process == null)
        {
            Console.WriteLine("[Git] Failed to start process");
            return;
        }

        // Copy stdout directly to output stream
        await process.StandardOutput.BaseStream.CopyToAsync(output);
        
        // Read any errors
        var error = await process.StandardError.ReadToEndAsync();
        if (!string.IsNullOrEmpty(error))
        {
            Console.WriteLine($"[Git] Error: {error}");
        }

        await process.WaitForExitAsync();
        Console.WriteLine($"[Git] Process exited with code {process.ExitCode}");
    }

    private async Task<byte[]> RunGitWithInputAsync(string repoPath, string command, string args, byte[] input)
    {
        var subcommand = command.Replace("git-", "");
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"{subcommand} {args} \"{repoPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.EnvironmentVariables["GIT_HTTP_EXPORT_ALL"] = "1";

        using var process = Process.Start(psi);
        if (process == null)
            return Array.Empty<byte>();

        await process.StandardInput.BaseStream.WriteAsync(input, 0, input.Length);
        process.StandardInput.Close();

        using var ms = new MemoryStream();
        var readTask = process.StandardOutput.BaseStream.CopyToAsync(ms);
        await process.WaitForExitAsync();
        await readTask;

        return ms.ToArray();
    }

    private static byte[] FormatPacketLine(string data)
    {
        var length = data.Length + 4;
        return Encoding.UTF8.GetBytes($"{length:x4}{data}");
    }
}
