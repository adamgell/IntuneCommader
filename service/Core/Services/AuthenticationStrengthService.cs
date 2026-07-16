using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class AuthenticationStrengthService : IAuthenticationStrengthService
{
    private readonly GraphServiceClient _graphClient;

    public AuthenticationStrengthService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<AuthenticationStrengthPolicy>> ListAuthenticationStrengthPoliciesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<AuthenticationStrengthPolicy>();

        var response = await _graphClient.Identity.ConditionalAccess.AuthenticationStrength.Policies
            .GetAsync(cancellationToken: cancellationToken);

        while (response != null)
        {
            if (response.Value != null)
                result.AddRange(response.Value);

            if (!string.IsNullOrEmpty(response.OdataNextLink))
            {
                response = await _graphClient.Identity.ConditionalAccess.AuthenticationStrength.Policies
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

    public async Task<AuthenticationStrengthPolicy?> GetAuthenticationStrengthPolicyAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.Identity.ConditionalAccess.AuthenticationStrength.Policies[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<AuthenticationStrengthPolicy> CreateAuthenticationStrengthPolicyAsync(AuthenticationStrengthPolicy policy, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.Identity.ConditionalAccess.AuthenticationStrength.Policies
            .PostAsync(policy, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create authentication strength policy");
    }

    public async Task<AuthenticationStrengthPolicy> UpdateAuthenticationStrengthPolicyAsync(AuthenticationStrengthPolicy policy, CancellationToken cancellationToken = default)
    {
        var id = policy.Id ?? throw new ArgumentException("Authentication strength policy must have an ID for update");

        var result = await _graphClient.Identity.ConditionalAccess.AuthenticationStrength.Policies[id]
            .PatchAsync(policy, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetAuthenticationStrengthPolicyAsync(id, cancellationToken), "authentication strength policy");
    }

    public async Task DeleteAuthenticationStrengthPolicyAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.Identity.ConditionalAccess.AuthenticationStrength.Policies[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }
}
