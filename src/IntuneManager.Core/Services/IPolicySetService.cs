using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Services;

public interface IPolicySetService
{
    Task<List<PolicySet>> ListPolicySetsAsync(CancellationToken cancellationToken = default);
    Task<PolicySet?> GetPolicySetAsync(string id, CancellationToken cancellationToken = default);
}
