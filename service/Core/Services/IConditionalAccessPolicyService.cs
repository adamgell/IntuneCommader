using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IConditionalAccessPolicyService
{
    Task<List<ConditionalAccessPolicy>> ListPoliciesAsync(CancellationToken cancellationToken = default);
    Task<ConditionalAccessPolicy?> GetPolicyAsync(string id, CancellationToken cancellationToken = default);
}
