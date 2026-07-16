using Azure.Core;
using Azure.Identity;
using CmProjectX.Recording;
using Intune.Commander.Core.Auth;
using Intune.Commander.Core.Models;
using Microsoft.Graph.Beta;
using Xunit;
using Kiota = Microsoft.Kiota.Abstractions.Authentication;

namespace CmProjectX.Api.Tests.Integration.Recording;

// Proves the chain works through the REAL Graph SDK, two ways: a hand-built
// client over the replay handler, and the production seam on
// IntuneGraphClientFactory. A typed Graph request flows SDK request-builder ->
// HttpClient -> ReplayHandler -> cassette, and the recorded JSON deserializes
// back into the SDK's model — all offline. An anonymous auth provider means the
// SDK never tries to acquire a token.
public sealed class GraphReplayTests : IDisposable
{
    private const string Url =
        "https://graph.microsoft.com/beta/deviceManagement/deviceConfigurations";

    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "cmpx-graphreplay-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private CassetteStore StoreWithEmptyCollection()
    {
        var store = new CassetteStore(_dir);
        store.Save(new Cassette(
            new CassetteRequest("GET", Url),
            new CassetteResponse(200, "application/json", """{"value":[]}""")));
        return store;
    }

    [Fact]
    public async Task GraphSdk_OverReplay_DeserializesRecordedCollection()
    {
        using var httpClient = new HttpClient(new ReplayHandler(StoreWithEmptyCollection()));
        var graph = new GraphServiceClient(
            httpClient, new Kiota.AnonymousAuthenticationProvider(), "https://graph.microsoft.com/beta");

        var result = await graph.DeviceManagement.DeviceConfigurations.GetAsync();

        Assert.NotNull(result);
        Assert.NotNull(result!.Value);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task Factory_WithReplayTransport_BuildsReplayingClient()
    {
        // The production seam: IntuneGraphClientFactory.GraphTransport routes the
        // client over the replay handler, so the sidecar's own client-construction
        // path runs offline. The fake credential is never used to fetch a token.
        var factory = new IntuneGraphClientFactory(new FakeAuthProvider())
        {
            GraphTransport = (new ReplayHandler(StoreWithEmptyCollection()),
                              new Kiota.AnonymousAuthenticationProvider()),
        };
        var profile = new TenantProfile
        {
            Name = "test",
            TenantId = "t",
            ClientId = "c",
            Cloud = CloudEnvironment.Commercial,
        };

        var graph = await factory.CreateClientAsync(profile);
        var result = await graph.DeviceManagement.DeviceConfigurations.GetAsync();

        Assert.NotNull(result);
        Assert.Empty(result!.Value!);
    }

    // Builds a token credential without any network call; the token is never
    // exercised because replay uses an anonymous auth provider.
    private sealed class FakeAuthProvider : IAuthenticationProvider
    {
        public Task<TokenCredential> GetCredentialAsync(
            TenantProfile profile,
            Func<DeviceCodeInfo, CancellationToken, Task>? deviceCodeCallback = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TokenCredential>(new FakeCredential());
    }

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken ct) =>
            new("fake-token", DateTimeOffset.MaxValue);

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken ct) =>
            new(new AccessToken("fake-token", DateTimeOffset.MaxValue));
    }
}
