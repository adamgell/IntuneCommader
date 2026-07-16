using System.Text.Json.Serialization;

namespace Intune.Commander.Core.Models;

public sealed class DriftChange
{
    [JsonPropertyName("objectType")]
    public string ObjectType { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("changeType")]
    public string ChangeType { get; init; } = string.Empty;

    [JsonPropertyName("severity")]
    public DriftSeverity Severity { get; init; }

    [JsonPropertyName("fields")]
    public IReadOnlyList<DriftFieldChange> Fields { get; init; } = [];
}
