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
                TokenCachePersistenceOptions = tokenCacheOptions
            }),
        };

        return Task.FromResult(credential);
    }
}
