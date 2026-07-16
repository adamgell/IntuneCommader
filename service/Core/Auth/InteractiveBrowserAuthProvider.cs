using Azure.Core;
using Azure.Identity;
using Intune.Commander.Core.Models;

namespace Intune.Commander.Core.Auth;

public class InteractiveBrowserAuthProvider : IAuthenticationProvider
{
    public Task<TokenCredential> GetCredentialAsync(
        TenantProfile profile,
        Func<DeviceCodeInfo, CancellationToken, Task>? deviceCodeCallback = null,
        CancellationToken cancellationToken = default)
    {
        var (_, authorityHost) = CloudEndpoints.GetEndpoints(profile.Cloud);

        // Allow unencrypted token cache on Linux where secure storage may not be available.
        var tokenCacheOptions = new TokenCachePersistenceOptions
        {
            Name = $"IntuneCommander-{profile.Id}",
            UnsafeAllowUnencryptedStorage = OperatingSystem.IsLinux()
        };

        TokenCredential credential = profile.AuthMethod switch
        {
            AuthMethod.ClientSecret when !string.IsNullOrWhiteSpace(profile.ClientSecret) =>
                new ClientSecretCredential(
                    profile.TenantId,
                    profile.ClientId,
                    profile.ClientSecret,
                    new ClientSecretCredentialOptions { AuthorityHost = authorityHost }),

            AuthMethod.ClientSecret => throw new InvalidOperationException(
                "ClientSecret auth method requires a non-empty ClientSecret value."),

            AuthMethod.DeviceCode =>
                new DeviceCodeCredential(new DeviceCodeCredentialOptions
                {
                    TenantId = profile.TenantId,
                    ClientId = profile.ClientId,
                    AuthorityHost = authorityHost,
                    DeviceCodeCallback = deviceCodeCallback,
                    TokenCachePersistenceOptions = tokenCacheOptions
                }),

            _ => new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
            {
                TenantId = profile.TenantId,
                ClientId = profile.ClientId,
                AuthorityHost = authorityHost,
                // Portless loopback → MSAL picks a fresh random free port each sign-in.
                // A FIXED port (e.g. :45132) collides when a stale OAuth browser tab from
                // a prior/abandoned attempt redirects to that same port the new listener is
                // on, which MSAL rejects as a "state mismatch". A random port makes such
                // stale tabs harmless (they hit a port nothing is listening on). Requires
                // "http://localhost" registered on the app — Entra's loopback exception
                // then accepts any port (RFC 8252).
                RedirectUri = new Uri("http://localhost"),
                TokenCachePersistenceOptions = tokenCacheOptions
            }),
        };

        return Task.FromResult(credential);
    }
}
