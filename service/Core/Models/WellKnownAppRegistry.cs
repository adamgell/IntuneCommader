using System.Text.Json;
using System.Text.Json.Serialization;

namespace Intune.Commander.Core.Models;

/// <summary>
/// Provides a lazily-loaded, case-insensitive lookup of well-known Microsoft first-party
/// application IDs → display names, sourced from the embedded <c>MicrosoftApps.json</c> asset.
/// <para>
/// To update the list, replace <c>Assets/MicrosoftApps.json</c> with a newer version
/// and rebuild — no code changes required.
/// </para>
/// </summary>
public static class WellKnownAppRegistry
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> _apps = new(LoadFromEmbeddedResource);

    /// <summary>
    /// Dictionary mapping AppId → AppDisplayName for all entries in MicrosoftApps.json.
    /// Loaded once on first access; thread-safe.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Apps => _apps.Value;

    /// <summary>
    /// Resolves an application ID to its well-known display name.
    /// Returns the original <paramref name="appId"/> if not found.
    /// </summary>
    public static string Resolve(string? appId)
    {
        if (string.IsNullOrEmpty(appId)) return string.Empty;
        return Apps.TryGetValue(appId, out var name) ? name : appId;
    }

    private static IReadOnlyDictionary<string, string> LoadFromEmbeddedResource()
    {
        var assembly = typeof(WellKnownAppRegistry).Assembly;
        const string resourceName = "Intune.Commander.Core.Assets.MicrosoftApps.json";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var entries = JsonSerializer.Deserialize<List<MicrosoftAppEntry>>(stream) ?? [];

        var dict = new Dictionary<string, string>(entries.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!string.IsNullOrEmpty(entry.AppId) && !string.IsNullOrEmpty(entry.AppDisplayName))
                dict.TryAdd(entry.AppId, entry.AppDisplayName);
        }
        return dict;
    }

    private sealed class MicrosoftAppEntry
    {
        [JsonPropertyName("AppId")]
        public string? AppId { get; set; }

        [JsonPropertyName("AppDisplayName")]
        public string? AppDisplayName { get; set; }
    }
}
