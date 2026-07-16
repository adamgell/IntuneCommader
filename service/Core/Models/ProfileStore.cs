using System.Text.Json.Serialization;

namespace Intune.Commander.Core.Models;

public class ProfileStore
{
    [JsonPropertyName("profiles")]
    public List<TenantProfile> Profiles { get; set; } = [];

    [JsonPropertyName("activeProfileId")]
    public string? ActiveProfileId { get; set; }
}
