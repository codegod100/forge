namespace Forge.Core.Models;

/// <summary>
/// Represents commit information from a git repository
/// </summary>
public class CommitInfo
{
    public required string Sha { get; set; }
    public required string Message { get; set; }
    public required string Author { get; set; }
    public required string AuthorEmail { get; set; }
    public DateTime AuthorDate { get; set; }
    public required string Committer { get; set; }
    public required string CommitterEmail { get; set; }
    public DateTime CommitterDate { get; set; }
    public string? ParentSha { get; set; }
}

/// <summary>
/// Detailed commit info with file changes
/// </summary>
public class CommitDetail : CommitInfo
{
    public List<FileChange> Changes { get; set; } = [];
}

public class FileChange
{
    public required string Path { get; set; }
    public required ChangeType ChangeType { get; set; }
    public int Additions { get; set; }
    public int Deletions { get; set; }
}

public enum ChangeType
{
    Added,
    Modified,
    Deleted,
    Renamed,
    Copied
}
