using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace IntuneManager.Core.Services;

public class DeviceHealthScriptService : IDeviceHealthScriptService
{
    private readonly GraphServiceClient _graphClient;

    public DeviceHealthScriptService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<DeviceHealthScript>> ListDeviceHealthScriptsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<DeviceHealthScript>();

        var response = await _graphClient.DeviceManagement.DeviceHealthScripts
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
                response = await _graphClient.DeviceManagement.DeviceHealthScripts
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

    public async Task<DeviceHealthScript?> GetDeviceHealthScriptAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceManagement.DeviceHealthScripts[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<DeviceHealthScript> CreateDeviceHealthScriptAsync(DeviceHealthScript script, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.DeviceManagement.DeviceHealthScripts
            .PostAsync(script, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create device health script");
    }

    public async Task<DeviceHealthScript> UpdateDeviceHealthScriptAsync(DeviceHealthScript script, CancellationToken cancellationToken = default)
    {
        var id = script.Id ?? throw new ArgumentException("Device health script must have an ID for update");

        var result = await _graphClient.DeviceManagement.DeviceHealthScripts[id]
            .PatchAsync(script, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetDeviceHealthScriptAsync(id, cancellationToken), "device health script");
    }

    public async Task DeleteDeviceHealthScriptAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.DeviceHealthScripts[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }
}