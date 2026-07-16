using System.Text.Json.Serialization;
using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Models;

public class AdministrativeTemplateExport
{
    [JsonPropertyName("template")]
    public required GroupPolicyConfiguration Template { get; set; }

    [JsonPropertyName("assignments")]
    public List<GroupPolicyConfigurationAssignment> Assignments { get; set; } = [];
}
