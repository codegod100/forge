namespace Forge.Core.Models;

/// <summary>
/// Represents a file or directory in a repository tree
/// </summary>
public class TreeNode
{
    public required string Name { get; set; }
    public required string Path { get; set; }
    public required TreeEntryType Type { get; set; }
    public string? Sha { get; set; }
    public long? Size { get; set; }
    
    /// <summary>
    /// For files, the content (if loaded)
    /// </summary>
    public string? Content { get; set; }
    
    /// <summary>
    /// For directories, child entries (if loaded)
    /// </summary>
    public List<TreeNode>? Children { get; set; }
}

public enum TreeEntryType
{
    File,
    Directory,
    Symlink,
    Submodule
}
