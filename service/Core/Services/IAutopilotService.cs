using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IAutopilotService
{
    Task<List<WindowsAutopilotDeploymentProfile>> ListAutopilotProfilesAsync(CancellationToken cancellationToken = default);
    Task<WindowsAutopilotDeploymentProfile?> GetAutopilotProfileAsync(string id, CancellationToken cancellationToken = default);
    Task<WindowsAutopilotDeploymentProfile> CreateAutopilotProfileAsync(WindowsAutopilotDeploymentProfile profile, CancellationToken cancellationToken = default);
    Task<WindowsAutopilotDeploymentProfile> UpdateAutopilotProfileAsync(WindowsAutopilotDeploymentProfile profile, CancellationToken cancellationToken = default);
    Task DeleteAutopilotProfileAsync(string id, CancellationToken cancellationToken = default);
}