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
                        LastCommitDate = b.Tip?.Author.When.DateTime,
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
                LastCommitDate = branch.Tip?.Author.When.DateTime,
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

    public async Task<IEnumerable<TreeNode>> GetTreeAsync(Models.Repository repository, string branch, string? path = null)
    {
        var fullPath = GetFullPath(repository);
        if (!Directory.Exists(fullPath))
            return [];
        
        try
        {
            using var repo = new LibGit2SharpRepository(fullPath);
            
            var gitBranch = repo.Branches[branch];
            if (gitBranch == null)
                return [];
            
            var tree = gitBranch.Tip?.Tree;
            if (tree == null)
                return [];
            
            if (!string.IsNullOrEmpty(path))
            {
                var parts = path.Split('/', '\\');
                foreach (var part in parts)
                {
                    var entry = tree[part];
                    if (entry?.Target is Tree subTree)
                        tree = subTree;
                    else
                        return [];
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
            return [];
        }
    }

    public async Task<TreeNode?> GetFileAsync(Models.Repository repository, string branch, string path)
    {
        var fullPath = GetFullPath(repository);
        if (!Directory.Exists(fullPath))
            return null;
        
        using var repo = new LibGit2SharpRepository(fullPath);
        
        var gitBranch = repo.Branches[branch];
        if (gitBranch == null)
            return null;
        
        var entry = gitBranch.Tip?[path];
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
            
            var gitBranch = repo.Branches[branch];
            if (gitBranch == null)
                return [];
            
            var commits = gitBranch.Commits
                .Skip(skip)
                .Take(take)
                .Select(c => new CommitInfo
                {
                    Sha = c.Sha,
                    Message = c.Message,
                    Author = c.Author.Name,
                    AuthorEmail = c.Author.Email,
                    AuthorDate = c.Author.When.DateTime,
                    Committer = c.Committer.Name,
                    CommitterEmail = c.Committer.Email,
                    CommitterDate = c.Committer.When.DateTime,
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
            var diff = repo.Diff.Compare<TreeChanges>(parent.Tree, commit.Tree);
            
            foreach (var added in diff.Added)
            {
                changes.Add(new FileChange
                {
                    Path = added.Path,
                    ChangeType = ChangeType.Added,
                    Additions = 0,
                    Deletions = 0
                });
            }
            
            foreach (var modified in diff.Modified)
            {
                changes.Add(new FileChange
                {
                    Path = modified.Path,
                    ChangeType = ChangeType.Modified,
                    Additions = 0,
                    Deletions = 0
                });
            }
            
            foreach (var deleted in diff.Deleted)
            {
                changes.Add(new FileChange
                {
                    Path = deleted.Path,
                    ChangeType = ChangeType.Deleted,
                    Additions = 0,
                    Deletions = 0
                });
            }
            
            foreach (var renamed in diff.Renamed)
            {
                changes.Add(new FileChange
                {
                    Path = renamed.Path,
                    ChangeType = ChangeType.Renamed,
                    Additions = 0,
                    Deletions = 0
                });
            }
        }
        else
        {
            // Initial commit - all files are added
            foreach (var entry in commit.Tree)
            {
                changes.AddRange(GetAllFiles(entry, ""));
            }
        }
        
        return await Task.FromResult(new CommitDetail
        {
            Sha = commit.Sha,
            Message = commit.Message,
            Author = commit.Author.Name,
            AuthorEmail = commit.Author.Email,
            AuthorDate = commit.Author.When.DateTime,
            Committer = commit.Committer.Name,
            CommitterEmail = commit.Committer.Email,
            CommitterDate = commit.Committer.When.DateTime,
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

    private string GetFullPath(Models.Repository repository)
    {
        return System.IO.Path.Combine(_repositoriesRoot, repository.Path);
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
