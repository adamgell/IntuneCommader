using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class DeviceManagementScriptService : IDeviceManagementScriptService
{
    private readonly GraphServiceClient _graphClient;

    public DeviceManagementScriptService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<DeviceManagementScript>> ListDeviceManagementScriptsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<DeviceManagementScript>();

        var response = await _graphClient.DeviceManagement.DeviceManagementScripts
            .GetAsync(req =>
            {
                req.QueryParameters.Top = 999;
            }, cancellationToken);

        while (response != null)
        {
            if (response.Value != null)
                result.AddRange(response.Value);

            if (!string.IsNullOrEmpty(response.OdataNextLink))
            {
                response = await _graphClient.DeviceManagement.DeviceManagementScripts
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

    public async Task<DeviceManagementScript?> GetDeviceManagementScriptAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceManagement.DeviceManagementScripts[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<DeviceManagementScript> CreateDeviceManagementScriptAsync(DeviceManagementScript script, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.DeviceManagement.DeviceManagementScripts
            .PostAsync(script, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create device management script");
    }

    public async Task<DeviceManagementScript> UpdateDeviceManagementScriptAsync(DeviceManagementScript script, CancellationToken cancellationToken = default)
    {
        var id = script.Id ?? throw new ArgumentException("Device management script must have an ID for update");

        var result = await _graphClient.DeviceManagement.DeviceManagementScripts[id]
            .PatchAsync(script, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetDeviceManagementScriptAsync(id, cancellationToken), "device management script");
    }

    public async Task DeleteDeviceManagementScriptAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.DeviceManagementScripts[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }

    public async Task<List<DeviceManagementScriptAssignment>> GetAssignmentsAsync(string scriptId, CancellationToken cancellationToken = default)
    {
        var response = await _graphClient.DeviceManagement.DeviceManagementScripts[scriptId]
            .Assignments.GetAsync(cancellationToken: cancellationToken);

        return response?.Value ?? [];
    }

    public async Task AssignScriptAsync(string scriptId, List<DeviceManagementScriptAssignment> assignments, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.DeviceManagementScripts[scriptId]
            .Assign.PostAsync(
                new Microsoft.Graph.Beta.DeviceManagement.DeviceManagementScripts.Item.Assign.AssignPostRequestBody
                {
                    DeviceManagementScriptAssignments = assignments
                },
                cancellationToken: cancellationToken);
    }
}
