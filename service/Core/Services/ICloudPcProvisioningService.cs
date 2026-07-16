using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface ICloudPcProvisioningService
{
    Task<List<CloudPcProvisioningPolicy>> ListProvisioningPoliciesAsync(CancellationToken cancellationToken = default);
    Task<CloudPcProvisioningPolicy?> GetProvisioningPolicyAsync(string id, CancellationToken cancellationToken = default);
}
