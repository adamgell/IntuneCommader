using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IConfigurationProfileService
{
    Task<List<DeviceConfiguration>> ListDeviceConfigurationsAsync(CancellationToken cancellationToken = default);
    Task<DeviceConfiguration?> GetDeviceConfigurationAsync(string id, CancellationToken cancellationToken = default);
    Task<DeviceConfiguration> CreateDeviceConfigurationAsync(DeviceConfiguration config, CancellationToken cancellationToken = default);
    Task<DeviceConfiguration> UpdateDeviceConfigurationAsync(DeviceConfiguration config, CancellationToken cancellationToken = default);
    Task DeleteDeviceConfigurationAsync(string id, CancellationToken cancellationToken = default);
    Task<List<DeviceConfigurationAssignment>> GetAssignmentsAsync(string configId, CancellationToken cancellationToken = default);
}
