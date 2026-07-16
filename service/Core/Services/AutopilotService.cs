using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class AutopilotService : IAutopilotService
{
    private readonly GraphServiceClient _graphClient;

    public AutopilotService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<WindowsAutopilotDeploymentProfile>> ListAutopilotProfilesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<WindowsAutopilotDeploymentProfile>();

        var response = await _graphClient.DeviceManagement.WindowsAutopilotDeploymentProfiles
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
                response = await _graphClient.DeviceManagement.WindowsAutopilotDeploymentProfiles
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

    public async Task<WindowsAutopilotDeploymentProfile?> GetAutopilotProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceManagement.WindowsAutopilotDeploymentProfiles[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<WindowsAutopilotDeploymentProfile> CreateAutopilotProfileAsync(WindowsAutopilotDeploymentProfile profile, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.DeviceManagement.WindowsAutopilotDeploymentProfiles
            .PostAsync(profile, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create Autopilot profile");
    }

    public async Task<WindowsAutopilotDeploymentProfile> UpdateAutopilotProfileAsync(WindowsAutopilotDeploymentProfile profile, CancellationToken cancellationToken = default)
    {
        var id = profile.Id ?? throw new ArgumentException("Autopilot profile must have an ID for update");

        var result = await _graphClient.DeviceManagement.WindowsAutopilotDeploymentProfiles[id]
            .PatchAsync(profile, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetAutopilotProfileAsync(id, cancellationToken), "Autopilot profile");
    }

    public async Task DeleteAutopilotProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.WindowsAutopilotDeploymentProfiles[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }
}