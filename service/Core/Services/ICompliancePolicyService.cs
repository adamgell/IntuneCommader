using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface ICompliancePolicyService
{
    Task<List<DeviceCompliancePolicy>> ListCompliancePoliciesAsync(CancellationToken cancellationToken = default);
    Task<DeviceCompliancePolicy?> GetCompliancePolicyAsync(string id, CancellationToken cancellationToken = default);
    Task<DeviceCompliancePolicy> CreateCompliancePolicyAsync(DeviceCompliancePolicy policy, CancellationToken cancellationToken = default);
    Task<DeviceCompliancePolicy> UpdateCompliancePolicyAsync(DeviceCompliancePolicy policy, CancellationToken cancellationToken = default);
    Task DeleteCompliancePolicyAsync(string id, CancellationToken cancellationToken = default);
    Task<List<DeviceCompliancePolicyAssignment>> GetAssignmentsAsync(string policyId, CancellationToken cancellationToken = default);
    Task AssignPolicyAsync(string policyId, List<DeviceCompliancePolicyAssignment> assignments, CancellationToken cancellationToken = default);
}
