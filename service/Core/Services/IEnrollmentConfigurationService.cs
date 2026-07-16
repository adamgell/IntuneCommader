using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IEnrollmentConfigurationService
{
    Task<List<DeviceEnrollmentConfiguration>> ListEnrollmentConfigurationsAsync(CancellationToken cancellationToken = default);
    Task<List<DeviceEnrollmentConfiguration>> ListEnrollmentStatusPagesAsync(CancellationToken cancellationToken = default);
    Task<List<DeviceEnrollmentConfiguration>> ListEnrollmentRestrictionsAsync(CancellationToken cancellationToken = default);
    Task<List<DeviceEnrollmentConfiguration>> ListCoManagementSettingsAsync(CancellationToken cancellationToken = default);
    Task<DeviceEnrollmentConfiguration?> GetEnrollmentConfigurationAsync(string id, CancellationToken cancellationToken = default);
    Task<DeviceEnrollmentConfiguration> CreateEnrollmentConfigurationAsync(DeviceEnrollmentConfiguration configuration, CancellationToken cancellationToken = default);
    Task<DeviceEnrollmentConfiguration> UpdateEnrollmentConfigurationAsync(DeviceEnrollmentConfiguration configuration, CancellationToken cancellationToken = default);
    Task DeleteEnrollmentConfigurationAsync(string id, CancellationToken cancellationToken = default);
}
