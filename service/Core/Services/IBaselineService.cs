using Intune.Commander.Core.Models;
using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IBaselineService
{
    IReadOnlyList<BaselinePolicy> GetAllBaselines();
    IReadOnlyList<BaselinePolicy> GetBaselinesByType(BaselinePolicyType type);
    IReadOnlyList<string> GetCategories(BaselinePolicyType? type = null);
    IReadOnlyList<BaselinePolicy> GetBaselinesByCategory(string category, BaselinePolicyType? type = null);
    BaselineComparisonResult CompareSettingsCatalog(
        BaselinePolicy baseline,
        IReadOnlyList<DeviceManagementConfigurationSetting> tenantSettings,
        string? tenantPolicyId = null, string? tenantPolicyName = null);
}
