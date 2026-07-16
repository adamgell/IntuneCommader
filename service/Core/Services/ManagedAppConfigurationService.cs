using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class ManagedAppConfigurationService : IManagedAppConfigurationService
{
    private readonly GraphServiceClient _graphClient;

    public ManagedAppConfigurationService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<ManagedDeviceMobileAppConfiguration>> ListManagedDeviceAppConfigurationsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<ManagedDeviceMobileAppConfiguration>();

        var response = await _graphClient.DeviceAppManagement.MobileAppConfigurations
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
                response = await _graphClient.DeviceAppManagement.MobileAppConfigurations
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

    public async Task<ManagedDeviceMobileAppConfiguration?> GetManagedDeviceAppConfigurationAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceAppManagement.MobileAppConfigurations[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<ManagedDeviceMobileAppConfiguration> CreateManagedDeviceAppConfigurationAsync(ManagedDeviceMobileAppConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.DeviceAppManagement.MobileAppConfigurations
            .PostAsync(configuration, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create managed device app configuration");
    }

    public async Task<ManagedDeviceMobileAppConfiguration> UpdateManagedDeviceAppConfigurationAsync(ManagedDeviceMobileAppConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var id = configuration.Id ?? throw new ArgumentException("Managed device app configuration must have an ID for update");

        var result = await _graphClient.DeviceAppManagement.MobileAppConfigurations[id]
            .PatchAsync(configuration, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetManagedDeviceAppConfigurationAsync(id, cancellationToken), "managed device app configuration");
    }

    public async Task DeleteManagedDeviceAppConfigurationAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceAppManagement.MobileAppConfigurations[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }

    public async Task<List<TargetedManagedAppConfiguration>> ListTargetedManagedAppConfigurationsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<TargetedManagedAppConfiguration>();

        var response = await _graphClient.DeviceAppManagement.TargetedManagedAppConfigurations
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
                response = await _graphClient.DeviceAppManagement.TargetedManagedAppConfigurations
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

    public async Task<TargetedManagedAppConfiguration?> GetTargetedManagedAppConfigurationAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceAppManagement.TargetedManagedAppConfigurations[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<TargetedManagedAppConfiguration> CreateTargetedManagedAppConfigurationAsync(TargetedManagedAppConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.DeviceAppManagement.TargetedManagedAppConfigurations
            .PostAsync(configuration, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create targeted managed app configuration");
    }

    public async Task<TargetedManagedAppConfiguration> UpdateTargetedManagedAppConfigurationAsync(TargetedManagedAppConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var id = configuration.Id ?? throw new ArgumentException("Targeted managed app configuration must have an ID for update");

        var result = await _graphClient.DeviceAppManagement.TargetedManagedAppConfigurations[id]
            .PatchAsync(configuration, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetTargetedManagedAppConfigurationAsync(id, cancellationToken), "targeted managed app configuration");
    }

    public async Task DeleteTargetedManagedAppConfigurationAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceAppManagement.TargetedManagedAppConfigurations[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }
}
