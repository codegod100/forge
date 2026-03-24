using LibGit2Sharp;
using Forge.Core.Models;
using LibGit2SharpRepository = LibGit2Sharp.Repository;

namespace Forge.Core.Services;

/// <summary>
/// Git operations using LibGit2Sharp
/// </summary>
public class GitService : IGitService
{
    private readonly string _repositoriesRoot;

    public GitService(string repositoriesRoot)
    {
        _repositoriesRoot = repositoriesRoot;
        Directory.CreateDirectory(_repositoriesRoot);
    }

    public async Task<Models.Repository> InitializeRepositoryAsync(string name, string owner, string? description = null, bool isPrivate = false)
    {
        var path = $"{owner}/{name}.git";
        var fullPath = System.IO.Path.Combine(_repositoriesRoot, path);
        
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        
        // Initialize bare repository
        LibGit2SharpRepository.Init(fullPath, true);

        // Set HEAD to point to main branch (write directly for empty repo)
        File.WriteAllText(System.IO.Path.Combine(fullPath, "HEAD"), "ref: refs/heads/main\n");
        
        var repo = new Models.Repository
        {
            Id = Guid.NewGuid(),
            Name = name,
            Owner = owner,
            Description = description,
            IsPrivate = isPrivate,
            Path = path,
            DefaultBranch = "main"
        };
        
        return await Task.FromResult(repo);
    }

    public async Task<IEnumerable<BranchInfo>> GetBranchesAsync(Models.Repository repository)
    {
        var fullPath = GetFullPath(repository);
        if (!Directory.Exists(fullPath))
            return [];
        
        try
        {
            using var repo = new LibGit2SharpRepository(fullPath);
            
            var result = new List<BranchInfo>();
            foreach (var b in repo.Branches)
            {
                if (b.IsRemote && b.RemoteName != "origin")
                    continue;
                
                try
                {
                    result.Add(new BranchInfo
                    {
                        Name = b.FriendlyName,
                        HeadSha = b.Tip?.Sha ?? "",
                        IsDefault = b.FriendlyName == repository.DefaultBranch,
                        LastCommitDate = b.Tip?.Author.When.UtcDateTime,
                        LastCommitMessage = b.Tip?.MessageShort
                    });
                }
                catch
                {
                    // Skip branches that cause errors
                }
            }
            
            return await Task.FromResult(result.OrderByDescending(b => b.LastCommitDate));
        }
        catch (LibGit2SharpException ex)
        {
            Console.WriteLine($"[Forge] Git error in GetBranchesAsync: {ex.Message}");
            return [];
        }
    }

    public async Task<BranchInfo?> GetBranchAsync(Models.Repository repository, string branchName)
    {
        var fullPath = GetFullPath(repository);
        if (!Directory.Exists(fullPath))
            return null;
        
        try
        {
            using var repo = new LibGit2SharpRepository(fullPath);
            var branch = repo.Branches[branchName];
            
            if (branch == null)
                return null;
            
            return await Task.FromResult(new BranchInfo
            {
                Name = branch.FriendlyName,
                HeadSha = branch.Tip?.Sha ?? "",
                IsDefault = branch.FriendlyName == repository.DefaultBranch,
                LastCommitDate = branch.Tip?.Author.When.UtcDateTime,
                LastCommitMessage = branch.Tip?.MessageShort
            });
        }
        catch (LibGit2SharpException ex)
        {
            Console.WriteLine($"[Forge] Git error in GetBranchAsync: {ex.Message}");
            return null;
        }
    }

    public async Task<BranchInfo?> GetDefaultBranchAsync(Models.Repository repository)
    {
        return await GetBranchAsync(repository, repository.DefaultBranch);
    }

