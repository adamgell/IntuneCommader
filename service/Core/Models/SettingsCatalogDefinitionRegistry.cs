using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Intune.Commander.Core.Models;

/// <summary>
/// Provides lazily-loaded, in-memory lookup of Intune Settings Catalog definitions
/// and categories from embedded JSON assets. The JSON is fetched daily by a GitHub
/// Actions workflow and baked into the .exe at build time.
/// <para>
/// To update: run <c>scripts/Fetch-SettingsCatalogDefinitions.ps1</c> or let the
/// <c>update-settings-catalog</c> workflow update the Assets automatically.
/// </para>
/// </summary>
public static class SettingsCatalogDefinitionRegistry
{
    private static readonly Lazy<IReadOnlyDictionary<string, SettingDefinitionEntry>> _definitions = new(LoadDefinitions);
    private static readonly Lazy<IReadOnlyDictionary<string, SettingCategoryEntry>> _categories = new(LoadCategories);

    /// <summary>
    /// All setting definitions keyed by definition ID. Loaded once on first access.
    /// </summary>
    public static IReadOnlyDictionary<string, SettingDefinitionEntry> Definitions => _definitions.Value;

    /// <summary>
    /// All setting categories keyed by category ID. Loaded once on first access.
    /// </summary>
    public static IReadOnlyDictionary<string, SettingCategoryEntry> Categories => _categories.Value;

    /// <summary>
    /// Resolves a setting definition ID to its human-readable display name.
    /// Returns <c>null</c> if not found in the embedded definitions.
    /// </summary>
    public static string? ResolveDisplayName(string? definitionId)
    {
        if (string.IsNullOrEmpty(definitionId)) return null;
        return Definitions.TryGetValue(definitionId, out var def) ? def.DisplayName : null;
    }

    /// <summary>
    /// Resolves a setting definition ID to its description.
    /// Returns <c>null</c> if not found.
    /// </summary>
    public static string? ResolveDescription(string? definitionId)
    {
        if (string.IsNullOrEmpty(definitionId)) return null;
        return Definitions.TryGetValue(definitionId, out var def) ? def.Description : null;
    }

    /// <summary>
    /// Resolves a setting definition ID to its full embedded definition entry.
    /// Returns <c>null</c> if not found.
    /// </summary>
    public static SettingDefinitionEntry? ResolveDefinition(string? definitionId)
    {
        if (string.IsNullOrEmpty(definitionId)) return null;
        return Definitions.TryGetValue(definitionId, out var def) ? def : null;
    }

    /// <summary>
    /// Resolves a setting definition ID to its help text.
    /// Returns <c>null</c> if not found.
    /// </summary>
    public static string? ResolveHelpText(string? definitionId)
    {
        if (string.IsNullOrEmpty(definitionId)) return null;
        return Definitions.TryGetValue(definitionId, out var def) ? def.HelpText : null;
    }

    /// <summary>
    /// Resolves a choice option value to its human-readable display name using the embedded schema.
    /// Returns <c>null</c> if the option cannot be found.
    /// </summary>
    public static string? ResolveOptionDisplayName(string? definitionId, string? optionId)
    {
        var option = ResolveOption(definitionId, optionId);
        return option?.DisplayName ?? option?.Name;
    }

    /// <summary>
    /// Resolves a choice option value to its description using the embedded schema.
    /// Returns <c>null</c> if the option cannot be found.
    /// </summary>
    public static string? ResolveOptionDescription(string? definitionId, string? optionId)
    {
        var option = ResolveOption(definitionId, optionId);
        return option?.Description;
    }

    /// <summary>
    /// Resolves a category ID to its display name.
    /// Returns the original <paramref name="categoryId"/> if not found.
    /// </summary>
    public static string ResolveCategoryName(string? categoryId)
    {
        if (string.IsNullOrEmpty(categoryId)) return string.Empty;
        return Categories.TryGetValue(categoryId, out var cat) ? cat.DisplayName ?? categoryId : categoryId;
    }

