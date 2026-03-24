using Forge.Core.Models;

namespace Forge.Core.Services;

/// <summary>
/// Service for interacting with git repositories
/// </summary>
public interface IGitService
{
    /// <summary>
    /// Initialize a new bare repository at the given path
    /// </summary>
    Task<Repository> InitializeRepositoryAsync(string name, string owner, string? description = null, bool isPrivate = false);
    
    /// <summary>
    /// Get all branches in a repository
    /// </summary>
    Task<IEnumerable<BranchInfo>> GetBranchesAsync(Repository repository);
    
    /// <summary>
    /// Get a specific branch
    /// </summary>
    Task<BranchInfo?> GetBranchAsync(Repository repository, string branchName);
    
    /// <summary>
    /// Get the default branch for a repository
    /// </summary>
    Task<BranchInfo?> GetDefaultBranchAsync(Repository repository);
    
    /// <summary>
    /// Get tree entries at a path in a branch. Returns null if branch/path not found.
    /// </summary>
    Task<IEnumerable<TreeNode>?> GetTreeAsync(Repository repository, string branch, string? path = null);
    
    /// <summary>
    /// Get all files in a repository recursively
    /// </summary>
    Task<IEnumerable<TreeNode>> GetAllFilesAsync(Repository repository, string branch);
    
    /// <summary>
    /// Get a single file's content
    /// </summary>
    Task<TreeNode?> GetFileAsync(Repository repository, string branch, string path);
    
    /// <summary>
    /// Get commit history for a branch
    /// </summary>
    Task<IEnumerable<CommitInfo>> GetCommitsAsync(Repository repository, string branch, int skip = 0, int take = 50);
    
    /// <summary>
    /// Get a specific commit with details
    /// </summary>
    Task<CommitDetail?> GetCommitAsync(Repository repository, string sha);
    
    /// <summary>
    /// Check if a repository exists on disk
    /// </summary>
    bool RepositoryExists(Repository repository);

    /// <summary>
    /// Ensure a repository exists on disk, creating it if missing
    /// </summary>
    void EnsureRepositoryExists(Repository repository);

    /// <summary>
    /// Validate all repositories in database exist on disk, repair any missing
    /// </summary>
    Task<int> ValidateAndRepairRepositoriesAsync(IEnumerable<Repository> repositories);

    /// <summary>
    /// Get blame information for a file
    /// </summary>
    Task<IEnumerable<BlameLine>> GetBlameAsync(Repository repository, string branch, string path);
}
