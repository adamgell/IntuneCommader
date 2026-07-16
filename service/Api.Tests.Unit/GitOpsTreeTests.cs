using CmProjectX.Api;
using Intune.Commander.Core.Services;
using System.Text.Json;
using Xunit;

namespace CmProjectX.Tests.Unit;

// Tier-1 hermetic tests for the M15 GitOps tree differ — the pure (Graph-free)
// core of pull/plan. Exercises ReadTree + Diff over real temp directories using the
// production ExportNormalizer, so it pins the "zero-diff on fresh pull" guarantee
// (a normalizer regression that leaks a volatile field would fail here) plus the
// Added / Removed / Modified verdicts and the content-hash roll-up.
public class GitOpsTreeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cmpx-gitops-test-" + Guid.NewGuid().ToString("n"));
    private static readonly Func<string, string> Normalize = new ExportNormalizer().NormalizeJson;
    private static readonly GitOpsSurfaceDef DeviceConfigs = GitOpsSurfaces.Find("device-configs")!;
    private static readonly IReadOnlyList<GitOpsSurfaceDef> Surfaces = new[] { DeviceConfigs };

    private string Tree(string name)
    {
        var dir = Path.Combine(_root, name, DeviceConfigs.Folder);
        Directory.CreateDirectory(dir);
        return Path.Combine(_root, name);
    }

    private static void Write(string treeRoot, string fileName, string json) =>
        File.WriteAllText(Path.Combine(treeRoot, DeviceConfigs.Folder, fileName), json);

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void IdenticalTrees_ProduceZeroDiff()
    {
        // A field-reordered, volatile-field-bearing copy must still diff to zero
        // after normalization — that is the "fresh pull plans to noop" guarantee.
        var repo = Tree("repo");
        var live = Tree("live");
        Write(repo, "a.json", """{ "displayName": "Policy A", "platform": "windows10AndLater", "value": 1 }""");
        Write(live, "a.json", """{ "id": "abc-123", "value": 1, "platform": "windows10AndLater", "displayName": "Policy A", "version": 7 }""");

        var (rows, summary, _) = GitOpsTree.Diff(
            GitOpsTree.ReadTree(repo, Surfaces, Normalize),
            GitOpsTree.ReadTree(live, Surfaces, Normalize));

        Assert.Empty(rows);
        Assert.Equal(new GitOpsPlanSummary(0, 0, 0, 1), summary);
    }

    [Fact]
    public void RepoOnly_IsAdded_LiveOnly_IsRemoved()
    {
        var repo = Tree("repo");
        var live = Tree("live");
        Write(repo, "new.json", """{ "displayName": "Brand New", "value": 1 }""");
        Write(live, "gone.json", """{ "displayName": "Legacy", "value": 2 }""");

        var (rows, summary, liveHashes) = GitOpsTree.Diff(
            GitOpsTree.ReadTree(repo, Surfaces, Normalize),
            GitOpsTree.ReadTree(live, Surfaces, Normalize));

        Assert.Equal(1, summary.Add);
        Assert.Equal(1, summary.Destroy);
        Assert.Equal(0, summary.Change);
        Assert.Contains(rows, r => r.Verdict == "Added" && r.ObjectName == "Brand New");
        Assert.Contains(rows, r => r.Verdict == "Removed" && r.ObjectName == "Legacy");
        // Only the live object carries a pinned hash (repo-only Added has none).
        Assert.Single(liveHashes);
    }

    [Fact]
    public void ChangedField_IsModified_WithBeforeAfter()
    {
        var repo = Tree("repo");
        var live = Tree("live");
        Write(repo, "p.json", """{ "displayName": "P", "firewallEnabled": "allowed" }""");
        Write(live, "p.json", """{ "displayName": "P", "firewallEnabled": "notConfigured" }""");

        var (rows, summary, _) = GitOpsTree.Diff(
            GitOpsTree.ReadTree(repo, Surfaces, Normalize),
            GitOpsTree.ReadTree(live, Surfaces, Normalize));

        Assert.Equal(1, summary.Change);
        var row = Assert.Single(rows);
        Assert.Equal("Modified", row.Verdict);
        var change = Assert.Single(row.Changes);
        Assert.Equal("/firewallEnabled", change.Path);
        Assert.Equal("Modified", change.Kind);
        // before = live, after = repo (reads as "what apply does to the tenant").
        Assert.Equal("notConfigured", change.Before?.ToString());
        Assert.Equal("allowed", change.After?.ToString());
    }

    [Fact]
    public void RollupHash_ChangesWithContent_StableOtherwise()
    {
        var a = Tree("a");
        var b = Tree("b");
        Write(a, "x.json", """{ "displayName": "X", "v": 1 }""");
        Write(b, "x.json", """{ "displayName": "X", "v": 2 }""");

        var ha = GitOpsTree.RollupHash(GitOpsTree.ReadTree(a, Surfaces, Normalize));
        var hb = GitOpsTree.RollupHash(GitOpsTree.ReadTree(b, Surfaces, Normalize));
        var ha2 = GitOpsTree.RollupHash(GitOpsTree.ReadTree(a, Surfaces, Normalize));

        Assert.Equal(ha, ha2);
        Assert.NotEqual(ha, hb);
    }

    [Fact]
    public void ExtractName_HandlesExportWrappers()
    {
        Assert.Equal("Bare", GitOpsTree.ExtractName("""{ "displayName": "Bare" }""", "fallback"));
        Assert.Equal("Wrapped", GitOpsTree.ExtractName("""{ "policy": { "name": "Wrapped" } }""", "fallback"));
        Assert.Equal("fallback", GitOpsTree.ExtractName("""{ "nope": 1 }""", "fallback"));
    }

    [Fact]
    public void BuildMinimalPatch_SendsOnlyChangedFields_PlusRemappedDiscriminator()
    {
        // The unchanged supportsScopeTags MUST be omitted — sending it back (null on
        // read, non-nullable on write) is exactly what Graph rejected on a full PATCH.
        var live = """{ "odataType": "#microsoft.graph.windows10GeneralConfiguration", "displayName": "P", "description": "old", "supportsScopeTags": false }""";
        var repo = """{ "odataType": "#microsoft.graph.windows10GeneralConfiguration", "displayName": "P", "description": "new", "supportsScopeTags": false }""";

        var patch = GitOpsTree.BuildMinimalPatch(repo, live);
        Assert.NotNull(patch);
        var node = System.Text.Json.Nodes.JsonNode.Parse(patch!)!.AsObject();
        Assert.Equal("#microsoft.graph.windows10GeneralConfiguration", (string?)node["@odata.type"]); // remapped from odataType for Kiota
        Assert.Equal("new", (string?)node["description"]);
        Assert.False(node.ContainsKey("supportsScopeTags"));
        Assert.False(node.ContainsKey("displayName"));
        Assert.False(node.ContainsKey("odataType"));
    }

    [Fact]
    public void BuildMinimalPatch_NoChanges_ReturnsNull()
    {
        var body = """{ "odataType": "#x", "description": "same" }""";
        Assert.Null(GitOpsTree.BuildMinimalPatch(body, body));
    }

    [Fact]
    public void StoredPlan_RoundTripsTenantId()
    {
        var plan = new GitOpsStoredPlan(
            "p1",
            "C:\\repo",
            "tenant-a",
            new[] { "device-configs" },
            new Dictionary<string, string> { ["device-configs|p.json"] = "hash" },
            new Dictionary<string, GitOpsStoredObject>
            {
                ["device-configs|p.json"] = new("device-configs", "p.json", "Policy", "Modified"),
            });

        var json = JsonSerializer.Serialize(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var copy = JsonSerializer.Deserialize<GitOpsStoredPlan>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(copy);
        Assert.Equal("tenant-a", copy!.TenantId);
    }
}
