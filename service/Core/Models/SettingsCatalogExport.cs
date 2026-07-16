using System.Text.Json.Serialization;
using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Models;

/// <summary>
/// Wrapper used for export/import of Settings Catalog policies with their settings and assignments.
/// Settings are a separate sub-resource in Graph and must be fetched and re-applied on import.
/// </summary>
public class SettingsCatalogExport
{
    [JsonPropertyName("policy")]
    public required DeviceManagementConfigurationPolicy Policy { get; set; }

    [JsonPropertyName("settings")]
    public List<DeviceManagementConfigurationSetting> Settings { get; set; } = [];

    [JsonPropertyName("assignments")]
    public List<DeviceManagementConfigurationPolicyAssignment> Assignments { get; set; } = [];
}
