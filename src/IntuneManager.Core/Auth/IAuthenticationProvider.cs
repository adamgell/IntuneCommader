using Azure.Core;
using Azure.Identity;
using IntuneManager.Core.Models;

namespace IntuneManager.Core.Auth;

public interface IAuthenticationProvider
{
    Task<TokenCredential> GetCredentialAsync(
        TenantProfile profile,
        Func<DeviceCodeInfo, CancellationToken, Task>? deviceCodeCallback = null,
        CancellationToken cancellationToken = default);
}
