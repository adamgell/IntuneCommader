namespace Intune.Commander.Core.Models;

public sealed class BaselineComparisonResult
{
    public string BaselineName { get; set; } = string.Empty;
    public string? TenantPolicyId { get; set; }
    public string? TenantPolicyName { get; set; }
    public IReadOnlyList<BaselineSettingComparison> Matching { get; set; } = [];
    public IReadOnlyList<BaselineSettingComparison> Missing { get; set; } = [];
    public IReadOnlyList<BaselineSettingComparison> Drifted { get; set; } = [];
    public IReadOnlyList<BaselineSettingComparison> Extra { get; set; } = [];
}

public sealed class BaselineSettingComparison
{
    public string SettingDefinitionId { get; set; } = string.Empty;
    public string? BaselineValue { get; set; }
    public string? TenantValue { get; set; }
}
