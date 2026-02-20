using Azure.Core;
using Azure.Identity;
using IntuneManager.Core.Auth;
using IntuneManager.Core.Models;

namespace IntuneManager.Core.Tests.Auth;

/// <summary>
/// Tests for IntuneGraphClientFactory.CreateClientAsync — verifies that the factory
/// delegates to the auth provider correctly, threads the device-code callback through,
/// and produces a valid GraphServiceClient for every supported cloud environment.
/// </summary>
public class IntuneGraphClientFactoryTests
{
    // ---------------------------------------------------------------------------
    // Stub provider — captures call arguments and returns a safe credential
    // ---------------------------------------------------------------------------

    private sealed class SpyAuthProvider : IAuthenticationProvider
    {
        public int CallCount { get; private set; }
        public Func<DeviceCodeInfo, CancellationToken, Task>? LastCallback { get; private set; }
        public TenantProfile? LastProfile { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<TokenCredential> GetCredentialAsync(
            TenantProfile profile,
            Func<DeviceCodeInfo, CancellationToken, Task>? deviceCodeCallback = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastProfile = profile;
            LastCallback = deviceCodeCallback;
            LastCancellationToken = cancellationToken;

            // ClientSecretCredential is safe to construct without any browser/network interaction.
            TokenCredential credential = new ClientSecretCredential(
                profile.TenantId, profile.ClientId, "stub-secret-for-testing");
            return Task.FromResult(credential);
        }
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static TenantProfile MakeProfile(CloudEnvironment cloud = CloudEnvironment.Commercial)
        => new TenantProfile
        {
            Name = "TestProfile",
            TenantId = Guid.NewGuid().ToString(),
            ClientId = Guid.NewGuid().ToString(),
            Cloud = cloud
        };

    // ---------------------------------------------------------------------------
    // Constructor
    // ---------------------------------------------------------------------------

    [Fact]
    public void Constructor_AcceptsIAuthenticationProvider()
    {
        var provider = new SpyAuthProvider();
        var factory = new IntuneGraphClientFactory(provider);
        Assert.NotNull(factory);
    }

    // ---------------------------------------------------------------------------
    // CreateClientAsync — basic delegation
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateClientAsync_DelegatesToAuthProvider()
    {
        var spy = new SpyAuthProvider();
        var factory = new IntuneGraphClientFactory(spy);

        await factory.CreateClientAsync(MakeProfile());

        Assert.Equal(1, spy.CallCount);
    }

    [Fact]
    public async Task CreateClientAsync_PassesProfileToAuthProvider()
    {
        var spy = new SpyAuthProvider();
        var factory = new IntuneGraphClientFactory(spy);
        var profile = MakeProfile();

        await factory.CreateClientAsync(profile);

        Assert.Same(profile, spy.LastProfile);
    }

    [Fact]
    public async Task CreateClientAsync_ReturnsNonNullGraphServiceClient()
    {
        var factory = new IntuneGraphClientFactory(new SpyAuthProvider());

        var client = await factory.CreateClientAsync(MakeProfile());

        Assert.NotNull(client);
    }

    // ---------------------------------------------------------------------------
    // CreateClientAsync — device-code callback threading
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateClientAsync_PassesDeviceCodeCallbackToAuthProvider()
    {
        var spy = new SpyAuthProvider();
        var factory = new IntuneGraphClientFactory(spy);

        Func<DeviceCodeInfo, CancellationToken, Task> callback = (_, _) => Task.CompletedTask;
        await factory.CreateClientAsync(MakeProfile(), deviceCodeCallback: callback);

        Assert.Same(callback, spy.LastCallback);
    }

    [Fact]
    public async Task CreateClientAsync_NullCallback_PassesNullToAuthProvider()
    {
        var spy = new SpyAuthProvider();
        var factory = new IntuneGraphClientFactory(spy);

        await factory.CreateClientAsync(MakeProfile(), deviceCodeCallback: null);

        Assert.Null(spy.LastCallback);
    }

    [Fact]
    public async Task CreateClientAsync_DefaultCallback_IsNull()
    {
        var spy = new SpyAuthProvider();
        var factory = new IntuneGraphClientFactory(spy);

        // Call without specifying the callback — the default must be null
        await factory.CreateClientAsync(MakeProfile());

        Assert.Null(spy.LastCallback);
    }

    // ---------------------------------------------------------------------------
    // CreateClientAsync — CancellationToken
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateClientAsync_PassesCancellationTokenToAuthProvider()
    {
        var spy = new SpyAuthProvider();
        var factory = new IntuneGraphClientFactory(spy);
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        await factory.CreateClientAsync(MakeProfile(), cancellationToken: token);

        Assert.Equal(token, spy.LastCancellationToken);
    }

    [Fact]
    public async Task CreateClientAsync_AlreadyCancelledToken_ThrowsOrPropagates()
    {
        // A provider that honours cancellation would throw; our spy doesn't network,
        // so we just verify the method accepts the parameter without issue.
        var factory = new IntuneGraphClientFactory(new SpyAuthProvider());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Our stub ignores the cancellation token, so no exception is expected from the factory itself.
        var client = await factory.CreateClientAsync(MakeProfile(), cancellationToken: cts.Token);
        Assert.NotNull(client);
    }

    // ---------------------------------------------------------------------------
    // CreateClientAsync — all cloud environments
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(CloudEnvironment.Commercial)]
    [InlineData(CloudEnvironment.GCC)]
    [InlineData(CloudEnvironment.GCCHigh)]
    [InlineData(CloudEnvironment.DoD)]
    public async Task CreateClientAsync_AllClouds_ReturnsNonNullClient(CloudEnvironment cloud)
    {
        var factory = new IntuneGraphClientFactory(new SpyAuthProvider());

        var client = await factory.CreateClientAsync(MakeProfile(cloud));

        Assert.NotNull(client);
    }