    public async Task<IEnumerable<TreeNode>?> GetTreeAsync(Models.Repository repository, string branch, string? path = null)
    {
        var fullPath = GetFullPath(repository);
        if (!Directory.Exists(fullPath))
            return null;
        
        try
        {
            using var repo = new LibGit2SharpRepository(fullPath);
            
            var commit = ResolveCommitish(repo, branch);
            if (commit == null)
                return null;
            
            var tree = commit.Tree;
            if (tree == null)
                return null;
            
            if (!string.IsNullOrEmpty(path))
            {
                var parts = path.Split('/', '\\');
                foreach (var part in parts)
                {
                    var entry = tree[part];
                    if (entry?.Target is Tree subTree)
                        tree = subTree;
                    else
                        return null; // Path not found or not a directory
                }
            }
            
            // Build list manually to avoid enumeration issues
            var nodes = new List<TreeNode>();
            foreach (var entry in tree)
            {
                long? size = null;
                try
                {
                    if (entry.Target is Blob blob)
                        size = blob.Size;
                }
                catch { /* ignore lookup errors */ }
                
                nodes.Add(new TreeNode
                {
                    Name = entry.Name,
                    Path = string.IsNullOrEmpty(path) ? entry.Name : $"{path}/{entry.Name}",
                    Type = MapEntryType(entry.Mode),
                    Sha = entry.Target.Sha,
                    Size = size
                });
            }
            
            return await Task.FromResult(
                nodes.OrderBy(n => n.Type == TreeEntryType.Directory ? 0 : 1).ThenBy(n => n.Name).ToList()
            );
        }
        catch (LibGit2SharpException ex)
        {
            Console.WriteLine($"[Forge] Git error in GetTreeAsync: {ex.Message}");
            return null;
        }
    }

    public async Task<IEnumerable<TreeNode>> GetAllFilesAsync(Models.Repository repository, string branch)
    {
        var fullPath = GetFullPath(repository);
        if (!Directory.Exists(fullPath))
            return [];
        
        try
        {
            using var repo = new LibGit2SharpRepository(fullPath);
            
            var commit = ResolveCommitish(repo, branch);
            if (commit == null)
                return [];
            
            var nodes = new List<TreeNode>();
            CollectAllFiles(commit.Tree, "", nodes);
            
            return await Task.FromResult(nodes.OrderBy(n => n.Path).ToList());
        }
        catch (LibGit2SharpException ex)
        {
            Console.WriteLine($"[Forge] Git error in GetAllFilesAsync: {ex.Message}");
            return [];
        }
    }

    private static void CollectAllFiles(Tree tree, string basePath, List<TreeNode> nodes)
    {
        foreach (var entry in tree)
        {
            var path = string.IsNullOrEmpty(basePath) ? entry.Name : $"{basePath}/{entry.Name}";
            
            if (entry.Target is Tree subTree)
            {
                CollectAllFiles(subTree, path, nodes);
            }
            else if (entry.Target is Blob blob)
            {
                long? size = null;
                try { size = blob.Size; } catch { }
                
                nodes.Add(new TreeNode
                {
                    Name = entry.Name,
                    Path = path,
                    Type = TreeEntryType.File,
                    Sha = blob.Sha,
                    Size = size
                });
            }
        }
    }

    public async Task<TreeNode?> GetFileAsync(Models.Repository repository, string branch, string path)
    {
        var fullPath = GetFullPath(repository);
        if (!Directory.Exists(fullPath))
            return null;
        
        using var repo = new LibGit2SharpRepository(fullPath);
        
        var commit = ResolveCommitish(repo, branch);
        if (commit == null)
            return null;
        
        var entry = commit[path];
        if (entry == null || entry.Target is not Blob blob)
            return null;
        
        var content = blob.GetContentText();
        
        return await Task.FromResult(new TreeNode
        {
            Name = System.IO.Path.GetFileName(path),
            Path = path,
            Type = TreeEntryType.File,
            Sha = blob.Sha,
            Size = blob.Size,
            Content = content
        });
    }

    public async Task<IEnumerable<CommitInfo>> GetCommitsAsync(Models.Repository repository, string branch, int skip = 0, int take = 50)
    {
        var fullPath = GetFullPath(repository);
        if (!Directory.Exists(fullPath))
            return [];
        
        try
        {
            using var repo = new LibGit2SharpRepository(fullPath);
            
            var commit = ResolveCommitish(repo, branch);
            if (commit == null)
                return [];
            
            var commits = repo.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = commit })
                .Skip(skip)
                .Take(take)
                .Select(c => new CommitInfo
                {
                    Sha = c.Sha,
                    Message = c.Message,
                    Author = c.Author.Name,
                    AuthorEmail = c.Author.Email,
                    AuthorDate = c.Author.When.UtcDateTime,
                    Committer = c.Committer.Name,
                    CommitterEmail = c.Committer.Email,
                    CommitterDate = c.Committer.When.UtcDateTime,
                    ParentSha = c.Parents.FirstOrDefault()?.Sha
                })
                .ToList();
            
