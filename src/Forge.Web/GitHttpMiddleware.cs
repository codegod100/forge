using System.Diagnostics;
using System.Globalization;
using System.Text;
using Forge.Core.Services;
using Forge.Data.Services;
using Forge.Web.Auth;

namespace Forge.Web.GitHttp;

public class GitHttpMiddleware
{
    private readonly IRepositoryService _repoService;
    private readonly IGitService _gitService;
    private readonly IAuthService _authService;
    private readonly string _repositoriesRoot;

    public GitHttpMiddleware(IRepositoryService repoService, IGitService gitService, IAuthService authService, string repositoriesRoot)
    {
        _repoService = repoService;
        _gitService = gitService;
        _authService = authService;
        _repositoriesRoot = repositoriesRoot;
    }

    public async Task HandleAsync(HttpContext context, string owner, string repoName)
    {
        var service = GetRequestedService(context);
        var repo = await EnsureRepositoryForRequestAsync(context, owner, repoName, service);
        if (repo == null)
        {
            return;
        }

        if ((repo.IsPrivate || service == "git-receive-pack") && !IsAuthenticated(context))
        {
            context.Response.StatusCode = 401;
            context.Response.Headers.WWWAuthenticate = "Basic realm=\"Forge Git\"";
            return;
        }

        await RunGitHttpBackendAsync(context, owner, repoName);
    }

    private async Task<Forge.Core.Models.Repository?> EnsureRepositoryForRequestAsync(HttpContext context, string owner, string repoName, string? service)
    {
        var repoPath = Path.Combine(_repositoriesRoot, owner, $"{repoName}.git");
        var repo = await _repoService.GetByOwnerAndNameAsync(owner, repoName);

        if (repo != null)
        {
            if (!Directory.Exists(repoPath))
            {
                _gitService.EnsureRepositoryExists(repo);
            }

            return repo;
        }

        if (Directory.Exists(repoPath))
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync("Repository not found in database");
            return null;
        }

