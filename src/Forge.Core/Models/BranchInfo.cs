namespace Forge.Core.Models;

/// <summary>
/// Represents a branch in a repository
/// </summary>
public class BranchInfo
{
    public required string Name { get; set; }
    public required string HeadSha { get; set; }
    public bool IsDefault { get; set; }
    public DateTime? LastCommitDate { get; set; }
    public string? LastCommitMessage { get; set; }
}
