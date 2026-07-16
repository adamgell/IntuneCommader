namespace Intune.Commander.Core.Services;

/// <summary>
/// Encrypts and decrypts profile store data at rest.
/// </summary>
public interface IProfileEncryptionService
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}
