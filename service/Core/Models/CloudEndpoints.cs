using Azure.Identity;

namespace Intune.Commander.Core.Models;

public static class CloudEndpoints
{
    public static (string GraphBaseUrl, Uri AuthorityHost) GetEndpoints(CloudEnvironment cloud)
    {
        return cloud switch
        {
            CloudEnvironment.Commercial => ("https://graph.microsoft.com/beta", AzureAuthorityHosts.AzurePublicCloud),
            CloudEnvironment.GCC => ("https://graph.microsoft.com/beta", AzureAuthorityHosts.AzurePublicCloud),
            CloudEnvironment.GCCHigh => ("https://graph.microsoft.us/beta", AzureAuthorityHosts.AzureGovernment),
            CloudEnvironment.DoD => ("https://dod-graph.microsoft.us/beta", AzureAuthorityHosts.AzureGovernment),
            _ => throw new ArgumentOutOfRangeException(nameof(cloud), cloud, "Unsupported cloud environment")
        };
    }

    // The Graph resource root for the cloud (no API version), e.g.
    // "https://graph.microsoft.com". Scopes are built off this, not the versioned URL.
    public static string GetGraphRootUrl(CloudEnvironment cloud)
    {
        var (graphBaseUrl, _) = GetEndpoints(cloud);
        return graphBaseUrl[..graphBaseUrl.IndexOf("/beta", StringComparison.Ordinal)];
    }

    // App-only (client-credentials) scope: every application permission already
    // consented on the registration. Delegated scopes are resolved elsewhere
    // (IntuneGraphClientFactory.ResolveScopes) since they mirror the required
    // permission set.
    public static string[] GetScopes(CloudEnvironment cloud) => [$"{GetGraphRootUrl(cloud)}/.default"];
}
