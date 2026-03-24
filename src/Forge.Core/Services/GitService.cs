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
        
        using var repo = new LibGit2SharpRepository(fullPath);
        
        var branches = repo.Branches
            .Where(b => !b.IsRemote || b.RemoteName == "origin")
            .Select(b => new BranchInfo
            {
                Name = b.FriendlyName,
                HeadSha = b.Tip?.Sha ?? "",
                IsDefault = b.FriendlyName == repository.DefaultBranch,
                LastCommitDate = b.Tip?.Author.When.DateTime,
                LastCommitMessage = b.Tip?.MessageShort
            })
            .OrderByDescending(b => b.LastCommitDate);
        
        return await Task.FromResult(branches);
    }

    public async Task<BranchInfo?> GetBranchAsync(Models.Repository repository, string branchName)
    {
        var fullPath = GetFullPath(repository);
        if (!Directory.Exists(fullPath))
            return null;
        
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

    public async Task<BranchInfo?> GetDefaultBranchAsync(Models.Repository repository)
    {
        return await GetBranchAsync(repository, repository.DefaultBranch);
    }

    public async Task<IEnumerable<TreeNode>> GetTreeAsync(Models.Repository repository, string branch, string? path = null)
    {
        var fullPath = GetFullPath(repository);
        if (!Directory.Exists(fullPath))
            return [];
        
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
        
        var nodes = tree.Select(entry => new TreeNode
        {
            Name = entry.Name,
            Path = string.IsNullOrEmpty(path) ? entry.Name : $"{path}/{entry.Name}",
            Type = MapEntryType(entry.Mode),
            Sha = entry.Target.Sha,
            Size = entry.Target is Blob blob ? blob.Size : null
        }).OrderBy(n => n.Type == TreeEntryType.Directory ? 0 : 1).ThenBy(n => n.Name);
        
        return await Task.FromResult(nodes);
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
            });
        
        return await Task.FromResult(commits);
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
