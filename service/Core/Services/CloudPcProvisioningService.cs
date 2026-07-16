using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class CloudPcProvisioningService : ICloudPcProvisioningService
{
    private readonly GraphServiceClient _graphClient;

    public CloudPcProvisioningService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<CloudPcProvisioningPolicy>> ListProvisioningPoliciesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<CloudPcProvisioningPolicy>();

        var response = await _graphClient.DeviceManagement.VirtualEndpoint.ProvisioningPolicies
            .GetAsync(cancellationToken: cancellationToken);

        while (response != null)
        {
            if (response.Value != null)
                result.AddRange(response.Value);

            if (!string.IsNullOrEmpty(response.OdataNextLink))
            {
                response = await _graphClient.DeviceManagement.VirtualEndpoint.ProvisioningPolicies
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

    public async Task<CloudPcProvisioningPolicy?> GetProvisioningPolicyAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceManagement.VirtualEndpoint.ProvisioningPolicies[id]
            .GetAsync(cancellationToken: cancellationToken);
    }
}