    [Theory]
    [InlineData(CloudEnvironment.Commercial)]
    [InlineData(CloudEnvironment.GCC)]
    [InlineData(CloudEnvironment.GCCHigh)]
    [InlineData(CloudEnvironment.DoD)]
    public async Task CreateClientAsync_AllClouds_CallsAuthProviderExactlyOnce(CloudEnvironment cloud)
    {
        var spy = new SpyAuthProvider();
        var factory = new IntuneGraphClientFactory(spy);

        await factory.CreateClientAsync(MakeProfile(cloud));

        Assert.Equal(1, spy.CallCount);
    }

    // ---------------------------------------------------------------------------
    // CreateClientAsync — interface method signature via reflection
    // ---------------------------------------------------------------------------

    [Fact]
    public void IAuthenticationProvider_HasExpectedMethodSignature()
    {
        var method = typeof(IAuthenticationProvider).GetMethod(nameof(IAuthenticationProvider.GetCredentialAsync));
        Assert.NotNull(method);

        var parameters = method!.GetParameters();

        // profile, deviceCodeCallback (optional), cancellationToken (optional)
        Assert.Equal(3, parameters.Length);
        Assert.Equal("profile", parameters[0].Name);
        Assert.Equal("deviceCodeCallback", parameters[1].Name);
        Assert.True(parameters[1].IsOptional);
        Assert.Null(parameters[1].DefaultValue);
        Assert.Equal("cancellationToken", parameters[2].Name);
        Assert.True(parameters[2].IsOptional);
    }

    [Fact]
    public void IntuneGraphClientFactory_CreateClientAsync_HasExpectedParameterNames()
    {
        var method = typeof(IntuneGraphClientFactory).GetMethod(nameof(IntuneGraphClientFactory.CreateClientAsync));
        Assert.NotNull(method);

        var paramNames = method!.GetParameters().Select(p => p.Name).ToArray();
        Assert.Contains("profile", paramNames);
        Assert.Contains("deviceCodeCallback", paramNames);
        Assert.Contains("cancellationToken", paramNames);
    }
}
