using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IReusablePolicySettingService
{
    Task<List<DeviceManagementReusablePolicySetting>> ListReusablePolicySettingsAsync(CancellationToken cancellationToken = default);
    Task<DeviceManagementReusablePolicySetting?> GetReusablePolicySettingAsync(string id, CancellationToken cancellationToken = default);
    Task<DeviceManagementReusablePolicySetting> CreateReusablePolicySettingAsync(DeviceManagementReusablePolicySetting setting, CancellationToken cancellationToken = default);
    Task<DeviceManagementReusablePolicySetting> UpdateReusablePolicySettingAsync(DeviceManagementReusablePolicySetting setting, CancellationToken cancellationToken = default);
    Task DeleteReusablePolicySettingAsync(string id, CancellationToken cancellationToken = default);
}
