using System.Text.Json.Serialization;

namespace Intune.Commander.Core.Models;

public sealed class DriftReport
{
    [JsonPropertyName("tenant")]
    public string Tenant { get; init; } = string.Empty;

    [JsonPropertyName("scanTime")]
    public DateTimeOffset ScanTime { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("driftDetected")]
    public bool DriftDetected { get; init; }

    [JsonPropertyName("summary")]
    public DriftSummary Summary { get; init; } = new();

    [JsonPropertyName("changes")]
    public IReadOnlyList<DriftChange> Changes { get; init; } = [];
}