        if (service != "git-receive-pack")
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync("Repository not found");
            return null;
        }

        var credentials = GetBasicCredentials(context);
        if (credentials == null || !_authService.ValidateCredentials(credentials.Value.Username, credentials.Value.Password))
        {
            context.Response.StatusCode = 401;
            context.Response.Headers.WWWAuthenticate = "Basic realm=\"Forge Git\"";
            return null;
        }

        if (!string.Equals(credentials.Value.Username, owner, StringComparison.Ordinal))
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsync("You can only create repositories for your own owner.");
            return null;
        }

        repo = await _gitService.InitializeRepositoryAsync(repoName, owner);
        await _repoService.CreateAsync(repo);
        return repo;
    }

    private static string? GetRequestedService(HttpContext context)
    {
        if (HttpMethods.IsGet(context.Request.Method))
        {
            return context.Request.Query["service"].ToString();
        }

        var path = context.Request.Path.Value ?? string.Empty;
        if (path.EndsWith("/git-upload-pack", StringComparison.Ordinal))
        {
            return "git-upload-pack";
        }

        if (path.EndsWith("/git-receive-pack", StringComparison.Ordinal))
        {
            return "git-receive-pack";
        }

        return null;
    }

    private bool IsAuthenticated(HttpContext context)
    {
        var credentials = GetBasicCredentials(context);
        return credentials != null && _authService.ValidateCredentials(credentials.Value.Username, credentials.Value.Password);
    }

    private (string Username, string Password)? GetBasicCredentials(HttpContext context)
    {
        var authHeader = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Basic "))
            return null;

        try
        {
            var encoded = authHeader["Basic ".Length..].Trim();
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var parts = decoded.Split(':', 2);
            return parts.Length == 2 ? (parts[0], parts[1]) : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task RunGitHttpBackendAsync(HttpContext context, string owner, string repoName)
    {
        var pathInfo = context.Request.Path.Value?[($"/{owner}/{repoName}.git").Length..] ?? string.Empty;
        if (string.IsNullOrEmpty(pathInfo))
        {
            pathInfo = "/";
        }

        var queryString = context.Request.QueryString.HasValue
            ? context.Request.QueryString.Value?.TrimStart('?') ?? string.Empty
            : string.Empty;

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = "http-backend",
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        ApplyGitHttpEnvironment(context, psi, owner, repoName, pathInfo, queryString);

        Console.WriteLine($"[Git] Running: git http-backend PATH_INFO=/{owner}/{repoName}.git{pathInfo}");

        using var process = Process.Start(psi);
        if (process == null)
        {
            Console.WriteLine("[Git] Failed to start process");
            return;
        }

        var stdoutTask = CopyToMemoryAsync(process.StandardOutput.BaseStream);
        var stderrTask = process.StandardError.ReadToEndAsync();
        var inputTask = CopyRequestBodyAsync(context, process);

        await inputTask;
        var output = await stdoutTask;
        var error = await stderrTask;
        await process.WaitForExitAsync();

        if (!string.IsNullOrEmpty(error))
        {
            Console.WriteLine($"[Git] Error: {error}");
        }

        Console.WriteLine($"[Git] Process exited with code {process.ExitCode}");
        if (process.ExitCode != 0)
        {
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("Git HTTP backend failed");
            return;
        }

        await WriteBackendResponseAsync(context, output);
    }

    private static async Task CopyRequestBodyAsync(HttpContext context, Process process)
    {
        await context.Request.Body.CopyToAsync(process.StandardInput.BaseStream);
        await process.StandardInput.BaseStream.FlushAsync();
        process.StandardInput.Close();
    }

    private static async Task<byte[]> CopyToMemoryAsync(Stream stream)
    {
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    private static async Task WriteBackendResponseAsync(HttpContext context, byte[] output)
    {
        var headerTerminatorLength = TryFindHeaderTerminator(output, out var headerEndIndex);
        if (headerTerminatorLength == 0)
        {
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("Invalid git HTTP backend response");
            return;
        }

        var headerText = Encoding.ASCII.GetString(output, 0, headerEndIndex);
        var headerLines = headerText.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in headerLines)
        {
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0) continue;

            var headerName = line[..separatorIndex].Trim();
            var headerValue = line[(separatorIndex + 1)..].Trim();

            if (string.Equals(headerName, "Status", StringComparison.OrdinalIgnoreCase))
            {
                var parts = headerValue.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && int.TryParse(parts[0], out var statusCode))
                {
                    context.Response.StatusCode = statusCode;
                }
                continue;
            }

            context.Response.Headers[headerName] = headerValue;
        }

        var bodyStartIndex = headerEndIndex + headerTerminatorLength;
        var bodyLength = output.Length - bodyStartIndex;
        if (bodyLength > 0)
        {
            await context.Response.Body.WriteAsync(output.AsMemory(bodyStartIndex, bodyLength));
        }
    }

    private static int TryFindHeaderTerminator(byte[] data, out int headerEndIndex)
    {
        for (var i = 0; i < data.Length - 3; i++)
        {
            if (data[i] == '\r' && data[i + 1] == '\n' && data[i + 2] == '\r' && data[i + 3] == '\n')
            {
                headerEndIndex = i;
                return 4;
            }
        }

        for (var i = 0; i < data.Length - 1; i++)
        {
            if (data[i] == '\n' && data[i + 1] == '\n')
            {
                headerEndIndex = i;
                return 2;
            }
        }

        headerEndIndex = -1;
        return 0;
    }

    private void ApplyGitHttpEnvironment(HttpContext context, ProcessStartInfo psi, string owner, string repoName, string pathInfo, string queryString)
    {
        psi.EnvironmentVariables["GIT_HTTP_EXPORT_ALL"] = "1";
        psi.EnvironmentVariables["GIT_PROJECT_ROOT"] = _repositoriesRoot;
        psi.EnvironmentVariables["PATH_INFO"] = $"/{owner}/{repoName}.git{pathInfo}";
        psi.EnvironmentVariables["REQUEST_METHOD"] = context.Request.Method;
        psi.EnvironmentVariables["QUERY_STRING"] = queryString;
        psi.EnvironmentVariables["CONTENT_TYPE"] = context.Request.ContentType ?? string.Empty;
        psi.EnvironmentVariables["REMOTE_USER"] = GetBasicCredentials(context)?.Username ?? string.Empty;
        psi.EnvironmentVariables["REMOTE_ADDR"] = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;

        if (context.Request.ContentLength.HasValue)
        {
            psi.EnvironmentVariables["CONTENT_LENGTH"] = context.Request.ContentLength.Value.ToString(CultureInfo.InvariantCulture);
        }

        foreach (var header in context.Request.Headers)
        {
            var envName = $"HTTP_{header.Key.ToUpperInvariant().Replace('-', '_')}";
            psi.EnvironmentVariables[envName] = header.Value.ToString();
        }

        var gitProtocol = context.Request.Headers["Git-Protocol"].ToString();
        if (!string.IsNullOrWhiteSpace(gitProtocol))
        {
            psi.EnvironmentVariables["GIT_PROTOCOL"] = gitProtocol;
        }
    }
}
