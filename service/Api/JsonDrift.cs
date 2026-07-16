using System.Text.Json.Nodes;

namespace CmProjectX.Api;

// Field-level diff of two config snapshot bodies → contract-shaped DriftChange list.
// Snapshots are stored already-normalized (volatile fields stripped, keys/arrays
// sorted by the Core ExportNormalizer), so this is a structural compare: anything
// that differs here is real drift, not noise.
public static class JsonDrift
{
    public static List<DriftChangeDto> Diff(string baseJson, string headJson)
    {
        var changes = new List<DriftChangeDto>();
        JsonNode? baseNode = TryParse(baseJson);
        JsonNode? headNode = TryParse(headJson);
        Compare(baseNode, headNode, "", changes);
        return changes;
    }

    private static JsonNode? TryParse(string json)
    {
        try { return JsonNode.Parse(json); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private static void Compare(JsonNode? a, JsonNode? b, string path, List<DriftChangeDto> changes)
    {
        if (a is null && b is null) return;

        if (a is null)
        {
            changes.Add(new DriftChangeDto(PathOrRoot(path), "Added", null, b?.DeepClone()));
            return;
        }
        if (b is null)
        {
            changes.Add(new DriftChangeDto(PathOrRoot(path), "Removed", a.DeepClone(), null));
            return;
        }

        switch (a, b)
        {
            case (JsonObject ao, JsonObject bo):
                foreach (var key in Union(ao, bo))
                    Compare(ao[key], bo[key], $"{path}/{key}", changes);
                break;

            case (JsonArray aa, JsonArray ba):
                var max = Math.Max(aa.Count, ba.Count);
                for (var i = 0; i < max; i++)
                    Compare(i < aa.Count ? aa[i] : null, i < ba.Count ? ba[i] : null, $"{path}/{i}", changes);
                break;

            default:
                // Leaf values, or a type change (object↔value etc.) — compare by JSON text.
                if (a.ToJsonString() != b.ToJsonString())
                    changes.Add(new DriftChangeDto(PathOrRoot(path), "Modified", a.DeepClone(), b.DeepClone()));
                break;
        }
    }

    private static IEnumerable<string> Union(JsonObject a, JsonObject b)
    {
        var seen = new HashSet<string>();
        foreach (var kv in a) if (seen.Add(kv.Key)) yield return kv.Key;
        foreach (var kv in b) if (seen.Add(kv.Key)) yield return kv.Key;
    }

    private static string PathOrRoot(string path) => path.Length == 0 ? "/" : path;
}
