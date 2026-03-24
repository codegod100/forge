namespace Forge.Core.Models;

/// <summary>
/// Represents a git repository stored in the forge
/// </summary>
public class Repository
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string Owner { get; set; }
    public string DefaultBranch { get; set; } = "main";
    public bool IsPrivate { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Relative path within the repositories root directory
    /// </summary>
    public required string Path { get; set; }
}
