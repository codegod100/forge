namespace Forge.Core.Models;

/// <summary>
/// Stores a WebAuthn passkey credential
/// </summary>
public class PasskeyCredential
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// Username this credential belongs to
    /// </summary>
    public required string Username { get; set; }
    
    /// <summary>
    /// Credential ID (base64url encoded)
    /// </summary>
    public required byte[] CredentialId { get; set; }
    
    /// <summary>
    /// Public key (COSE format)
    /// </summary>
    public required byte[] PublicKey { get; set; }
    
    /// <summary>
    /// Sign count for clone detection
    /// </summary>
    public uint SignCount { get; set; }
    
    /// <summary>
    /// AAGUID of the authenticator
    /// </summary>
    public Guid AaGuid { get; set; }
    
    /// <summary>
    /// Friendly name for the passkey (e.g., "iPhone 15 Pro")
    /// </summary>
    public string? Name { get; set; }
    
    /// <summary>
    /// When this credential was registered
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// When this credential was last used
    /// </summary>
    public DateTime? LastUsedAt { get; set; }
}
