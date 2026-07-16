using System.Text.Json;

namespace Intune.Commander.Core.Models;

public sealed class BaselinePolicy
{
    public BaselinePolicyType PolicyType { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = "General";
    public string FileName { get; set; } = string.Empty;
    public JsonElement RawJson { get; set; }
}

public enum BaselinePolicyType { SettingsCatalog, EndpointSecurity, Compliance }
