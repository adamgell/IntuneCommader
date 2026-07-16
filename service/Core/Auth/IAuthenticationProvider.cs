using Azure.Core;
using Azure.Identity;
using Intune.Commander.Core.Models;

namespace Intune.Commander.Core.Auth;

public interface IAuthenticationProvider
{
    Task<TokenCredential> GetCredentialAsync(
        TenantProfile profile,
        Func<DeviceCodeInfo, CancellationToken, Task>? deviceCodeCallback = null,
        CancellationToken cancellationToken = default);
}
