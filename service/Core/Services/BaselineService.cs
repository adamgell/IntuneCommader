using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Intune.Commander.Core.Models;
using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public sealed class BaselineService : IBaselineService
{
    private const string ScResource = "Intune.Commander.Core.Assets.oib-sc-baselines.json.gz";
    private const string EsResource = "Intune.Commander.Core.Assets.oib-es-baselines.json.gz";
    private const string ComplianceResource = "Intune.Commander.Core.Assets.oib-compliance-baselines.json.gz";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Lazy<IReadOnlyList<BaselinePolicy>> _scBaselines;
    private readonly Lazy<IReadOnlyList<BaselinePolicy>> _esBaselines;
    private readonly Lazy<IReadOnlyList<BaselinePolicy>> _complianceBaselines;

    public BaselineService()
    {
        _scBaselines = new(() => LoadBaselines(ScResource, BaselinePolicyType.SettingsCatalog));
        _esBaselines = new(() => LoadBaselines(EsResource, BaselinePolicyType.EndpointSecurity));
        _complianceBaselines = new(() => LoadBaselines(ComplianceResource, BaselinePolicyType.Compliance));
    }

    /// <summary>
    /// Constructor for unit testing with pre-loaded baselines.
    /// </summary>
    public BaselineService(IReadOnlyList<BaselinePolicy> baselines)
    {
        var sc = baselines.Where(b => b.PolicyType == BaselinePolicyType.SettingsCatalog).ToList();
        var es = baselines.Where(b => b.PolicyType == BaselinePolicyType.EndpointSecurity).ToList();
        var comp = baselines.Where(b => b.PolicyType == BaselinePolicyType.Compliance).ToList();

        _scBaselines = new Lazy<IReadOnlyList<BaselinePolicy>>(() => sc);
        _esBaselines = new Lazy<IReadOnlyList<BaselinePolicy>>(() => es);
        _complianceBaselines = new Lazy<IReadOnlyList<BaselinePolicy>>(() => comp);
    }

    public IReadOnlyList<BaselinePolicy> GetAllBaselines()
    {
        var result = new List<BaselinePolicy>();
        result.AddRange(_scBaselines.Value);
        result.AddRange(_esBaselines.Value);
        result.AddRange(_complianceBaselines.Value);
        return result;
    }

    public IReadOnlyList<BaselinePolicy> GetBaselinesByType(BaselinePolicyType type)
    {
        return type switch
        {
            BaselinePolicyType.SettingsCatalog => _scBaselines.Value,
            BaselinePolicyType.EndpointSecurity => _esBaselines.Value,
            BaselinePolicyType.Compliance => _complianceBaselines.Value,
            _ => []
        };
    }

    public IReadOnlyList<string> GetCategories(BaselinePolicyType? type = null)
    {
        var baselines = type.HasValue ? GetBaselinesByType(type.Value) : GetAllBaselines();
        return baselines
            .Select(b => b.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<BaselinePolicy> GetBaselinesByCategory(string category, BaselinePolicyType? type = null)
    {
        var baselines = type.HasValue ? GetBaselinesByType(type.Value) : GetAllBaselines();
        return baselines
            .Where(b => string.Equals(b.Category, category, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // TODO: Implement ES/Compliance comparison
    public BaselineComparisonResult CompareSettingsCatalog(
        BaselinePolicy baseline,
        IReadOnlyList<DeviceManagementConfigurationSetting> tenantSettings,
        string? tenantPolicyId = null,
        string? tenantPolicyName = null)
    {
        var baselineMap = ExtractBaselineSettings(baseline.RawJson);
        var tenantMap = ExtractTenantSettings(tenantSettings);

        var matching = new List<BaselineSettingComparison>();
        var missing = new List<BaselineSettingComparison>();
        var drifted = new List<BaselineSettingComparison>();
        var extra = new List<BaselineSettingComparison>();

        foreach (var (defId, baselineValue) in baselineMap)
        {
            if (tenantMap.TryGetValue(defId, out var tenantValue))
            {
                if (string.Equals(baselineValue, tenantValue, StringComparison.Ordinal))
                    matching.Add(new() { SettingDefinitionId = defId, BaselineValue = baselineValue, TenantValue = tenantValue });
                else
                    drifted.Add(new() { SettingDefinitionId = defId, BaselineValue = baselineValue, TenantValue = tenantValue });
                tenantMap.Remove(defId);
            }
            else
            {
                missing.Add(new() { SettingDefinitionId = defId, BaselineValue = baselineValue });
            }
        }

        foreach (var (defId, tenantValue) in tenantMap)
        {
            extra.Add(new() { SettingDefinitionId = defId, TenantValue = tenantValue });
        }

        return new BaselineComparisonResult
        {
            BaselineName = baseline.Name,
            TenantPolicyId = tenantPolicyId,
            TenantPolicyName = tenantPolicyName,
            Matching = matching,
            Missing = missing,
            Drifted = drifted,
            Extra = extra
        };
    }

    /// <summary>
    /// Symmetric compare of two live Settings Catalog policies (A vs B), reusing the
    /// same setting-value extraction as the baseline comparison. Buckets:
    /// Matching = same in both, Drifted = present in both but different,
    /// Missing = only in A, Extra = only in B. BaselineValue carries A's value,
    /// TenantValue carries B's value.
    /// </summary>
    public static BaselineComparisonResult CompareTwoPolicies(
        IReadOnlyList<DeviceManagementConfigurationSetting> policyA,
        IReadOnlyList<DeviceManagementConfigurationSetting> policyB,
        string? nameA = null,
        string? nameB = null,
        string? idA = null,
        string? idB = null)
    {
        var mapA = ExtractTenantSettings(policyA);
        var mapB = ExtractTenantSettings(policyB);

        var matching = new List<BaselineSettingComparison>();
        var missing = new List<BaselineSettingComparison>();
        var drifted = new List<BaselineSettingComparison>();
        var extra = new List<BaselineSettingComparison>();

        foreach (var (defId, valueA) in mapA)
        {
            if (mapB.TryGetValue(defId, out var valueB))
            {
                if (string.Equals(valueA, valueB, StringComparison.Ordinal))
                    matching.Add(new() { SettingDefinitionId = defId, BaselineValue = valueA, TenantValue = valueB });
                else
                    drifted.Add(new() { SettingDefinitionId = defId, BaselineValue = valueA, TenantValue = valueB });
                mapB.Remove(defId);
            }
            else
            {
                missing.Add(new() { SettingDefinitionId = defId, BaselineValue = valueA });
            }
        }

        foreach (var (defId, valueB) in mapB)
            extra.Add(new() { SettingDefinitionId = defId, TenantValue = valueB });

        return new BaselineComparisonResult
        {
            BaselineName = string.IsNullOrWhiteSpace(nameA) ? (idA ?? "Policy A") : nameA,
            TenantPolicyId = idB,
            TenantPolicyName = string.IsNullOrWhiteSpace(nameB) ? idB : nameB,
            Matching = matching,
            Missing = missing,
            Drifted = drifted,
            Extra = extra
        };
    }

    /// <summary>
    /// Parses the OIB category from a filename.
    /// OIB naming convention: "Win - OIB - {Type} - {Category} - {D/U} - {SubCategory}"
    /// </summary>
    public static string ParseCategory(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var parts = name.Split(" - ");
        return parts.Length >= 4 ? parts[3].Trim() : "General";
    }

    private static Dictionary<string, string> ExtractBaselineSettings(JsonElement rawJson)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!rawJson.TryGetProperty("settings", out var settings) ||
            settings.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var setting in settings.EnumerateArray())
        {
            if (!setting.TryGetProperty("settingInstance", out var inst))
                continue;
            if (!inst.TryGetProperty("settingDefinitionId", out var defIdElem))
                continue;

            var defId = defIdElem.GetString();
            if (string.IsNullOrEmpty(defId))
                continue;

            result[defId] = ExtractValueFromJson(inst);
        }

        return result;
    }

    private static Dictionary<string, string> ExtractTenantSettings(
        IReadOnlyList<DeviceManagementConfigurationSetting> settings)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var setting in settings)
        {
            var defId = setting.SettingInstance?.SettingDefinitionId;
            if (string.IsNullOrEmpty(defId))
                continue;

            result[defId] = ExtractValueFromInstance(setting.SettingInstance);
        }

        return result;
    }

    private static string ExtractValueFromJson(JsonElement settingInstance)
    {
        if (settingInstance.TryGetProperty("choiceSettingValue", out var csv) &&
            csv.TryGetProperty("value", out var cv))
            return cv.GetString() ?? "";

        if (settingInstance.TryGetProperty("simpleSettingValue", out var ssv) &&
            ssv.TryGetProperty("value", out var sv))
            return sv.ValueKind == JsonValueKind.String ? sv.GetString() ?? "" : sv.GetRawText();

        foreach (var prop in settingInstance.EnumerateObject())
        {
            if (prop.Name != "settingDefinitionId" && prop.Name != "@odata.type" &&
                (prop.Name.EndsWith("Value", StringComparison.Ordinal) ||
                 prop.Name.EndsWith("CollectionValue", StringComparison.Ordinal)))
                return prop.Value.GetRawText();
        }

        return "";
    }

    private static string ExtractValueFromInstance(DeviceManagementConfigurationSettingInstance? instance)
    {
        if (instance is null)
            return "";

        switch (instance)
        {
            case DeviceManagementConfigurationChoiceSettingInstance c:
                return c.ChoiceSettingValue?.Value ?? "";
            case DeviceManagementConfigurationSimpleSettingInstance s:
                return ExtractSimpleValue(s.SimpleSettingValue);
            default:
                // For group/collection/other instance types, serialize to JSON and reuse
                // the same extraction logic as the baseline JSON path so comparisons are consistent.
                try
                {
                    var json = JsonSerializer.Serialize(instance, JsonOptions);
                    using var doc = JsonDocument.Parse(json);
                    return ExtractValueFromJson(doc.RootElement);
                }
                catch (JsonException)
                {
                    return "";
                }
        }
    }

    private static string ExtractSimpleValue(DeviceManagementConfigurationSimpleSettingValue? value)
    {
        return value switch
        {
            DeviceManagementConfigurationIntegerSettingValue iv => iv.Value?.ToString() ?? "",
            DeviceManagementConfigurationStringSettingValue sv => sv.Value ?? "",
            DeviceManagementConfigurationSecretSettingValue sv => sv.Value ?? "",
            _ => ""
        };
    }

    private static IReadOnlyList<BaselinePolicy> LoadBaselines(string resourceName, BaselinePolicyType type)
    {
        try
        {
            var assembly = typeof(BaselineService).Assembly;
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return [];

            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            var entries = JsonSerializer.Deserialize<List<EmbeddedBaselineEntry>>(gzip, JsonOptions) ?? [];

            return entries.Select(e => new BaselinePolicy
            {
                PolicyType = type,
                Name = Path.GetFileNameWithoutExtension(e.FileName),
                Description = ExtractDescription(e.RawJson),
                FileName = e.FileName,
                Category = ParseCategory(e.FileName),
                RawJson = e.RawJson
            }).ToList();
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[BaselineService] Failed to load {resourceName}: {ex.Message}");
            return [];
        }
    }

    private static string? ExtractDescription(JsonElement rawJson)
    {
        return rawJson.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String
            ? desc.GetString()
            : null;
    }

    private sealed class EmbeddedBaselineEntry
    {
        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = string.Empty;

        [JsonPropertyName("rawJson")]
        public JsonElement RawJson { get; set; }
    }
}
