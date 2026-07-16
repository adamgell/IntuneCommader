using System.Text.Json.Serialization;
using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Models;

public class EndpointSecurityExport
{
    [JsonPropertyName("intent")]
    public required DeviceManagementIntent Intent { get; set; }

    [JsonPropertyName("assignments")]
    public List<DeviceManagementIntentAssignment> Assignments { get; set; } = [];
}
