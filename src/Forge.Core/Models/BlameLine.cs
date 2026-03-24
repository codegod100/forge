namespace Forge.Core.Models;

/// <summary>
/// Represents blame information for a single line in a file
/// </summary>
public class BlameLine
{
    /// <summary>
    /// The line number (1-indexed)
    /// </summary>
    public required int LineNumber { get; set; }
    
    /// <summary>
    /// The content of the line
    /// </summary>
    public required string Content { get; set; }
    
    /// <summary>
    /// The commit SHA that last modified this line
    /// </summary>
    public required string CommitSha { get; set; }
    
    /// <summary>
    /// Short SHA (first 7 characters)
    /// </summary>
    public string ShortSha => CommitSha.Length > 7 ? CommitSha[..7] : CommitSha;
    
    /// <summary>
    /// Author of the commit
    /// </summary>
    public required string Author { get; set; }
    
    /// <summary>
    /// Date of the commit
    /// </summary>
    public DateTime Date { get; set; }
    
    /// <summary>
    /// Commit message (first line)
    /// </summary>
    public string? Message { get; set; }
}
