using System.Text.Json.Serialization;
using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Models;

/// <summary>
/// Wrapper used for export/import of compliance policies with their assignments.
/// The Graph SDK model doesn't include assignments in the base object.
/// </summary>
public class CompliancePolicyExport
{
    [JsonPropertyName("policy")]
    public required DeviceCompliancePolicy Policy { get; set; }

    [JsonPropertyName("assignments")]
    public List<DeviceCompliancePolicyAssignment> Assignments { get; set; } = [];
}