    /// <summary>
    /// Returns whether embedded definitions are available (non-empty).
    /// Will be false if the placeholder JSON has not been replaced by the workflow.
    /// </summary>
    public static bool HasDefinitions => Definitions.Count > 0;

    private static SettingDefinitionOption? ResolveOption(string? definitionId, string? optionId)
    {
        if (string.IsNullOrEmpty(definitionId) || string.IsNullOrEmpty(optionId)) return null;
        if (!Definitions.TryGetValue(definitionId, out var definition) || definition.Options is not { Count: > 0 }) return null;

        return definition.Options.FirstOrDefault(option =>
            string.Equals(option.ItemId, optionId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(option.Name, optionId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(option.DisplayName, optionId, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyDictionary<string, SettingDefinitionEntry> LoadDefinitions()
    {
        var entries = LoadGzipResource<SettingDefinitionEntry>(
            "Intune.Commander.Core.Assets.settings-catalog-definitions.json.gz");

        var dict = new Dictionary<string, SettingDefinitionEntry>(entries.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!string.IsNullOrEmpty(entry.Id))
                dict.TryAdd(entry.Id, entry);
        }
        return dict;
    }

    private static IReadOnlyDictionary<string, SettingCategoryEntry> LoadCategories()
    {
        var entries = LoadGzipResource<SettingCategoryEntry>(
            "Intune.Commander.Core.Assets.settings-catalog-categories.json.gz");

        var dict = new Dictionary<string, SettingCategoryEntry>(entries.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!string.IsNullOrEmpty(entry.Id))
                dict.TryAdd(entry.Id, entry);
        }
        return dict;
    }

    private static List<T> LoadGzipResource<T>(string resourceName)
    {
        try
        {
            var assembly = typeof(SettingsCatalogDefinitionRegistry).Assembly;
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return [];

            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            return JsonSerializer.Deserialize<List<T>>(gzip, JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        {
            // This runs inside Lazy<>, so an unhandled exception would be cached
            // permanently and re-thrown on every future access for the lifetime of
            // the process. Return empty so the app falls back to string-parsing
            // display names instead of being permanently broken.
            System.Diagnostics.Debug.WriteLine(
                $"[SettingsCatalogDefinitionRegistry] Failed to load {resourceName}: {ex.Message}");
            return [];
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

/// <summary>
/// Represents a single setting definition from the Settings Catalog.
/// Matches the shape returned by GET /beta/deviceManagement/configurationSettings.
/// </summary>
public sealed class SettingDefinitionEntry
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("helpText")]
    public string? HelpText { get; set; }

    [JsonPropertyName("categoryId")]
    public string? CategoryId { get; set; }

    [JsonPropertyName("keywords")]
    public List<string>? Keywords { get; set; }

    [JsonPropertyName("baseUri")]
    public string? BaseUri { get; set; }

    [JsonPropertyName("offsetUri")]
    public string? OffsetUri { get; set; }

    [JsonPropertyName("options")]
    public List<SettingDefinitionOption>? Options { get; set; }

    [JsonPropertyName("defaultOptionId")]
    public string? DefaultOptionId { get; set; }

    /// <summary>The OData type discriminator (e.g. #microsoft.graph.deviceManagementConfigurationChoiceSettingDefinition).</summary>
    [JsonPropertyName("@odata.type")]
    public string? OdataType { get; set; }
}

/// <summary>
/// Represents an allowed option for a choice-type setting definition.
/// </summary>
public sealed class SettingDefinitionOption
{
    [JsonPropertyName("itemId")]
    public string? ItemId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// Represents a category in the Settings Catalog category tree.
/// Matches the shape returned by GET /beta/deviceManagement/configurationCategories.
/// </summary>
public sealed class SettingCategoryEntry
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("platforms")]
    public string? Platforms { get; set; }

    [JsonPropertyName("technologies")]
    public string? Technologies { get; set; }

    [JsonPropertyName("parentCategoryId")]
    public string? ParentCategoryId { get; set; }

    [JsonPropertyName("rootCategoryId")]
    public string? RootCategoryId { get; set; }

    [JsonPropertyName("childCategoryIds")]
    public List<string>? ChildCategoryIds { get; set; }
}
