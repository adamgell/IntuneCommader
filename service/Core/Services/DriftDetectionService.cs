using System.Text.Json;
using System.Text.Json.Nodes;
using Intune.Commander.Core.Models;

namespace Intune.Commander.Core.Services;

public sealed class DriftDetectionService(IExportNormalizer normalizer) : IDriftDetectionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<DriftReport> CompareAsync(
        string baselinePath,
        string currentPath,
        DriftSeverity minSeverity = DriftSeverity.Low,
        IEnumerable<string>? objectTypes = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(baselinePath))
            throw new DirectoryNotFoundException($"Baseline directory not found: {baselinePath}");
        if (!Directory.Exists(currentPath))
            throw new DirectoryNotFoundException($"Current directory not found: {currentPath}");

        var filter = objectTypes?.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var baselineFiles = GetJsonFileMap(baselinePath, filter);
        var currentFiles = GetJsonFileMap(currentPath, filter);
        var allRelativePaths = baselineFiles.Keys.Union(currentFiles.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

        var changes = new List<DriftChange>();

        foreach (var relativePath in allRelativePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var objectType = GetObjectType(relativePath);
            var name = Path.GetFileNameWithoutExtension(relativePath);

            baselineFiles.TryGetValue(relativePath, out var baselineFile);
            currentFiles.TryGetValue(relativePath, out var currentFile);

            if (baselineFile is null)
            {
                changes.Add(new DriftChange
                {
                    ObjectType = objectType,
                    Name = name,
                    ChangeType = "added",
                    Severity = DriftSeverity.Medium
                });
                continue;
            }

            if (currentFile is null)
            {
                changes.Add(new DriftChange
                {
                    ObjectType = objectType,
                    Name = name,
                    ChangeType = "deleted",
                    Severity = DriftSeverity.Critical
                });
                continue;
            }

            var baselineJson = await File.ReadAllTextAsync(baselineFile, cancellationToken);
            var currentJson = await File.ReadAllTextAsync(currentFile, cancellationToken);

            var normalizedBaseline = normalizer.NormalizeJson(baselineJson);
            var normalizedCurrent = normalizer.NormalizeJson(currentJson);

            if (string.Equals(normalizedBaseline, normalizedCurrent, StringComparison.Ordinal))
                continue;

            var fieldChanges = GetFieldChanges(normalizedBaseline, normalizedCurrent);
            var severity = DetermineSeverity(fieldChanges);

            changes.Add(new DriftChange
            {
                ObjectType = objectType,
                Name = name,
                ChangeType = "modified",
                Severity = severity,
                Fields = fieldChanges
            });
        }

        var filteredChanges = changes
            .Where(c => c.Severity >= minSeverity)
            .ToList();

        return new DriftReport
        {
            DriftDetected = filteredChanges.Count > 0,
            Summary = new DriftSummary
            {
                Critical = filteredChanges.Count(c => c.Severity == DriftSeverity.Critical),
                High = filteredChanges.Count(c => c.Severity == DriftSeverity.High),
                Medium = filteredChanges.Count(c => c.Severity == DriftSeverity.Medium),
                Low = filteredChanges.Count(c => c.Severity == DriftSeverity.Low)
            },
            Changes = filteredChanges
        };
    }

    private static Dictionary<string, string> GetJsonFileMap(string root, HashSet<string>? objectTypesFilter)
    {
        var files = Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Where(file => !string.Equals(Path.GetFileName(file), "migration-table.json", StringComparison.OrdinalIgnoreCase));

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(root, file);
            var objectType = GetObjectType(relative);
            if (objectTypesFilter is not null && !objectTypesFilter.Contains(objectType))
                continue;

            map[relative] = file;
        }

        return map;
    }

    private static string GetObjectType(string relativePath)
    {
        var firstSegment = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return firstSegment switch
        {
            "CompliancePolicies" => "CompliancePolicy",
            "DeviceConfigurations" => "DeviceConfiguration",
            "Applications" => "Application",
            _ => firstSegment
        };
    }

    private static List<DriftFieldChange> GetFieldChanges(string baselineJson, string currentJson)
    {
        var baselineNode = JsonNode.Parse(baselineJson);
        var currentNode = JsonNode.Parse(currentJson);
        var changes = new List<DriftFieldChange>();

        CompareNode(string.Empty, baselineNode, currentNode, changes);
        return changes;
    }

    private static void CompareNode(string path, JsonNode? baseline, JsonNode? current, List<DriftFieldChange> changes)
    {
        if (baseline is null && current is null)
            return;

        if (baseline is null || current is null)
        {
            changes.Add(new DriftFieldChange
            {
                Path = string.IsNullOrEmpty(path) ? "$" : path,
                Baseline = baseline is null ? null : ToObject(baseline),
                Current = current is null ? null : ToObject(current)
            });
            return;
        }

        if (baseline is JsonObject baselineObject && current is JsonObject currentObject)
        {
            var allKeys = baselineObject.Select(k => k.Key)
                .Union(currentObject.Select(k => k.Key), StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

            foreach (var key in allKeys)
            {
                var childPath = string.IsNullOrEmpty(path) ? key : $"{path}.{key}";
                CompareNode(childPath, baselineObject[key], currentObject[key], changes);
            }

            return;
        }

        if (baseline is JsonArray baselineArray && current is JsonArray currentArray)
        {
            var max = Math.Max(baselineArray.Count, currentArray.Count);
            for (var i = 0; i < max; i++)
            {
                var childPath = $"{path}[{i}]";
                CompareNode(
                    childPath,
                    i < baselineArray.Count ? baselineArray[i] : null,
                    i < currentArray.Count ? currentArray[i] : null,
                    changes);
            }

            return;
        }

        var baselineSerialized = baseline.ToJsonString();
        var currentSerialized = current.ToJsonString();
        if (!string.Equals(baselineSerialized, currentSerialized, StringComparison.Ordinal))
        {
            changes.Add(new DriftFieldChange
            {
                Path = string.IsNullOrEmpty(path) ? "$" : path,
                Baseline = ToObject(baseline),
                Current = ToObject(current)
            });
        }
    }

    private static object? ToObject(JsonNode node) =>
        JsonSerializer.Deserialize<object>(node.ToJsonString(), JsonOptions);

    private static DriftSeverity DetermineSeverity(IEnumerable<DriftFieldChange> fieldChanges)
    {
        var maxSeverity = DriftSeverity.Low;
        foreach (var fieldChange in fieldChanges)
        {
            var severity = ClassifyFieldChange(fieldChange);
            if (severity > maxSeverity)
                maxSeverity = severity;
        }

        return maxSeverity;
    }

    private static DriftSeverity ClassifyFieldChange(DriftFieldChange fieldChange)
    {
        var path = fieldChange.Path.ToLowerInvariant();

        if (path.Contains("assignment"))
            return DriftSeverity.High;

        if (path.Contains("displayname") || path.Contains("description"))
            return DriftSeverity.Low;

        if (path.Contains("password") || path.Contains("mfa") || path.Contains("encryption") || path.Contains("bitlocker"))
            return DriftSeverity.Critical;

        if (path.Contains("isenabled") || path == "state" || path.EndsWith(".state", StringComparison.Ordinal))
        {
            var current = fieldChange.Current?.ToString();
            if (string.Equals(current, "reportOnly", StringComparison.OrdinalIgnoreCase))
                return DriftSeverity.High;
            if (string.Equals(current, "false", StringComparison.OrdinalIgnoreCase))
                return DriftSeverity.Critical;
        }

        return DriftSeverity.Medium;
    }
}
