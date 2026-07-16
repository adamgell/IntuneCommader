using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Intune.Commander.Core.Services;

/// <summary>
/// Encrypts/decrypts profile data using ASP.NET DataProtection API.
/// Cross-platform; keys stored alongside the profile data.
/// </summary>
public class ProfileEncryptionService : IProfileEncryptionService
{
    private const string Purpose = "Intune.Commander.Profiles.v1";

    // Legacy purpose string used before the Intune.Commander rename (Phase 1).
    // Kept as a read-only compatibility constant so profiles encrypted under the
    // old name can still be decrypted and migrated during Phase 3 runtime migration.
    private const string LegacyPurpose = "IntuneManager.Profiles.v1";

    private readonly IDataProtector _protector;
    private readonly IDataProtector _legacyProtector;

    public ProfileEncryptionService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
        _legacyProtector = provider.CreateProtector(LegacyPurpose);
    }

    public string Encrypt(string plainText)
    {
        return _protector.Protect(plainText);
    }

    /// <summary>
    /// Decrypts cipherText. Tries the current purpose first; if that fails, falls
    /// back to the legacy purpose string so profiles written before the rename can
    /// still be read and migrated.
    /// </summary>
    public string Decrypt(string cipherText)
    {
        try
        {
            return _protector.Unprotect(cipherText);
        }
        catch (CryptographicException)
        {
            // Fall back to legacy purpose — will throw if both fail
            return _legacyProtector.Unprotect(cipherText);
        }
    }
}
