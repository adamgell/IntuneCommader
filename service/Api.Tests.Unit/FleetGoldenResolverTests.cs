using System.Text.Json;
using System.Text.Json.Nodes;
using CmProjectX.Api;
using Xunit;

namespace CmProjectX.Tests.Unit;

// M20 Fleet — hermetic tests for GoldenResolver's RFC 7386 merge-patch layering, the
// engine behind effective = golden ⊕ groupOverride ⊕ tenantOverride. A campaign's
// per-tenant change set is the diff of this resolved body against the live object, so
// the layering must be exact: an override touches only the fields it names.
public class FleetGoldenResolverTests
{
    [Fact]
    public void Resolve_OverrideReplacesNamedFieldOnly()
    {
        var golden = """{"displayName":"Baseline","passcodeMinimumLength":6,"requireMfa":true}""";
        var ov = JsonNode.Parse("""{"passcodeMinimumLength":8}""");
        var result = JsonNode.Parse(GoldenResolver.Resolve(golden, ov))!.AsObject();
        Assert.Equal(8, (int)result["passcodeMinimumLength"]!);
        Assert.Equal("Baseline", (string)result["displayName"]!); // untouched
        Assert.True((bool)result["requireMfa"]!);                  // untouched
    }

    [Fact]
    public void Resolve_NullMemberDeletes()
    {
        var golden = """{"a":1,"b":2}""";
        var ov = JsonNode.Parse("""{"b":null}""");
        var result = JsonNode.Parse(GoldenResolver.Resolve(golden, ov))!.AsObject();
        Assert.True(result.ContainsKey("a"));
        Assert.False(result.ContainsKey("b")); // null member deletes (RFC 7386)
    }

    [Fact]
    public void Resolve_LayersGroupThenTenant_LastWins()
    {
        var golden = """{"x":1,"y":1,"z":1}""";
        var group = JsonNode.Parse("""{"y":2,"z":2}""");
        var tenant = JsonNode.Parse("""{"z":3}""");
        var result = JsonNode.Parse(GoldenResolver.Resolve(golden, group, tenant))!.AsObject();
        Assert.Equal(1, (int)result["x"]!);  // golden
        Assert.Equal(2, (int)result["y"]!);  // group override
        Assert.Equal(3, (int)result["z"]!);  // tenant override wins (applied last)
    }

    [Fact]
    public void Resolve_NestedObjectsMergeRecursively()
    {
        var golden = """{"settings":{"a":1,"b":2}}""";
        var ov = JsonNode.Parse("""{"settings":{"b":9}}""");
        var result = JsonNode.Parse(GoldenResolver.Resolve(golden, ov))!.AsObject();
        var s = result["settings"]!.AsObject();
        Assert.Equal(1, (int)s["a"]!); // inherited
        Assert.Equal(9, (int)s["b"]!); // overridden
    }

    [Fact]
    public void Resolve_NoPatches_ReturnsGoldenUnchanged()
    {
        var golden = """{"a":1}""";
        var result = JsonNode.Parse(GoldenResolver.Resolve(golden))!.AsObject();
        Assert.Equal(1, (int)result["a"]!);
    }

    [Fact]
    public void OverrideFor_NullOrMissing_IsNull()
    {
        Assert.Null(GoldenResolver.OverrideFor(null, "k"));
    }

    // A `null` layer means "no override here" and must be SKIPPED, not treated as a
    // wholesale-replace that wipes the golden. This is the exact shape the layered
    // resolve produces when a group (or tenant) has no override — regression guard for
    // the null-patch-erases-golden bug the M20 template wiring would otherwise trip.
    [Fact]
    public void Resolve_SkipsNullLayer_KeepsGolden()
    {
        var golden = """{"a":1,"b":2,"c":3}""";
        var tenant = JsonNode.Parse("""{"c":9}""");
        var result = JsonNode.Parse(GoldenResolver.Resolve(golden, null, tenant))!.AsObject();
        Assert.Equal(1, (int)result["a"]!); // golden preserved despite the null (group) layer
        Assert.Equal(2, (int)result["b"]!);
        Assert.Equal(9, (int)result["c"]!); // tenant override still applied
    }

    [Fact]
    public void Resolve_AllNullLayers_ReturnsGoldenUnchanged()
    {
        var golden = """{"a":1,"b":2}""";
        var result = JsonNode.Parse(GoldenResolver.Resolve(golden, null, null))!.AsObject();
        Assert.Equal(1, (int)result["a"]!);
        Assert.Equal(2, (int)result["b"]!);
    }

    // The stored-template layers (group + tenant) AND the inline per-campaign overrides
    // fold into one effective golden, later-wins: golden ⊕ templateGroup ⊕ templateTenant
    // ⊕ inlineGroup ⊕ inlineTenant. This is the wiring M20 adds so a saved override is
    // honored on every campaign/drift, while an ad-hoc inline override still wins.
    [Fact]
    public void LayersFor_FoldsTemplateAndInlineOverrides_InlineWins()
    {
        const string groupId = "grp-1";
        const string tenantId = "ten-1";
        var template = new GoldenTemplateDto(
            "gt-1", "t", "compliance-policies", "Win10 Baseline",
            new GoldenRefDto("goldenTenant", "golden-ten", null),
            BaselineJson: null,
            GroupOverrides: Dict($$"""{ "{{groupId}}": { "g": 1, "shared": "fromGroup" } }"""),
            TenantOverrides: Dict($$"""{ "{{tenantId}}": { "t": 2, "shared": "fromTenant" } }"""),
            CreatedUtc: "2026-06-24T00:00:00Z");
        var inline = Dict($$"""{ "{{tenantId}}": { "shared": "fromInline", "i": 3 } }""");

        var golden = """{"base":0,"g":0,"t":0,"i":0,"shared":"fromGolden"}""";
        var layers = GoldenResolver.LayersFor(template, inline, groupId, tenantId);
        var eff = JsonNode.Parse(GoldenResolver.Resolve(golden, layers))!.AsObject();

        Assert.Equal(0, (int)eff["base"]!);                 // golden field, untouched
        Assert.Equal(1, (int)eff["g"]!);                    // template group layer
        Assert.Equal(2, (int)eff["t"]!);                    // template tenant layer
        Assert.Equal(3, (int)eff["i"]!);                    // inline tenant layer
        Assert.Equal("fromInline", (string)eff["shared"]!); // inline > template > golden
    }

    // With no template and no inline overrides, LayersFor yields all-null layers, so the
    // effective golden is the baseline verbatim (the common "just broadcast the golden" path).
    [Fact]
    public void LayersFor_NoTemplateNoInline_YieldsGoldenVerbatim()
    {
        var layers = GoldenResolver.LayersFor(null, null, "grp-1", "ten-1");
        Assert.All(layers, Assert.Null);
        var golden = """{"a":1,"b":2}""";
        var eff = JsonNode.Parse(GoldenResolver.Resolve(golden, layers))!.AsObject();
        Assert.Equal(1, (int)eff["a"]!);
        Assert.Equal(2, (int)eff["b"]!);
    }

    private static Dictionary<string, JsonElement> Dict(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
}
