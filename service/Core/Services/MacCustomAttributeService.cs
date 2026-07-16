using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class MacCustomAttributeService : IMacCustomAttributeService
{
    private readonly GraphServiceClient _graphClient;

    public MacCustomAttributeService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<DeviceCustomAttributeShellScript>> ListMacCustomAttributesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<DeviceCustomAttributeShellScript>();

        var response = await _graphClient.DeviceManagement.DeviceCustomAttributeShellScripts
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
                response = await _graphClient.DeviceManagement.DeviceCustomAttributeShellScripts
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

    public async Task<DeviceCustomAttributeShellScript?> GetMacCustomAttributeAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceManagement.DeviceCustomAttributeShellScripts[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<DeviceCustomAttributeShellScript> CreateMacCustomAttributeAsync(DeviceCustomAttributeShellScript script, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.DeviceManagement.DeviceCustomAttributeShellScripts
            .PostAsync(script, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create mac custom attribute script");
    }

    public async Task<DeviceCustomAttributeShellScript> UpdateMacCustomAttributeAsync(DeviceCustomAttributeShellScript script, CancellationToken cancellationToken = default)
    {
        var id = script.Id ?? throw new ArgumentException("Mac custom attribute script must have an ID for update");

        var result = await _graphClient.DeviceManagement.DeviceCustomAttributeShellScripts[id]
            .PatchAsync(script, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetMacCustomAttributeAsync(id, cancellationToken), "mac custom attribute script");
    }

    public async Task DeleteMacCustomAttributeAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.DeviceCustomAttributeShellScripts[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }
}
