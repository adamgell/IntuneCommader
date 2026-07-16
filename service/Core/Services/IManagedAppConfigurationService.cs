using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IManagedAppConfigurationService
{
    Task<List<ManagedDeviceMobileAppConfiguration>> ListManagedDeviceAppConfigurationsAsync(CancellationToken cancellationToken = default);
    Task<ManagedDeviceMobileAppConfiguration?> GetManagedDeviceAppConfigurationAsync(string id, CancellationToken cancellationToken = default);
    Task<ManagedDeviceMobileAppConfiguration> CreateManagedDeviceAppConfigurationAsync(ManagedDeviceMobileAppConfiguration configuration, CancellationToken cancellationToken = default);
    Task<ManagedDeviceMobileAppConfiguration> UpdateManagedDeviceAppConfigurationAsync(ManagedDeviceMobileAppConfiguration configuration, CancellationToken cancellationToken = default);
    Task DeleteManagedDeviceAppConfigurationAsync(string id, CancellationToken cancellationToken = default);

    Task<List<TargetedManagedAppConfiguration>> ListTargetedManagedAppConfigurationsAsync(CancellationToken cancellationToken = default);
    Task<TargetedManagedAppConfiguration?> GetTargetedManagedAppConfigurationAsync(string id, CancellationToken cancellationToken = default);
    Task<TargetedManagedAppConfiguration> CreateTargetedManagedAppConfigurationAsync(TargetedManagedAppConfiguration configuration, CancellationToken cancellationToken = default);
    Task<TargetedManagedAppConfiguration> UpdateTargetedManagedAppConfigurationAsync(TargetedManagedAppConfiguration configuration, CancellationToken cancellationToken = default);
    Task DeleteTargetedManagedAppConfigurationAsync(string id, CancellationToken cancellationToken = default);
}
