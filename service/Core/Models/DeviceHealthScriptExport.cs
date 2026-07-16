using System.Text.Json.Serialization;
using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Models;

public class DeviceHealthScriptExport
{
    [JsonPropertyName("script")]
    public required DeviceHealthScript Script { get; set; }

    [JsonPropertyName("assignments")]
    public List<DeviceHealthScriptAssignment> Assignments { get; set; } = [];
}
