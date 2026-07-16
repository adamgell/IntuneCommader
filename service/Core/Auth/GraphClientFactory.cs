using Azure.Core;
using Azure.Identity;
using Intune.Commander.Core.Models;
using Microsoft.Graph.Beta;
using KiotaAuth = Microsoft.Kiota.Abstractions.Authentication;

namespace Intune.Commander.Core.Auth;

public class IntuneGraphClientFactory
{
    private readonly IAuthenticationProvider _authProvider;

    public IntuneGraphClientFactory(IAuthenticationProvider authProvider)
    {
        _authProvider = authProvider;
    }

    // RT2 recorded-Graph seam (docs/REGRESSION-TESTING.md). When set, the
    // GraphServiceClient is built over this terminal handler with the given auth
    // provider (record/replay for tests) instead of the live, credential-bound
    // transport — an AnonymousAuthenticationProvider for replay means the SDK
    // never acquires a token. Null in production: the client is built exactly as
    // before. The credential is still resolved (offline) and returned, so callers
    // that use it independently (e.g. permission checks) keep working.
    public (HttpMessageHandler Handler, KiotaAuth.IAuthenticationProvider Auth)? GraphTransport { get; set; }

    public async Task<GraphServiceClient> CreateClientAsync(
        TenantProfile profile,
        Func<DeviceCodeInfo, CancellationToken, Task>? deviceCodeCallback = null,
        CancellationToken cancellationToken = default)
    {
        var (client, _, _) = await CreateClientWithCredentialAsync(profile, deviceCodeCallback, cancellationToken);
        return client;
    }

    /// <summary>
    /// Creates a <see cref="GraphServiceClient"/> and returns it together with the
    /// underlying <see cref="TokenCredential"/> and scopes, so callers can use the
    /// credential independently (e.g. to acquire tokens for permission checking).
    /// </summary>
    public async Task<(GraphServiceClient Client, TokenCredential Credential, string[] Scopes)> CreateClientWithCredentialAsync(
        TenantProfile profile,
        Func<DeviceCodeInfo, CancellationToken, Task>? deviceCodeCallback = null,
        CancellationToken cancellationToken = default)
    {
        var credential = await _authProvider.GetCredentialAsync(profile, deviceCodeCallback, cancellationToken);
        var (graphBaseUrl, _) = CloudEndpoints.GetEndpoints(profile.Cloud);
        var scopes = ResolveScopes(profile);

        if (GraphTransport is { } transport)
        {
            var httpClient = new HttpClient(transport.Handler);
            return (new GraphServiceClient(httpClient, transport.Auth, graphBaseUrl), credential, scopes);
        }

        return (new GraphServiceClient(credential, scopes, graphBaseUrl), credential, scopes);
    }

    /// <summary>
    /// Graph scopes for a profile — the resource-default <c>.default</c> for EVERY
    /// auth method (app-only and delegated alike).
    ///
    /// Do NOT switch delegated flows to an explicit scope list: the Microsoft.Graph
    /// SDK requests <c>{root}/.default</c> per call regardless of the scopes passed to
    /// the GraphServiceClient ctor. If sign-in cached a different set, every Graph call
    /// is a cache miss and an InteractiveBrowserCredential / DeviceCodeCredential
    /// re-prompts on EACH call (a browser tab per request). Keeping the sign-in cache
    /// and per-call scopes both <c>.default</c> means the token acquired at sign-in
    /// serves every Graph call silently. Delegated permissions still apply — they are
    /// configured + consented on the (public-client) app registration, and
    /// <c>.default</c> grants exactly those.
    /// </summary>
    public static string[] ResolveScopes(TenantProfile profile) =>
        CloudEndpoints.GetScopes(profile.Cloud);
}
