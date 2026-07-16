using System.Text.Json.Serialization;

namespace Intune.Commander.Core.Models;

public class MigrationEntry
{
    [JsonPropertyName("objectType")]
    public required string ObjectType { get; set; }

    [JsonPropertyName("originalId")]
    public required string OriginalId { get; set; }

    [JsonPropertyName("newId")]
    public string? NewId { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("exportedAt")]
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
}
