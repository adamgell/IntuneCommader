using System.Text.Json.Serialization;
using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Models;

/// <summary>
/// Wrapper used for export/import of device management scripts with their assignments.
/// The Graph SDK model doesn't include assignments in the base object.
/// </summary>
public class DeviceManagementScriptExport
{
    [JsonPropertyName("script")]
    public required DeviceManagementScript Script { get; set; }

    [JsonPropertyName("assignments")]
    public List<DeviceManagementScriptAssignment> Assignments { get; set; } = [];
}