            return await Task.FromResult(commits);
        }
        catch (LibGit2SharpException ex)
        {
            Console.WriteLine($"[Forge] Git error in GetCommitsAsync: {ex.Message}");
            return [];
        }
    }

    public async Task<CommitDetail?> GetCommitAsync(Models.Repository repository, string sha)
    {
        var fullPath = GetFullPath(repository);
        if (!Directory.Exists(fullPath))
            return null;
        
        using var repo = new LibGit2SharpRepository(fullPath);
        
        var commit = repo.Lookup<LibGit2Sharp.Commit>(sha);
        if (commit == null)
            return null;
        
        var changes = new List<FileChange>();
        
        // Compare with parent to get changes
        var parent = commit.Parents.FirstOrDefault();
        if (parent != null)
        {
            var diff = repo.Diff.Compare<Patch>(parent.Tree, commit.Tree);
            
            foreach (var patchEntry in diff)
            {
                var changeType = patchEntry.Status switch
                {
                    ChangeKind.Added => ChangeType.Added,
                    ChangeKind.Deleted => ChangeType.Deleted,
                    ChangeKind.Modified => ChangeType.Modified,
                    ChangeKind.Renamed => ChangeType.Renamed,
                    _ => ChangeType.Modified
                };
                
                // Count additions and deletions from patch
                var patch = patchEntry.Patch ?? "";
                var additions = 0;
                var deletions = 0;
                foreach (var line in patch.Split('\n'))
                {
                    if (line.StartsWith('+') && !line.StartsWith("++")) additions++;
                    else if (line.StartsWith('-') && !line.StartsWith("--")) deletions++;
                }
                
                changes.Add(new FileChange
                {
                    Path = patchEntry.Path,
                    ChangeType = changeType,
                    Additions = additions,
                    Deletions = deletions,
                    Diff = patch
                });
            }
        }
        else
        {
            // Initial commit - all files are added
            foreach (var entry in commit.Tree)
            {
                changes.AddRange(GetAllFilesWithContent(entry, "", commit));
            }
        }
        
        return await Task.FromResult(new CommitDetail
        {
            Sha = commit.Sha,
            Message = commit.Message,
            Author = commit.Author.Name,
            AuthorEmail = commit.Author.Email,
            AuthorDate = commit.Author.When.UtcDateTime,
            Committer = commit.Committer.Name,
            CommitterEmail = commit.Committer.Email,
            CommitterDate = commit.Committer.When.UtcDateTime,
            ParentSha = commit.Parents.FirstOrDefault()?.Sha,
            Changes = changes
        });
    }

    public bool RepositoryExists(Models.Repository repository)
    {
        var fullPath = GetFullPath(repository);
        return Directory.Exists(fullPath);
    }

    public void EnsureRepositoryExists(Models.Repository repository)
    {
        var fullPath = GetFullPath(repository);
        if (Directory.Exists(fullPath))
            return;

        // Recreate the bare repository
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        LibGit2SharpRepository.Init(fullPath, true);
        File.WriteAllText(System.IO.Path.Combine(fullPath, "HEAD"), "ref: refs/heads/main\n");
        Console.WriteLine($"[Forge] Recreated missing repository: {repository.Owner}/{repository.Name}");
    }

    public Task<int> ValidateAndRepairRepositoriesAsync(IEnumerable<Models.Repository> repositories)
    {
        int repaired = 0;
        foreach (var repo in repositories)
        {
            if (!RepositoryExists(repo))
            {
                EnsureRepositoryExists(repo);
                repaired++;
            }
        }
        return Task.FromResult(repaired);
    }

    public async Task<IEnumerable<BlameLine>> GetBlameAsync(Models.Repository repository, string branch, string path)
    {
        var fullPath = GetFullPath(repository);
        if (!Directory.Exists(fullPath))
            return [];
        
        try
        {
            using var repo = new LibGit2SharpRepository(fullPath);
            
            var commit = ResolveCommitish(repo, branch);
            if (commit == null)
                return [];
            
            var entry = commit[path];
            if (entry == null || entry.Target is not Blob blob)
                return [];
            
            var blame = repo.Blame(path, new BlameOptions { StartingAt = commit });
            var content = blob.GetContentText();
            var lines = content.Split('\n');
            
            var result = new List<BlameLine>();
            
            for (int i = 0; i < lines.Length; i++)
            {
                var lineNum = i + 1;
                var lineContent = lines[i];
                
                // Find the blame hunk for this line
                var hunk = blame.FirstOrDefault(h =>
                    h.FinalStartLineNumber <= lineNum &&
                    lineNum < h.FinalStartLineNumber + h.LineCount);
                
                if (hunk != null)
                {
                    var blamedCommit = hunk.FinalCommit;
                    result.Add(new BlameLine
                    {
                        LineNumber = lineNum,
                        Content = lineContent,
                        CommitSha = blamedCommit.Sha,
                        Author = hunk.FinalSignature.Name,
                        Date = hunk.FinalSignature.When.UtcDateTime,
                        Message = blamedCommit.MessageShort
                    });
                }
                else
                {
                    // Fallback for lines without blame info
                    result.Add(new BlameLine
                    {
                        LineNumber = lineNum,
                        Content = lineContent,
                        CommitSha = commit.Sha,
                        Author = commit.Author.Name,
                        Date = commit.Author.When.UtcDateTime,
                        Message = commit.MessageShort
                    });
                }
            }
            
            return await Task.FromResult(result);
        }
        catch (LibGit2SharpException ex)
        {
            Console.WriteLine($"[Forge] Git error in GetBlameAsync: {ex.Message}");
            return [];
        }
    }

    private string GetFullPath(Models.Repository repository)
    {
        return System.IO.Path.Combine(_repositoriesRoot, repository.Path);
    }

    private static LibGit2Sharp.Commit? ResolveCommitish(LibGit2SharpRepository repo, string revision)
    {
        var branch = repo.Branches[revision];
        if (branch?.Tip != null)
        {
            return branch.Tip;
        }

        return repo.Lookup<LibGit2Sharp.Commit>(revision);
    }

    private static TreeEntryType MapEntryType(Mode mode)
    {
        return mode switch
        {
            Mode.Directory => TreeEntryType.Directory,
            Mode.SymbolicLink => TreeEntryType.Symlink,
            _ => TreeEntryType.File
        };
    }

    private static IEnumerable<FileChange> GetAllFilesWithContent(TreeEntry entry, string basePath, LibGit2Sharp.Commit commit)
    {
        var path = string.IsNullOrEmpty(basePath) ? entry.Name : $"{basePath}/{entry.Name}";
        
        if (entry.Target is Tree tree)
        {
            return tree.SelectMany(e => GetAllFilesWithContent(e, path, commit));
        }
        
        // Get file content for initial commit diff
        var content = entry.Target is Blob blob ? blob.GetContentText() : "";
        var lines = content.Split('\n').Length;
        
        return [new FileChange
        {
            Path = path,
            ChangeType = ChangeType.Added,
            Additions = lines,
            Deletions = 0,
            Diff = string.IsNullOrEmpty(content) ? null : content.Split('\n').Select(l => $"+{l}").Aggregate((a, b) => $"{a}\n{b}")
        }];
    }

    private static IEnumerable<FileChange> GetAllFiles(TreeEntry entry, string basePath)
    {
        var path = string.IsNullOrEmpty(basePath) ? entry.Name : $"{basePath}/{entry.Name}";
        
        if (entry.Target is Tree tree)
        {
            return tree.SelectMany(e => GetAllFiles(e, path));
        }
        
        return [new FileChange
        {
            Path = path,
            ChangeType = ChangeType.Added,
            Additions = 0,
            Deletions = 0
        }];
    }
}
