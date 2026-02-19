using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace IntuneManager.Core.Services;

public class ConfigurationProfileService : IConfigurationProfileService
{
    private readonly GraphServiceClient _graphClient;

    public ConfigurationProfileService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<DeviceConfiguration>> ListDeviceConfigurationsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<DeviceConfiguration>();

        var response = await _graphClient.DeviceManagement.DeviceConfigurations
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
                response = await _graphClient.DeviceManagement.DeviceConfigurations
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

    public async Task<DeviceConfiguration?> GetDeviceConfigurationAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceManagement.DeviceConfigurations[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<DeviceConfiguration> CreateDeviceConfigurationAsync(DeviceConfiguration config, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.DeviceManagement.DeviceConfigurations
            .PostAsync(config, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create device configuration");
    }

    public async Task<DeviceConfiguration> UpdateDeviceConfigurationAsync(DeviceConfiguration config, CancellationToken cancellationToken = default)
    {
        var id = config.Id ?? throw new ArgumentException("Device configuration must have an ID for update");

        var result = await _graphClient.DeviceManagement.DeviceConfigurations[id]
            .PatchAsync(config, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetDeviceConfigurationAsync(id, cancellationToken), "device configuration");
    }

    public async Task DeleteDeviceConfigurationAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.DeviceConfigurations[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }

    public async Task<List<DeviceConfigurationAssignment>> GetAssignmentsAsync(string configId, CancellationToken cancellationToken = default)
    {
        var response = await _graphClient.DeviceManagement.DeviceConfigurations[configId]
            .Assignments.GetAsync(cancellationToken: cancellationToken);

        return response?.Value ?? [];
    }
}
