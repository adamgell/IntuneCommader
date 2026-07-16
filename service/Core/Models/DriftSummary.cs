using System.Text.Json.Serialization;

namespace Intune.Commander.Core.Models;

public sealed class DriftSummary
{
    [JsonPropertyName("critical")]
    public int Critical { get; init; }

    [JsonPropertyName("high")]
    public int High { get; init; }

    [JsonPropertyName("medium")]
    public int Medium { get; init; }

    [JsonPropertyName("low")]
    public int Low { get; init; }
}
