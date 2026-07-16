using System.Text.Json.Serialization;
using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Models;

/// <summary>
/// Wrapper used for export/import of device shell scripts with their assignments.
/// The Graph SDK model doesn't include assignments in the base object.
/// </summary>
public class DeviceShellScriptExport
{
    [JsonPropertyName("script")]
    public required DeviceShellScript Script { get; set; }

    [JsonPropertyName("assignments")]
    public List<DeviceManagementScriptAssignment> Assignments { get; set; } = [];
}
