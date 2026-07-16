using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface ISettingsCatalogService
{
    Task<List<DeviceManagementConfigurationPolicy>> ListSettingsCatalogPoliciesAsync(CancellationToken cancellationToken = default);
    Task<DeviceManagementConfigurationPolicy?> GetSettingsCatalogPolicyAsync(string id, CancellationToken cancellationToken = default);
    Task<List<DeviceManagementConfigurationPolicyAssignment>> GetAssignmentsAsync(string policyId, CancellationToken cancellationToken = default);
    Task<List<DeviceManagementConfigurationSetting>> GetPolicySettingsAsync(string policyId, CancellationToken cancellationToken = default);
    Task<DeviceManagementConfigurationPolicy> CreateSettingsCatalogPolicyAsync(DeviceManagementConfigurationPolicy policy, CancellationToken cancellationToken = default);
    Task AssignSettingsCatalogPolicyAsync(string policyId, List<DeviceManagementConfigurationPolicyAssignment> assignments, CancellationToken cancellationToken = default);
    Task<DeviceManagementConfigurationPolicy> UpdateSettingsCatalogPolicyMetadataAsync(string id, DeviceManagementConfigurationPolicy policy, CancellationToken cancellationToken = default);
    Task DeleteSettingsCatalogPolicyAsync(string id, CancellationToken cancellationToken = default);
    Task UpdatePolicySettingsAsync(string policyId, List<DeviceManagementConfigurationSetting> settings, CancellationToken cancellationToken = default);
}
