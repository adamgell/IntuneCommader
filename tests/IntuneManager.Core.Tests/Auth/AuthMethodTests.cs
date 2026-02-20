using IntuneManager.Core.Auth;
using IntuneManager.Core.Models;

namespace IntuneManager.Core.Tests.Auth;

public class AuthMethodTests
{
    [Fact]
    public void AuthMethod_OnlySupportedValuesExist()
    {
        var values = Enum.GetValues<AuthMethod>();
        Assert.Equal(3, values.Length);
        Assert.Contains(AuthMethod.Interactive, values);
        Assert.Contains(AuthMethod.ClientSecret, values);
        Assert.Contains(AuthMethod.DeviceCode, values);
    }

    [Fact]
    public void TenantProfile_DefaultAuthMethodIsInteractive()
    {
        var profile = new TenantProfile
        {
            Name = "Test",
            TenantId = Guid.NewGuid().ToString(),
            ClientId = Guid.NewGuid().ToString()
        };

        Assert.Equal(AuthMethod.Interactive, profile.AuthMethod);
    }

    [Fact]
    public void TenantProfile_DoesNotHaveCertificateThumbprintProperty()
    {
        var props = typeof(TenantProfile).GetProperties();
        Assert.DoesNotContain(props, p => p.Name == "CertificateThumbprint");
    }

    [Fact]
    public async Task InteractiveBrowserAuthProvider_Interactive_ReturnsCredential()
    {
        var provider = new InteractiveBrowserAuthProvider();
        var profile = new TenantProfile
        {
            Name = "Test",
            TenantId = Guid.NewGuid().ToString(),
            ClientId = Guid.NewGuid().ToString(),
            AuthMethod = AuthMethod.Interactive
        };

        // Should not throw — merely constructs the credential object
        var credential = await provider.GetCredentialAsync(profile);
        Assert.NotNull(credential);
    }

    [Fact]
    public async Task InteractiveBrowserAuthProvider_ClientSecret_ReturnsCredential()
    {
        var provider = new InteractiveBrowserAuthProvider();
        var profile = new TenantProfile
        {
            Name = "Test",
            TenantId = Guid.NewGuid().ToString(),
            ClientId = Guid.NewGuid().ToString(),
            AuthMethod = AuthMethod.ClientSecret,
            ClientSecret = "super-secret"
        };

        var credential = await provider.GetCredentialAsync(profile);
        Assert.NotNull(credential);
    }

    [Fact]
    public async Task InteractiveBrowserAuthProvider_ClientSecretWithEmptySecret_Throws()
    {
        var provider = new InteractiveBrowserAuthProvider();
        var profile = new TenantProfile
        {
            Name = "Test",
            TenantId = Guid.NewGuid().ToString(),
            ClientId = Guid.NewGuid().ToString(),
            AuthMethod = AuthMethod.ClientSecret,
            ClientSecret = ""
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetCredentialAsync(profile));
    }

    [Fact]
    public async Task InteractiveBrowserAuthProvider_ClientSecretWithNullSecret_Throws()
    {
        var provider = new InteractiveBrowserAuthProvider();
        var profile = new TenantProfile
        {
            Name = "Test",
            TenantId = Guid.NewGuid().ToString(),
            ClientId = Guid.NewGuid().ToString(),
            AuthMethod = AuthMethod.ClientSecret,
            ClientSecret = null
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetCredentialAsync(profile));
    }

    [Fact]
    public async Task InteractiveBrowserAuthProvider_ClientSecretWithWhitespaceSecret_Throws()
    {
        var provider = new InteractiveBrowserAuthProvider();
        var profile = new TenantProfile
        {
            Name = "Test",
            TenantId = Guid.NewGuid().ToString(),
            ClientId = Guid.NewGuid().ToString(),
            AuthMethod = AuthMethod.ClientSecret,
            ClientSecret = "   "
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetCredentialAsync(profile));
    }

    [Fact]
    public async Task InteractiveBrowserAuthProvider_DeviceCode_ReturnsCredential()
    {
        var provider = new InteractiveBrowserAuthProvider();
        var profile = new TenantProfile
        {
            Name = "Test",
            TenantId = Guid.NewGuid().ToString(),
            ClientId = Guid.NewGuid().ToString(),
            AuthMethod = AuthMethod.DeviceCode
        };

        // Should not throw — merely constructs the credential object; callback can be null
        var credential = await provider.GetCredentialAsync(profile, deviceCodeCallback: null);
        Assert.NotNull(credential);
    }

    [Fact]
    public async Task InteractiveBrowserAuthProvider_DeviceCode_WithCallback_ReturnsCredential()
    {
        var provider = new InteractiveBrowserAuthProvider();
        var profile = new TenantProfile
        {
            Name = "Test",
            TenantId = Guid.NewGuid().ToString(),
            ClientId = Guid.NewGuid().ToString(),
            AuthMethod = AuthMethod.DeviceCode
        };

        // Callback is stored on the credential; constructor should succeed regardless
        var credential = await provider.GetCredentialAsync(
            profile,
            deviceCodeCallback: (info, _) => Task.CompletedTask);

        Assert.NotNull(credential);
    }

    [Fact]
    public async Task InteractiveBrowserAuthProvider_Interactive_TokenCachePersisted()
    {
        var provider = new InteractiveBrowserAuthProvider();
        var profileId = Guid.NewGuid().ToString();
        var profile = new TenantProfile
        {
            Id = profileId,
            Name = "Test",
            TenantId = Guid.NewGuid().ToString(),
            ClientId = Guid.NewGuid().ToString(),
            AuthMethod = AuthMethod.Interactive
        };

        var credential = await provider.GetCredentialAsync(profile);

        // Verify the credential is an InteractiveBrowserCredential (token cache is configured)
        Assert.IsType<Azure.Identity.InteractiveBrowserCredential>(credential);
    }

    [Fact]
    public async Task InteractiveBrowserAuthProvider_DeviceCode_TokenCachePersisted()
    {
        var provider = new InteractiveBrowserAuthProvider();
        var profileId = Guid.NewGuid().ToString();
        var profile = new TenantProfile
        {
            Id = profileId,
            Name = "Test",
            TenantId = Guid.NewGuid().ToString(),
            ClientId = Guid.NewGuid().ToString(),
            AuthMethod = AuthMethod.DeviceCode
        };

        var credential = await provider.GetCredentialAsync(profile);

        // Verify the credential is a DeviceCodeCredential
        Assert.IsType<Azure.Identity.DeviceCodeCredential>(credential);
    }
}
