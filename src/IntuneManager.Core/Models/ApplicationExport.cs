using System.Text.Json.Serialization;
using Microsoft.Graph.Models;

namespace IntuneManager.Core.Models;

/// <summary>
/// Wrapper used for export of applications with their assignments.
/// The Graph SDK model doesn't include assignments in the base object.
/// </summary>
public class ApplicationExport
{
    [JsonPropertyName("application")]
    public required MobileApp Application { get; set; }

    [JsonPropertyName("assignments")]
    public List<MobileAppAssignment> Assignments { get; set; } = [];
}
