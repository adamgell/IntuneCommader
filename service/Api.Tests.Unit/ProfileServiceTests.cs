using Intune.Commander.Core.Services;
using Xunit;

namespace CmProjectX.Tests.Unit;

public sealed class ProfileServiceTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "cmpx-profiles-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task SaveAfterDecryptFailure_DoesNotOverwriteProfilesFile()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "profiles.json");
        const string encryptedBody = "INTUNEMANAGER_ENC:unreadable";
        await File.WriteAllTextAsync(path, encryptedBody);

        var profiles = new ProfileService(path, new FailingEncryptionService());
        await profiles.LoadAsync();
        profiles.AddProfile(new()
        {
            Name = "Replacement",
            TenantId = "tenant",
            ClientId = "client",
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => profiles.SaveAsync());
        Assert.Equal(encryptedBody, await File.ReadAllTextAsync(path));
    }

    private sealed class FailingEncryptionService : IProfileEncryptionService
    {
        public string Encrypt(string plainText) => plainText;
        public string Decrypt(string cipherText) => throw new InvalidOperationException("bad key");
    }
}
