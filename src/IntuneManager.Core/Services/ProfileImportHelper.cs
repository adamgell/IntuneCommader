using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using IntuneManager.Core.Models;

namespace IntuneManager.Core.Services;

/// <summary>
/// Parses a JSON string into a list of <see cref="TenantProfile"/> objects.
/// Supports three JSON shapes: an array of profiles, a single profile object,
/// or a ProfileStore envelope ({ "profiles": [...] }).
/// Invalid entries (missing required fields, unrecognised enum values) are skipped.
/// </summary>
public static class ProfileImportHelper
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    /// <summary>
    /// Parses <paramref name="json"/> and returns all valid profiles found.
    /// Throws <see cref="JsonException"/> if the JSON is syntactically invalid.
    /// </summary>
    public static List<TenantProfile> ParseProfiles(string json)
    {
        var node = JsonNode.Parse(json); // throws JsonException on bad syntax

        if (node is JsonArray array)
            return DeserializeArray(array);

        if (node is JsonObject obj)
        {
            // ProfileStore envelope: { "profiles": [...] }
            if (obj["profiles"] is JsonArray profilesArray)
                return DeserializeArray(profilesArray);

            // Single profile object
            var single = TryDeserialize(node);
            if (single is not null && IsValid(single))
                return [single];
        }

        return [];
    }

    /// <summary>
    /// Returns true when the profile has all required fields populated.
    /// </summary>
    public static bool IsValid(TenantProfile profile) =>
        !string.IsNullOrWhiteSpace(profile.TenantId) &&
        !string.IsNullOrWhiteSpace(profile.ClientId) &&
        !string.IsNullOrWhiteSpace(profile.Name);

    private static List<TenantProfile> DeserializeArray(JsonArray array) =>
        array
            .Select(n => n is null ? null : TryDeserialize(n))
            .Where(p => p is not null && IsValid(p))
            .Select(p => p!)
            .ToList();

    private static TenantProfile? TryDeserialize(JsonNode node)
    {
        try
        {
            return node.Deserialize<TenantProfile>(_options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
