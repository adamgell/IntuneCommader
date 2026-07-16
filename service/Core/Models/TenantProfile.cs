using System.Text.Json.Serialization;

namespace Intune.Commander.Core.Models;

public class TenantProfile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("tenantId")]
    public required string TenantId { get; set; }

    [JsonPropertyName("clientId")]
    public required string ClientId { get; set; }

    [JsonPropertyName("cloud")]
    public CloudEnvironment Cloud { get; set; } = CloudEnvironment.Commercial;

    [JsonPropertyName("authMethod")]
    public AuthMethod AuthMethod { get; set; } = AuthMethod.Interactive;

    [JsonPropertyName("clientSecret")]
    public string? ClientSecret { get; set; }

    [JsonPropertyName("lastUsed")]
    public DateTime? LastUsed { get; set; }
}
