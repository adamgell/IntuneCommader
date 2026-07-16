using System.Text.Json;
using System.Text.Json.Nodes;

namespace CmProjectX.Api;

// M20 — golden-template inheritance: effective = golden ⊕ groupOverride ⊕ tenantOverride.
// The override layers are sparse JSON-merge-patch documents (RFC 7386 semantics): an
// override touches only the fields it names; everything else inherits from the golden.
// Resolution is field-level, so a tenant override of `minimumLength: 8` over a golden
// `passcode 6 digits` changes ONLY that field. This is the same layering the design
// describes; drift is then effective-golden vs. live-tenant through JsonDrift.Diff.
//
// Cross-tenant references (groups, scope tags, filters) must NOT inherit raw — per the
// design they route through each target's MigrationTable for originalId→newId remap.
// That remap is a per-tenant write concern; this resolver layers the field values, and
// the gated M13 replay is where each target's MigrationTable applies (deferred wiring —
// see docs/part-ii/M20-fleet.md and the FleetEndpoints campaign notes).
internal static class GoldenResolver
{
    // Apply a stack of sparse override patches over a base body, in order. Each patch
    // is RFC 7386 JSON-merge-patch: object keys recurse, null deletes, scalars/arrays
    // replace. Returns the merged JSON text.
    //
    // A `null` element in `patches` means "no override at this layer" and is SKIPPED —
    // it must NOT wipe the accumulated result. This matters because the layered resolve
    // (golden ⊕ groupOverride ⊕ tenantOverride, plus the stored-template layers M20 wires
    // in) routinely passes null for a layer with no override; treating that null as a
    // wholesale-replace would erase the golden and diff against an empty body. (A null
    // *member value* inside a patch object still deletes that member, per RFC 7386 —
    // that path is handled in Merge and is unaffected.)
    public static string Resolve(string baseJson, params JsonNode?[] patches)
    {
        JsonNode? result = Parse(baseJson) ?? new JsonObject();
        foreach (var patch in patches)
        {
            if (patch is null) continue; // no override at this layer — inherit, don't wipe
            result = Merge(result, patch);
        }
        return result?.ToJsonString() ?? "{}";
    }

    // Build the ordered override layers for one target tenant, later-wins:
    //   template.groupOverrides[groupId] ⊕ template.tenantOverrides[tenantId]
    //   ⊕ inline[groupId] ⊕ inline[tenantId]
    // The stored template (fleet-templates.json) is the PERSISTED desired-state; the
    // inline request overrides are per-campaign ad-hoc tweaks, so they apply last and win.
    // Any absent layer is null and is skipped by Resolve. Feed the result to Resolve as
    //   Resolve(goldenBaselineJson, LayersFor(...)).
    public static JsonNode?[] LayersFor(
        GoldenTemplateDto? template,
        Dictionary<string, JsonElement>? inlineOverrides,
        string groupId, string tenantId) =>
        new[]
        {
            OverrideFor(template?.GroupOverrides, groupId),
            OverrideFor(template?.TenantOverrides, tenantId),
            OverrideFor(inlineOverrides, groupId),
            OverrideFor(inlineOverrides, tenantId),
        };

    // RFC 7386 merge-patch of `patch` onto `target`. Returns the merged node.
    public static JsonNode? Merge(JsonNode? target, JsonNode? patch)
    {
        if (patch is not JsonObject patchObj)
            return patch?.DeepClone(); // non-object patch replaces wholesale

        var result = target as JsonObject ?? new JsonObject();
        // Clone so we never mutate the caller's tree.
        result = (JsonObject)result.DeepClone();

        foreach (var kv in patchObj)
        {
            if (kv.Value is null)
            {
                result.Remove(kv.Key); // null member deletes
            }
            else
            {
                var merged = Merge(result.TryGetPropertyValue(kv.Key, out var existing) ? existing : null, kv.Value);
                result[kv.Key] = merged?.DeepClone();
            }
        }
        return result;
    }

    // A per-tenant override map (Dictionary<tenantId, JsonElement>) → the patch node for
    // one key, or null when absent / empty.
    public static JsonNode? OverrideFor(Dictionary<string, JsonElement>? overrides, string? key)
    {
        if (overrides is null || key is null) return null;
        if (!overrides.TryGetValue(key, out var el)) return null;
        // Defensive: a malformed/undefined override element must be ignored, never abort
        // the whole campaign/drift fan-out (this runs once, before the per-tenant try).
        try { return JsonNode.Parse(el.GetRawText()); }
        catch { return null; }
    }

    private static JsonNode? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonNode.Parse(json); }
        catch (JsonException) { return null; }
    }
}
