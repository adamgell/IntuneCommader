using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IComplianceScriptService
{
    Task<List<DeviceComplianceScript>> ListComplianceScriptsAsync(CancellationToken cancellationToken = default);
    Task<DeviceComplianceScript?> GetComplianceScriptAsync(string id, CancellationToken cancellationToken = default);
    Task<DeviceComplianceScript> CreateComplianceScriptAsync(DeviceComplianceScript script, CancellationToken cancellationToken = default);
    Task<DeviceComplianceScript> UpdateComplianceScriptAsync(DeviceComplianceScript script, CancellationToken cancellationToken = default);
    Task DeleteComplianceScriptAsync(string id, CancellationToken cancellationToken = default);
}
