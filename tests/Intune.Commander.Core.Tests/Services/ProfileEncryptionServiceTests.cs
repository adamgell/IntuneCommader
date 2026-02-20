using Intune.Commander.Core.Models;
using Intune.Commander.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Intune.Commander.Core.Tests.Services;

public class ProfileEncryptionServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _keysDir;
    private readonly IProfileEncryptionService _encryption;

    public ProfileEncryptionServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"intunecommander-enc-test-{Guid.NewGuid()}");
        _keysDir = Path.Combine(_tempDir, "keys");
        Directory.CreateDirectory(_keysDir);

        // Build a real DataProtection provider for integration tests
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName("IntuneCommander-Tests")
            .PersistKeysToFileSystem(new DirectoryInfo(_keysDir));

        var sp = services.BuildServiceProvider();
        _encryption = new ProfileEncryptionService(
            sp.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Encrypt_ThenDecrypt_RoundTrips()
    {
        var original = "{\"profiles\":[],\"activeProfileId\":null}";

        var encrypted = _encryption.Encrypt(original);
        var decrypted = _encryption.Decrypt(encrypted);

        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void Encrypt_ProducesDifferentOutput()
    {
        var plainText = "some sensitive data";

        var encrypted = _encryption.Encrypt(plainText);

        Assert.NotEqual(plainText, encrypted);
    }

    [Fact]
    public void Decrypt_InvalidData_Throws()
    {
        Assert.ThrowsAny<Exception>(() => _encryption.Decrypt("not-valid-encrypted-data"));
    }

    [Fact]
    public void Decrypt_LegacyPurpose_SucceedsViaFallback()
    {
        // Simulate data that was encrypted before the rename using the legacy purpose string.
        // The service must fall back to "IntuneManager.Profiles.v1" and decrypt it successfully.
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName("IntuneCommander-Tests")
            .PersistKeysToFileSystem(new DirectoryInfo(_keysDir));
        using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>();

        // Encrypt directly under the legacy purpose (bypassing ProfileEncryptionService.Encrypt)
        var legacyProtector = provider.CreateProtector("IntuneManager.Profiles.v1");
        var plainText = "{\"profiles\":[],\"activeProfileId\":null}";
        var legacyEncrypted = legacyProtector.Protect(plainText);

        // The service (which normally uses "Intune.Commander.Profiles.v1") must fall back
        // and successfully decrypt the legacy-purpose ciphertext.
        var decrypted = _encryption.Decrypt(legacyEncrypted);

        Assert.Equal(plainText, decrypted);
    }
}

public class EncryptedProfileServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _profilePath;
    private readonly IProfileEncryptionService _encryption;

    public EncryptedProfileServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"intunecommander-encprofile-test-{Guid.NewGuid()}");
        _profilePath = Path.Combine(_tempDir, "profiles.json");
        var keysDir = Path.Combine(_tempDir, "keys");
        Directory.CreateDirectory(keysDir);

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName("IntuneCommander-Tests")
            .PersistKeysToFileSystem(new DirectoryInfo(keysDir));

        var sp = services.BuildServiceProvider();
        _encryption = new ProfileEncryptionService(
            sp.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task SaveAndLoad_WithEncryption_RoundTrips()
    {
        var service = new ProfileService(_profilePath, _encryption);
        service.AddProfile(new TenantProfile
        {
            Name = "Encrypted",
            TenantId = Guid.NewGuid().ToString(),
            ClientId = Guid.NewGuid().ToString(),
            Cloud = CloudEnvironment.GCCHigh
        });
        await service.SaveAsync();

        // Verify file on disk is encrypted (starts with marker, not a JSON brace)
        var raw = await File.ReadAllTextAsync(_profilePath);
        Assert.StartsWith("INTUNEMANAGER_ENC:", raw);

        // Load in a new service instance
        var loaded = new ProfileService(_profilePath, _encryption);
        await loaded.LoadAsync();

        Assert.Single(loaded.Profiles);
        Assert.Equal("Encrypted", loaded.Profiles[0].Name);
        Assert.Equal(CloudEnvironment.GCCHigh, loaded.Profiles[0].Cloud);
    }

    [Fact]
    public async Task Load_PlaintextFile_MigratesOnSave()
    {
        // Write a plaintext file first
        var plainJson = "{\"profiles\":[{\"id\":\"abc\",\"name\":\"Plain\",\"tenantId\":\"" +
            Guid.NewGuid() + "\",\"clientId\":\"" + Guid.NewGuid() +
            "\",\"cloud\":0,\"authMethod\":0}],\"activeProfileId\":\"abc\"}";
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(_profilePath, plainJson);

        // Load with encryption — should parse the plaintext successfully
        var service = new ProfileService(_profilePath, _encryption);
        await service.LoadAsync();

        Assert.Single(service.Profiles);
        Assert.Equal("Plain", service.Profiles[0].Name);

        // Save should encrypt it
        await service.SaveAsync();
        var raw = await File.ReadAllTextAsync(_profilePath);
        Assert.StartsWith("INTUNEMANAGER_ENC:", raw);
    }

    [Fact]
    public async Task Load_CorruptedEncrypted_ReturnsEmptyStore()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(_profilePath, "INTUNEMANAGER_ENC:corrupted-garbage-data");

        var service = new ProfileService(_profilePath, _encryption);
        await service.LoadAsync();

        Assert.Empty(service.Profiles);
    }
}
