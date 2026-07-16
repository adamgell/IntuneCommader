using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class ConditionalAccessPolicyService : IConditionalAccessPolicyService
{
    private readonly GraphServiceClient _graphClient;

    public ConditionalAccessPolicyService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<ConditionalAccessPolicy>> ListPoliciesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<ConditionalAccessPolicy>();

        var response = await _graphClient.Identity.ConditionalAccess.Policies
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
                response = await _graphClient.Identity.ConditionalAccess.Policies
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

    public async Task<ConditionalAccessPolicy?> GetPolicyAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.Identity.ConditionalAccess.Policies[id]
            .GetAsync(cancellationToken: cancellationToken);
    }
}
