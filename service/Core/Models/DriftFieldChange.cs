using System.Text.Json.Serialization;

namespace Intune.Commander.Core.Models;

public sealed class DriftFieldChange
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("baseline")]
    public object? Baseline { get; init; }

    [JsonPropertyName("current")]
    public object? Current { get; init; }
}
