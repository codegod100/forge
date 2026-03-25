using Fido2NetLib;
using Fido2NetLib.Objects;
using Forge.Core.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Forge.Data.Services;

public interface IFido2Service
{
    /// <summary>
    /// Get options to start passkey registration
    /// </summary>
    Task<CredentialCreateOptions> StartRegistrationAsync(string username);
    
    /// <summary>
    /// Complete passkey registration
    /// </summary>
    Task<PasskeyCredential> CompleteRegistrationAsync(string username, AuthenticatorAttestationRawResponse response, string? deviceName = null);
    
    /// <summary>
    /// Get options to start passkey authentication
    /// </summary>
    Task<AssertionOptions> StartAuthenticationAsync();
    
    /// <summary>
    /// Complete passkey authentication
    /// </summary>
    Task<string?> CompleteAuthenticationAsync(AuthenticatorAssertionRawResponse response);
    
    /// <summary>
    /// Get all passkeys for a user
    /// </summary>
    Task<IEnumerable<PasskeyCredential>> GetCredentialsAsync(string username);
    
    /// <summary>
    /// Delete a passkey
    /// </summary>
    Task<bool> DeleteCredentialAsync(Guid credentialId, string username);
}

public class Fido2Service : IFido2Service
{
    private readonly IFido2 _fido2;
    private readonly ForgeDbContext _db;
    
    // Store pending challenges in memory (in production, use distributed cache)
    private static readonly Dictionary<string, CredentialCreateOptions> _pendingRegistrations = new();
    private static readonly Dictionary<string, AssertionOptions> _pendingAssertions = new();

    public Fido2Service(IFido2 fido2, ForgeDbContext db)
    {
        _fido2 = fido2;
        _db = db;
    }

    public async Task<CredentialCreateOptions> StartRegistrationAsync(string username)
    {
        // Get existing credentials for this user
        var existingCredentials = await _db.PasskeyCredentials
            .Where(c => c.Username == username)
            .ToListAsync();
        
        var existingKeys = existingCredentials
            .Select(c => new PublicKeyCredentialDescriptor(c.CredentialId))
            .ToList();
        
        var user = new Fido2User
        {
            Id = System.Text.Encoding.UTF8.GetBytes(username),
            Name = username,
            DisplayName = username
        };
        
        var options = _fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = user,
            ExcludeCredentials = existingKeys,
            AuthenticatorSelection = new AuthenticatorSelection
            {
                AuthenticatorAttachment = AuthenticatorAttachment.Platform,
                ResidentKey = ResidentKeyRequirement.Required,
                UserVerification = UserVerificationRequirement.Required
            },
            AttestationPreference = AttestationConveyancePreference.None
        });
        
        // Store options for verification
        _pendingRegistrations[username] = options;
        
        return options;
    }

    public async Task<PasskeyCredential> CompleteRegistrationAsync(
        string username, 
        AuthenticatorAttestationRawResponse response,
        string? deviceName = null)
    {
        if (!_pendingRegistrations.TryGetValue(username, out var options))
        {
            throw new InvalidOperationException("No pending registration found. Please start registration first.");
        }
        
        var result = await _fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
        {
            AttestationResponse = response,
            OriginalOptions = options,
            IsCredentialIdUniqueToUserCallback = async (args, cancellationToken) =>
            {
                // Verify this credential isn't already registered
                return !await _db.PasskeyCredentials.AnyAsync(c => c.CredentialId.SequenceEqual(args.CredentialId), cancellationToken);
            }
        });
        
        var credential = new PasskeyCredential
        {
            Id = Guid.NewGuid(),
            Username = username,
            CredentialId = result.Id,
            PublicKey = result.PublicKey,
            SignCount = result.SignCount,
            AaGuid = result.AaGuid,
            Name = deviceName,
            CreatedAt = DateTime.UtcNow
        };
        
        _db.PasskeyCredentials.Add(credential);
        await _db.SaveChangesAsync();
        
        // Clean up pending registration
        _pendingRegistrations.Remove(username);
        
        return credential;
    }

    public Task<AssertionOptions> StartAuthenticationAsync()
    {
        // Allow any resident credential (passkey) to be used
        var options = _fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = [],
            UserVerification = UserVerificationRequirement.Required
        });
        
        // Store options for verification
        var key = Base64Url.Encode(options.Challenge);
        _pendingAssertions[key] = options;
        
        return Task.FromResult(options);
    }

    public async Task<string?> CompleteAuthenticationAsync(AuthenticatorAssertionRawResponse response)
    {
        // Find the credential by ID
        var responseId = response.RawId;
        var allCredentials = await _db.PasskeyCredentials.ToListAsync();
        var credential = allCredentials.FirstOrDefault(c => c.CredentialId.AsSpan().SequenceEqual(responseId));
        
        if (credential == null)
        {
            return null;
        }
        
        // Find the pending assertion options
        AssertionOptions? options = null;
        string? optionsKey = null;
        
        foreach (var kvp in _pendingAssertions)
        {
            // Check if this challenge matches
            try
            {
                var clientData = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(
                    System.Text.Encoding.UTF8.GetString(response.Response.ClientDataJson));
                if (clientData.TryGetProperty("challenge", out var challengeProp) &&
                    challengeProp.GetString() == kvp.Key)
                {
                    options = kvp.Value;
                    optionsKey = kvp.Key;
                    break;
                }
            }
            catch
            {
                // Continue searching
            }
        }
        
        if (options == null)
        {
            throw new InvalidOperationException("No pending authentication found.");
        }
        
        // Verify the assertion
        var result = await _fido2.MakeAssertionAsync(new MakeAssertionParams
        {
            AssertionResponse = response,
            OriginalOptions = options,
            StoredPublicKey = credential.PublicKey,
            StoredSignatureCounter = credential.SignCount,
            IsUserHandleOwnerOfCredentialIdCallback = (args, cancellationToken) =>
            {
                // Verify the credential belongs to the user handle
                return Task.FromResult(credential.Username == System.Text.Encoding.UTF8.GetString(args.UserHandle));
            }
        });
        
        // Update sign count and last used
        credential.SignCount = result.SignCount;
        credential.LastUsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        
        // Clean up pending assertion
        if (optionsKey != null)
        {
            _pendingAssertions.Remove(optionsKey);
        }
        
        return credential.Username;
    }

    public async Task<IEnumerable<PasskeyCredential>> GetCredentialsAsync(string username)
    {
        return await _db.PasskeyCredentials
            .Where(c => c.Username == username)
            .OrderByDescending(c => c.LastUsedAt ?? c.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> DeleteCredentialAsync(Guid credentialId, string username)
    {
        var credential = await _db.PasskeyCredentials
            .FirstOrDefaultAsync(c => c.Id == credentialId && c.Username == username);
        
        if (credential == null)
        {
            return false;
        }
        
        _db.PasskeyCredentials.Remove(credential);
        await _db.SaveChangesAsync();
        
        return true;
    }
}

file static class Base64Url
{
    public static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
