using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IAuthenticationStrengthService
{
    Task<List<AuthenticationStrengthPolicy>> ListAuthenticationStrengthPoliciesAsync(CancellationToken cancellationToken = default);
    Task<AuthenticationStrengthPolicy?> GetAuthenticationStrengthPolicyAsync(string id, CancellationToken cancellationToken = default);
    Task<AuthenticationStrengthPolicy> CreateAuthenticationStrengthPolicyAsync(AuthenticationStrengthPolicy policy, CancellationToken cancellationToken = default);
    Task<AuthenticationStrengthPolicy> UpdateAuthenticationStrengthPolicyAsync(AuthenticationStrengthPolicy policy, CancellationToken cancellationToken = default);
    Task DeleteAuthenticationStrengthPolicyAsync(string id, CancellationToken cancellationToken = default);
}
