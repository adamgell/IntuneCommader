using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class AppProtectionPolicyService : IAppProtectionPolicyService
{
    private readonly GraphServiceClient _graphClient;

    public AppProtectionPolicyService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<ManagedAppPolicy>> ListAppProtectionPoliciesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<ManagedAppPolicy>();

        var response = await _graphClient.DeviceAppManagement.ManagedAppPolicies
            .GetAsync(req =>
            {
                req.QueryParameters.Top = 200;
            }, cancellationToken);

        while (response != null)
        {
            if (response.Value != null)
                result.AddRange(response.Value);

            if (!string.IsNullOrEmpty(response.OdataNextLink))
            {
                response = await _graphClient.DeviceAppManagement.ManagedAppPolicies
                    .WithUrl(response.OdataNextLink)
                    .GetAsync(cancellationToken: cancellationToken);
            }
            else
            {
                break;
            }
        }

        return result;
    }

    public async Task<ManagedAppPolicy?> GetAppProtectionPolicyAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceAppManagement.ManagedAppPolicies[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<ManagedAppPolicy> CreateAppProtectionPolicyAsync(ManagedAppPolicy policy, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.DeviceAppManagement.ManagedAppPolicies
            .PostAsync(policy, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create app protection policy");
    }

    public async Task<ManagedAppPolicy> UpdateAppProtectionPolicyAsync(ManagedAppPolicy policy, CancellationToken cancellationToken = default)
    {
        var id = policy.Id ?? throw new ArgumentException("App protection policy must have an ID for update");

        var result = await _graphClient.DeviceAppManagement.ManagedAppPolicies[id]
            .PatchAsync(policy, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetAppProtectionPolicyAsync(id, cancellationToken), "app protection policy");
    }

    public async Task DeleteAppProtectionPolicyAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceAppManagement.ManagedAppPolicies[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }
}
