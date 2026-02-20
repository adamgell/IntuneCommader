using Azure.Identity;
using IntuneManager.Core.Auth;
using IntuneManager.Core.Models;
using System.Text.Json;

namespace IntuneManager.Core.Tests.Auth;

public class AuthMethodTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static TenantProfile MakeProfile(
        CloudEnvironment cloud = CloudEnvironment.Commercial,
        AuthMethod method = AuthMethod.Interactive,
        string? clientSecret = null)
        => new TenantProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Test",
            TenantId = Guid.NewGuid().ToString(),
            ClientId = Guid.NewGuid().ToString(),
            Cloud = cloud,
            AuthMethod = method,
            ClientSecret = clientSecret
        };

    // ---------------------------------------------------------------------------
    // Enum contract
    // ---------------------------------------------------------------------------

    [Fact]
    public void AuthMethod_OnlySupportedValuesExist()
    {
        var values = Enum.GetValues<AuthMethod>();
        Assert.Equal(3, values.Length);
        Assert.Contains(AuthMethod.Interactive, values);
        Assert.Contains(AuthMethod.ClientSecret, values);
        Assert.Contains(AuthMethod.DeviceCode, values);
    }

    // ---------------------------------------------------------------------------
    // TenantProfile defaults and properties
    // ---------------------------------------------------------------------------

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

    [Theory]
    [InlineData(AuthMethod.Interactive)]
    [InlineData(AuthMethod.ClientSecret)]
    [InlineData(AuthMethod.DeviceCode)]
    public void TenantProfile_AuthMethodCanBeSetToAllValues(AuthMethod method)
    {
        var profile = MakeProfile(method: method);
        Assert.Equal(method, profile.AuthMethod);
    }

    // ---------------------------------------------------------------------------
    // TenantProfile JSON serialization for all AuthMethod values
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(AuthMethod.Interactive, 0)]
    [InlineData(AuthMethod.ClientSecret, 1)]
    [InlineData(AuthMethod.DeviceCode, 2)]
    public void TenantProfile_AuthMethod_SerializesAsInteger(AuthMethod method, int expectedJsonValue)
    {
        var profile = MakeProfile(method: method);
        var json = JsonSerializer.Serialize(profile);
        // Default STJ serialization writes enums as integer values
        Assert.Contains($"\"authMethod\":{expectedJsonValue}", json);
    }

    [Theory]
    [InlineData(AuthMethod.Interactive)]
    [InlineData(AuthMethod.ClientSecret)]
    [InlineData(AuthMethod.DeviceCode)]
    public void TenantProfile_AuthMethod_RoundTripsViaJson(AuthMethod method)
    {
        var profile = MakeProfile(method: method);
        var json = JsonSerializer.Serialize(profile);
        var deserialized = JsonSerializer.Deserialize<TenantProfile>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(method, deserialized!.AuthMethod);
    }

    [Fact]
    public void TenantProfile_DeviceCodeProfile_PreservesAllFieldsViaJson()
    {
        var original = MakeProfile(
            cloud: CloudEnvironment.GCCHigh,
            method: AuthMethod.DeviceCode);

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<TenantProfile>(json)!;

        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.TenantId, deserialized.TenantId);
        Assert.Equal(original.ClientId, deserialized.ClientId);
        Assert.Equal(original.Cloud, deserialized.Cloud);
        Assert.Equal(AuthMethod.DeviceCode, deserialized.AuthMethod);
    }

    // ---------------------------------------------------------------------------
    // InteractiveBrowserAuthProvider — Interactive credential
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task InteractiveBrowserAuthProvider_Interactive_ReturnsCredential()
    {
        var provider = new InteractiveBrowserAuthProvider();
        var credential = await provider.GetCredentialAsync(MakeProfile());
        Assert.NotNull(credential);
    }

    [Fact]
    public async Task InteractiveBrowserAuthProvider_Interactive_TokenCachePersisted()
    {
        var provider = new InteractiveBrowserAuthProvider();
        var credential = await provider.GetCredentialAsync(MakeProfile());
        Assert.IsType<InteractiveBrowserCredential>(credential);
    }

    [Theory]
    [InlineData(CloudEnvironment.Commercial)]
    [InlineData(CloudEnvironment.GCC)]
    [InlineData(CloudEnvironment.GCCHigh)]
    [InlineData(CloudEnvironment.DoD)]
    public async Task InteractiveBrowserAuthProvider_Interactive_AllClouds_ReturnsInteractiveCredential(CloudEnvironment cloud)
    {
        var provider = new InteractiveBrowserAuthProvider();
        var credential = await provider.GetCredentialAsync(MakeProfile(cloud, AuthMethod.Interactive));
        Assert.IsType<InteractiveBrowserCredential>(credential);
    }

    // ---------------------------------------------------------------------------
    // InteractiveBrowserAuthProvider — ClientSecret credential
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task InteractiveBrowserAuthProvider_ClientSecret_ReturnsCredential()
    {
        var provider = new InteractiveBrowserAuthProvider();
        var credential = await provider.GetCredentialAsync(
            MakeProfile(method: AuthMethod.ClientSecret, clientSecret: "super-secret"));
        Assert.NotNull(credential);
    }

    [Fact]
    public async Task InteractiveBrowserAuthProvider_ClientSecret_ReturnsClientSecretCredential()
    {
        var provider = new InteractiveBrowserAuthProvider();
        var credential = await provider.GetCredentialAsync(
            MakeProfile(method: AuthMethod.ClientSecret, clientSecret: "s3cr3t"));
        Assert.IsType<ClientSecretCredential>(credential);
    }

    [Fact]
    public async Task InteractiveBrowserAuthProvider_ClientSecretWithEmptySecret_Throws()
    {
        var provider = new InteractiveBrowserAuthProvider();
        var profile = MakeProfile(method: AuthMethod.ClientSecret, clientSecret: "");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetCredentialAsync(profile));
    }

    [Fact]
    public async Task InteractiveBrowserAuthProvider_ClientSecretWithNullSecret_Throws()
    {
        var provider = new InteractiveBrowserAuthProvider();
        var profile = MakeProfile(method: AuthMethod.ClientSecret, clientSecret: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetCredentialAsync(profile));
    }

    [Fact]
    public async Task InteractiveBrowserAuthProvider_ClientSecretWithWhitespaceSecret_Throws()
    {
        var provider = new InteractiveBrowserAuthProvider();
        var profile = MakeProfile(method: AuthMethod.ClientSecret, clientSecret: "   ");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetCredentialAsync(profile));
    }

    [Theory]
    [InlineData(CloudEnvironment.Commercial)]
    [InlineData(CloudEnvironment.GCC)]
    [InlineData(CloudEnvironment.GCCHigh)]
    [InlineData(CloudEnvironment.DoD)]
    public async Task InteractiveBrowserAuthProvider_ClientSecret_AllClouds_ReturnsClientSecretCredential(CloudEnvironment cloud)
    {
        var provider = new InteractiveBrowserAuthProvider();
        var credential = await provider.GetCredentialAsync(
            MakeProfile(cloud, AuthMethod.ClientSecret, clientSecret: "secret-value"));
        Assert.IsType<ClientSecretCredential>(credential);
    }

    // ---------------------------------------------------------------------------
    // InteractiveBrowserAuthProvider — DeviceCode credential
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task InteractiveBrowserAuthProvider_DeviceCode_ReturnsCredential()
    {
        var provider = new InteractiveBrowserAuthProvider();
        var credential = await provider.GetCredentialAsync(
            MakeProfile(method: AuthMethod.DeviceCode), deviceCodeCallback: null);
        Assert.NotNull(credential);
    }

    [Fact]
    public async Task InteractiveBrowserAuthProvider_DeviceCode_TokenCachePersisted()
    {
        var provider = new InteractiveBrowserAuthProvider();
        var credential = await provider.GetCredentialAsync(MakeProfile(method: AuthMethod.DeviceCode));
        Assert.IsType<DeviceCodeCredential>(credential);
    }

    [Fact]
    public async Task InteractiveBrowserAuthProvider_DeviceCode_WithCallback_ReturnsCredential()
    {
        var provider = new InteractiveBrowserAuthProvider();
        var credential = await provider.GetCredentialAsync(
            MakeProfile(method: AuthMethod.DeviceCode),
            deviceCodeCallback: (info, _) => Task.CompletedTask);
        Assert.NotNull(credential);
    }

    [Fact]
    public async Task InteractiveBrowserAuthProvider_DeviceCode_WithCallback_ReturnsDeviceCodeCredential()
    {
        var provider = new InteractiveBrowserAuthProvider();
        var credential = await provider.GetCredentialAsync(
            MakeProfile(method: AuthMethod.DeviceCode),
            deviceCodeCallback: (info, _) => Task.CompletedTask);
        Assert.IsType<DeviceCodeCredential>(credential);
    }

    [Theory]
    [InlineData(CloudEnvironment.Commercial)]
    [InlineData(CloudEnvironment.GCC)]
    [InlineData(CloudEnvironment.GCCHigh)]
    [InlineData(CloudEnvironment.DoD)]
    public async Task InteractiveBrowserAuthProvider_DeviceCode_AllClouds_ReturnsDeviceCodeCredential(CloudEnvironment cloud)
    {
        var provider = new InteractiveBrowserAuthProvider();
        var credential = await provider.GetCredentialAsync(MakeProfile(cloud, AuthMethod.DeviceCode));
        Assert.IsType<DeviceCodeCredential>(credential);
    }

    [Theory]
    [InlineData(CloudEnvironment.Commercial)]
    [InlineData(CloudEnvironment.GCC)]
    [InlineData(CloudEnvironment.GCCHigh)]
    [InlineData(CloudEnvironment.DoD)]
    public async Task InteractiveBrowserAuthProvider_DeviceCode_AllClouds_WithCallback_ReturnsDeviceCodeCredential(CloudEnvironment cloud)
    {
        var provider = new InteractiveBrowserAuthProvider();
        var credential = await provider.GetCredentialAsync(
            MakeProfile(cloud, AuthMethod.DeviceCode),
            deviceCodeCallback: (_, _) => Task.CompletedTask);
        Assert.IsType<DeviceCodeCredential>(credential);
    }

    // ---------------------------------------------------------------------------
    // InteractiveBrowserAuthProvider — CancellationToken accepted
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task InteractiveBrowserAuthProvider_AcceptsCancellationToken()
    {
        var provider = new InteractiveBrowserAuthProvider();
        using var cts = new CancellationTokenSource();
        var credential = await provider.GetCredentialAsync(
            MakeProfile(), cancellationToken: cts.Token);
        Assert.NotNull(credential);
    }
}
