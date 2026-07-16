using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class DeviceShellScriptService : IDeviceShellScriptService
{
    private readonly GraphServiceClient _graphClient;

    public DeviceShellScriptService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<DeviceShellScript>> ListDeviceShellScriptsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<DeviceShellScript>();

        var response = await _graphClient.DeviceManagement.DeviceShellScripts
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
                response = await _graphClient.DeviceManagement.DeviceShellScripts
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

    public async Task<DeviceShellScript?> GetDeviceShellScriptAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceManagement.DeviceShellScripts[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<DeviceShellScript> CreateDeviceShellScriptAsync(DeviceShellScript script, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.DeviceManagement.DeviceShellScripts
            .PostAsync(script, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create device shell script");
    }

    public async Task<DeviceShellScript> UpdateDeviceShellScriptAsync(DeviceShellScript script, CancellationToken cancellationToken = default)
    {
        var id = script.Id ?? throw new ArgumentException("Device shell script must have an ID for update");

        var result = await _graphClient.DeviceManagement.DeviceShellScripts[id]
            .PatchAsync(script, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetDeviceShellScriptAsync(id, cancellationToken), "device shell script");
    }

    public async Task DeleteDeviceShellScriptAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.DeviceShellScripts[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }

    public async Task<List<DeviceManagementScriptAssignment>> GetAssignmentsAsync(string scriptId, CancellationToken cancellationToken = default)
    {
        var response = await _graphClient.DeviceManagement.DeviceShellScripts[scriptId]
            .Assignments.GetAsync(cancellationToken: cancellationToken);

        return response?.Value ?? [];
    }

    public async Task AssignScriptAsync(string scriptId, List<DeviceManagementScriptAssignment> assignments, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.DeviceShellScripts[scriptId]
            .Assign.PostAsync(
                new Microsoft.Graph.Beta.DeviceManagement.DeviceShellScripts.Item.Assign.AssignPostRequestBody
                {
                    DeviceManagementScriptAssignments = assignments
                },
                cancellationToken: cancellationToken);
    }
}
