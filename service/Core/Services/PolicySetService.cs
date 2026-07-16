using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class PolicySetService : IPolicySetService
{
    private readonly GraphServiceClient _graphClient;

    public PolicySetService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<PolicySet>> ListPolicySetsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<PolicySet>();

        var response = await _graphClient.DeviceAppManagement.PolicySets
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
                response = await _graphClient.DeviceAppManagement.PolicySets
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

    public async Task<PolicySet?> GetPolicySetAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceAppManagement.PolicySets[id]
            .GetAsync(cancellationToken: cancellationToken);
    }
}
