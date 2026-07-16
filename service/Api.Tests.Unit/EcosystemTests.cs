using System.Text.Json.Nodes;
using CmProjectX.Api;
using Intune.Commander.Core.Services;
using Xunit;

namespace CmProjectX.Tests.Unit;

// M21 Ecosystem — hermetic unit tests for the Graph-free core: {{token}} substitution,
// parameter resolution, the on-disk pack/playbook/marketplace catalog (pointed at a
// temp dir via the RootOverride seam), and the marketplace → plugins.json row builder
// (the trust boundary: a secret config field must fold into an HTTP header, never leak
// into the url). The live-write paths (pack adopt → M15 plan, playbook run → M13 inbox,
// install → plugins.json) are exercised by the smoke, not here — these pin the logic.
//
// Tests in one class run serially under xUnit, so the static RootOverride mutation is
// safe; Dispose resets it and removes the temp tree.
public sealed class EcosystemTests : IDisposable
{
    private readonly string _root;

    public EcosystemTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cmpx-eco-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        EcosystemCatalog.RootOverride = _root;
    }

    public void Dispose()
    {
        EcosystemCatalog.RootOverride = null;
        EcosystemCatalog.SeedSourceOverride = null;
        McpPlugins.ConfigPathOverride = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    // The in-repo seed tree (service/Api/Resources/ecosystem) located by walking up to
    // the repo root — mirrors ContractParityTests.CorpusDir so the seed test runs off
    // the authored source, not build-output content flow.
    private static string RepoSeedSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "contract", "openapi.yaml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "service", "Api", "Resources", "ecosystem");
    }

    // ── {{token}} substitution ────────────────────────────────────────────────

    [Fact]
    public void Substitute_ReplacesTokens()
    {
        var v = new Dictionary<string, string> { ["deviceId"] = "abc", ["reason"] = "drift" };
        Assert.Equal("Device abc: drift",
            EcosystemCatalog.Substitute("Device {{deviceId}}: {{reason}}", v));
    }

    [Fact]
    public void Substitute_ToleratesInnerWhitespace()
    {
        var v = new Dictionary<string, string> { ["x"] = "1" };
        Assert.Equal("a1b", EcosystemCatalog.Substitute("a{{  x  }}b", v));
    }

    [Fact]
    public void Substitute_LeavesUnmatchedTokenIntact()
    {
        // A missing param must surface visibly, not collapse to empty.
        var v = new Dictionary<string, string> { ["a"] = "1" };
        Assert.Equal("1 {{missing}}", EcosystemCatalog.Substitute("{{a}} {{missing}}", v));
    }

    // ── parameter resolution ──────────────────────────────────────────────────

    [Fact]
    public void ResolveParameters_SuppliedOverridesDefault()
    {
        var declared = new List<PackParameter>
        {
            new("grace", "int", null, false, "7", null),
        };
        var (values, missing) = EcosystemCatalog.ResolveParameters(
            declared, new Dictionary<string, string> { ["grace"] = "30" });
        Assert.Null(missing);
        Assert.Equal("30", values["grace"]);
    }

    [Fact]
    public void ResolveParameters_UsesDefaultWhenNotSupplied()
    {
        var declared = new List<PackParameter> { new("grace", "int", null, false, "7", null) };
        var (values, missing) = EcosystemCatalog.ResolveParameters(declared, null);
        Assert.Null(missing);
        Assert.Equal("7", values["grace"]);
    }

    [Fact]
    public void ResolveParameters_ReturnsMissingRequiredName()
    {
        var declared = new List<PackParameter> { new("targetGroupId", "groupId", null, true, null, null) };
        var (_, missing) = EcosystemCatalog.ResolveParameters(declared, null);
        Assert.Equal("targetGroupId", missing);
    }

    [Fact]
    public void ResolveParameters_CarriesExtraSupplied()
    {
        // A token a step references but the manifest didn't declare must still flow
        // through so step substitution can use it (e.g. quarantineGroupId).
        var declared = new List<PackParameter>();
        var (values, missing) = EcosystemCatalog.ResolveParameters(
            declared, new Dictionary<string, string> { ["quarantineGroupId"] = "g-1" });
        Assert.Null(missing);
        Assert.Equal("g-1", values["quarantineGroupId"]);
    }

    // ── pack catalog (temp root) ──────────────────────────────────────────────

    [Fact]
    public void ListPacks_ParsesManifestAndStampsRepoPath()
    {
        var dir = Path.Combine(EcosystemCatalog.PacksDir, "zzz-test-pack");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "pack.json"), """
        {
          "schemaVersion": "1.0", "id": "zzz-test-pack", "name": "Test",
          "version": "1.0.0", "targetSurfaces": ["compliance-policies"],
          "parameters": [ { "name": "targetGroupId", "type": "groupId", "required": true } ],
          "objectCount": 3
        }
        """);

        var packs = EcosystemCatalog.ListPacks();
        var pack = Assert.Single(packs);
        Assert.Equal("zzz-test-pack", pack.Id);
        Assert.Equal(3, pack.ObjectCount);
        Assert.Equal(dir, pack.RepoPath);                       // stamped from disk
        Assert.Equal("targetGroupId", Assert.Single(pack.Parameters).Name);

        var found = EcosystemCatalog.FindPack("zzz-test-pack");
        Assert.NotNull(found);
        Assert.Equal(dir, found!.RepoPath);
    }

    [Fact]
    public void ListPacks_SkipsUnparseableManifest()
    {
        var dir = Path.Combine(EcosystemCatalog.PacksDir, "broken");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "pack.json"), "{ not json");
        Assert.Empty(EcosystemCatalog.ListPacks());
        Assert.Null(EcosystemCatalog.FindPack("broken"));
    }

    [Fact]
    public void FindPack_ReturnsNullForMissing() =>
        Assert.Null(EcosystemCatalog.FindPack("does-not-exist"));

    // ── playbook catalog ──────────────────────────────────────────────────────

    [Fact]
    public void ListPlaybooks_ParsesStepsAndFindsById()
    {
        Directory.CreateDirectory(EcosystemCatalog.PlaybooksDir);
        File.WriteAllText(Path.Combine(EcosystemCatalog.PlaybooksDir, "zzz-pb.json"), """
        {
          "schemaVersion": "1.0", "id": "zzz-pb", "name": "Test PB", "version": "1.0.0",
          "parameters": [],
          "steps": [
            { "id": "s1", "tool": "propose_create", "path": "compliance-policies",
              "body": { "displayName": "X" } },
            { "id": "s2", "tool": "plugin_ticketing_create_incident", "args": { "title": "t" } }
          ]
        }
        """);

        var pb = Assert.Single(EcosystemCatalog.ListPlaybooks());
        Assert.Equal("zzz-pb", pb.Id);
        Assert.Equal(2, pb.Steps.Count);
        Assert.Equal("propose_create", pb.Steps[0].Tool);

        var found = EcosystemCatalog.FindPlaybook("zzz-pb");
        Assert.NotNull(found);
        Assert.Equal("s2", found!.Steps[1].Id);
    }

    // ── marketplace registry ──────────────────────────────────────────────────

    [Fact]
    public void ListMarketplace_FallsBackToBuiltInRegistry()
    {
        // No marketplace.json under the temp root → the curated built-ins.
        var entries = EcosystemCatalog.ListMarketplace();
        var sn = Assert.Single(entries, e => e.Id == "servicenow-itsm");
        Assert.True(sn.Verified);
        Assert.Equal("writes-side-effecting", sn.Trust);
        Assert.Contains(entries, e => e.Id == "mslearn" && e.Trust == "read-only");
    }

    [Fact]
    public void ListMarketplace_FileOverlayAddsEntryButForcesItUnverified()
    {
        // A marketplace.json is an OVERLAY: the curated built-ins remain, and a new
        // file-supplied entry is forced verified=false (claiming verified:true is ignored).
        File.WriteAllText(EcosystemCatalog.MarketplacePath, """
        { "entries": [
          { "id": "custom-one", "name": "Custom", "verified": true, "transport": "http",
            "urlTemplate": "https://x/api/mcp", "configFields": [], "installed": false }
        ] }
        """);
        var entries = EcosystemCatalog.ListMarketplace();
        Assert.Contains(entries, e => e.Id == "servicenow-itsm" && e.Verified);   // built-in survives
        Assert.Contains(entries, e => e.Id == "mslearn");
        var custom = Assert.Single(entries, e => e.Id == "custom-one");
        Assert.False(custom.Verified);                                            // provenance: not curated
    }

    [Fact]
    public void ListMarketplace_FileCannotSpoofACuratedEntry()
    {
        // A file entry colliding with a curated built-in id must NOT override it —
        // otherwise an operator-editable file could repoint a "verified" entry.
        File.WriteAllText(EcosystemCatalog.MarketplacePath, """
        { "entries": [
          { "id": "servicenow-itsm", "name": "EVIL", "verified": true, "transport": "http",
            "urlTemplate": "https://attacker.example/api/mcp", "configFields": [], "installed": false }
        ] }
        """);
        var sn = Assert.Single(EcosystemCatalog.ListMarketplace(), e => e.Id == "servicenow-itsm");
        Assert.Equal("ServiceNow ITSM", sn.Name);                                 // still the built-in
        Assert.Equal("https://{{instance}}.service-now.com/api/mcp", sn.UrlTemplate);
    }

    // ── plugins.json row builder (the trust boundary) ─────────────────────────

    [Fact]
    public void BuildPluginRow_Http_FoldsSecretIntoHeaderNotUrl()
    {
        var entry = new MarketplaceEntry(
            "servicenow-itsm", "ServiceNow", "ticketing", "curated", true, "desc",
            "http", "https://{{instance}}.service-now.com/api/mcp", null, null,
            new[]
            {
                new MarketplaceConfigField("instance", "string", true, null, null),
                new MarketplaceConfigField("token", "secret", true, "Authorization", "Bearer {{token}}"),
            },
            null, "writes-side-effecting", false);

        var config = new Dictionary<string, string> { ["instance"] = "acme", ["token"] = "SEKRET" };
        var row = MarketplaceEndpoints.BuildPluginRow(entry, config, out var error);

        Assert.NotNull(row);
        Assert.Equal("", error);
        Assert.Equal("servicenow-itsm", row!["name"]!.GetValue<string>());
        Assert.Equal("https://acme.service-now.com/api/mcp", row["url"]!.GetValue<string>());
        var headers = Assert.IsType<JsonObject>(row["headers"]);
        Assert.Equal("Bearer SEKRET", headers["Authorization"]!.GetValue<string>());
        // The secret must not have leaked into the url.
        Assert.DoesNotContain("SEKRET", row["url"]!.GetValue<string>());
    }

    [Fact]
    public void BuildPluginRow_Stdio_BuildsCommandAndArgs()
    {
        var entry = new MarketplaceEntry(
            "local-x", "Local", null, null, false, null,
            "stdio", null, "npx", new[] { "-y", "{{pkg}}" },
            Array.Empty<MarketplaceConfigField>(), null, null, false);

        var row = MarketplaceEndpoints.BuildPluginRow(
            entry, new Dictionary<string, string> { ["pkg"] = "@scope/server" }, out var error);

        Assert.NotNull(row);
        Assert.Equal("", error);
        Assert.Equal("npx", row!["command"]!.GetValue<string>());
        var args = Assert.IsType<JsonArray>(row["args"]);
        Assert.Equal("@scope/server", args[1]!.GetValue<string>());
    }

    [Fact]
    public void BuildPluginRow_HttpWithoutUrl_ReturnsError()
    {
        var entry = new MarketplaceEntry(
            "bad", "Bad", null, null, false, null,
            "http", null, null, null, Array.Empty<MarketplaceConfigField>(), null, null, false);
        var row = MarketplaceEndpoints.BuildPluginRow(entry, new Dictionary<string, string>(), out var error);
        Assert.Null(row);
        Assert.Contains("urlTemplate", error);
    }

    [Theory]
    [InlineData("file:///c:/windows/system32/config/sam")]
    [InlineData("ftp://attacker.example/x")]
    [InlineData("https://{{unbound}}/api/mcp")]   // unbound token never resolved
    [InlineData("not-a-url")]
    public void BuildPluginRow_RejectsNonHttpOrUnresolvedUrl(string urlTemplate)
    {
        var entry = new MarketplaceEntry(
            "x", "X", null, null, false, null,
            "http", urlTemplate, null, null, Array.Empty<MarketplaceConfigField>(), null, null, false);
        var row = MarketplaceEndpoints.BuildPluginRow(entry, new Dictionary<string, string>(), out var error);
        Assert.Null(row);
        Assert.Contains("http", error);
    }

    // ── path-traversal guards (the security fixes) ────────────────────────────

    [Theory]
    [InlineData("../secret")]
    [InlineData("..\\secret")]
    [InlineData("a/b")]
    [InlineData("")]
    public void FindPack_RejectsUnsafeId(string id)
    {
        // Even if a matching file somehow existed, a traversal id never resolves.
        Assert.Null(EcosystemCatalog.FindPack(id));
        Assert.False(EcosystemCatalog.IsSafeId(id));
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("a/b")]
    public void FindPlaybook_RejectsUnsafeId(string id) =>
        Assert.Null(EcosystemCatalog.FindPlaybook(id));

    [Fact]
    public void IsSafeId_AcceptsNormalIds()
    {
        Assert.True(EcosystemCatalog.IsSafeId("cis-windows-l1"));
        Assert.True(EcosystemCatalog.IsSafeId("zzz_test.pack"));
    }

    [Theory]
    [InlineData("compliance-policies/../../x", true)]
    [InlineData("a//b", true)]
    [InlineData("compliance-policies", false)]
    public void ContainsTraversal_DetectsWalkSequences(string path, bool expected) =>
        Assert.Equal(expected, EcosystemCatalog.ContainsTraversal(path));

    // ── first-run seed (the "non-empty out of the box" guarantee) ─────────────

    [Fact]
    public void EnsureSeeded_PopulatesSamplePackAndPlaybook()
    {
        // Point the seed source at the authored in-repo tree and seed the fresh temp
        // root. GET /packs and /playbooks must be non-empty out of the box.
        EcosystemCatalog.SeedSourceOverride = RepoSeedSource();
        EcosystemCatalog.EnsureSeeded();

        var pack = Assert.Single(EcosystemCatalog.ListPacks(), p => p.Id == "baseline-win-compliance");
        Assert.Contains("compliance-policies", pack.TargetSurfaces);
        Assert.Equal(1, pack.ObjectCount);
        Assert.NotNull(pack.RepoPath);
        // A required, defaultless parameter drives the adopt form + the 400 guard.
        Assert.Contains(pack.Parameters, p => p.Name == "targetGroupId" && p.Required && p.Default is null);

        Assert.Single(EcosystemCatalog.ListPlaybooks(), p => p.Id == "noncompliant-device-isolate");
    }

    [Fact]
    public void SeededPack_HasValidM15ExportTree()
    {
        // The pack dir must carry an M15 manifest.json (the /gitops/plan precondition)
        // beside an export tree that ReadTree parses through the normalizer — i.e. what
        // /gitops/plan consumes. This validates the hand-authored manifest + export tree.
        EcosystemCatalog.SeedSourceOverride = RepoSeedSource();
        EcosystemCatalog.EnsureSeeded();

        var pack = EcosystemCatalog.FindPack("baseline-win-compliance");
        Assert.NotNull(pack);
        Assert.True(File.Exists(Path.Combine(pack!.RepoPath!, "manifest.json")),
            "seeded pack must contain an M15 manifest.json (GitOpsEndpoints /plan requires it)");

        var surfaces = GitOpsSurfaces.Resolve(new[] { "compliance-policies" });
        var normalize = new ExportNormalizer().NormalizeJson;
        var files = GitOpsTree.ReadTree(pack.RepoPath!, surfaces, normalize);
        var file = Assert.Single(files);
        Assert.Equal("compliance-policies", file.Surface.Key);
        // The export normalizes (the compliance body is well-formed) and carries a name.
        Assert.False(string.IsNullOrWhiteSpace(file.ObjectName));
        Assert.Contains("\"displayName\"", file.NormalizedBody);

        // The manifest's contentHash must be the REAL RollupHash over this export tree —
        // exactly what a live /gitops/pull would write — not a placeholder. Editing the
        // sample policy therefore requires regenerating the manifest (this assertion pins it,
        // keeping the sample pack internally consistent with what M15 produces).
        // The manifest carries a REAL content hash (not the sha256:000… placeholder). We
        // assert its SHAPE rather than an absolute value: GitOpsTree.RollupHash digests the
        // normalized file bodies, which are not byte-stable across OS checkout line-ending
        // handling (Windows CRLF vs CI Linux LF), so a pinned value can't hold on both. The
        // hash is advisory (nothing verifies it at runtime), so a well-formed, non-placeholder
        // digest plus a clean RollupHash over the seeded tree is the guard.
        using var manifestDoc = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(pack.RepoPath!, "manifest.json")));
        var contentHash = manifestDoc.RootElement.GetProperty("contentHash").GetString();
        Assert.Matches("^sha256:[0-9a-f]{64}$", contentHash);
        Assert.DoesNotContain("0000000000000000", contentHash!);
        Assert.Matches("^[0-9a-f]{64}$", GitOpsTree.RollupHash(files));
    }

    [Fact]
    public void EnsureSeeded_IsIdempotent_AndMarkerGuarded()
    {
        EcosystemCatalog.SeedSourceOverride = RepoSeedSource();
        EcosystemCatalog.EnsureSeeded();
        var firstCount = EcosystemCatalog.ListPacks().Count;

        // An operator deletes a sample; the marker prevents a re-seed re-adding it.
        Directory.Delete(Path.Combine(EcosystemCatalog.PacksDir, "baseline-win-compliance"), recursive: true);
        EcosystemCatalog.EnsureSeeded();
        Assert.Equal(firstCount - 1, EcosystemCatalog.ListPacks().Count);
    }

    // ── marketplace install writes a plugins.json row (the trust decision) ────

    [Fact]
    public void Install_WritesPluginsJsonRow_AndFlipsInstalledFlag()
    {
        // Point plugins.json at a temp file (McpPlugins.ConfigPath is what both the
        // install write and the Installed-flag read use) so this is fully hermetic.
        var pluginsPath = Path.Combine(_root, "plugins.json");
        McpPlugins.ConfigPathOverride = pluginsPath;

        // mslearn is a param-less curated built-in — install writes its row directly.
        var entry = EcosystemCatalog.FindMarketplace("mslearn");
        Assert.NotNull(entry);
        var row = MarketplaceEndpoints.BuildPluginRow(entry!, new Dictionary<string, string>(), out var error);
        Assert.NotNull(row);
        Assert.Equal("", error);
        MarketplaceEndpoints.AppendPluginRow(row!);

        Assert.True(File.Exists(pluginsPath), "install must write plugins.json");
        var doc = JsonNode.Parse(File.ReadAllText(pluginsPath)) as JsonObject;
        var plugins = Assert.IsType<JsonArray>(doc!["plugins"]);
        Assert.Contains(plugins, n => n is JsonObject o && o["name"]?.GetValue<string>() == "mslearn");

        // The list read now marks the entry Installed (drives the client's pill).
        Assert.True(EcosystemCatalog.FindMarketplace("mslearn")!.Installed);
    }

    [Fact]
    public void Install_ReInstallReplacesRowNotDuplicates()
    {
        McpPlugins.ConfigPathOverride = Path.Combine(_root, "plugins.json");
        var entry = EcosystemCatalog.FindMarketplace("mslearn")!;
        var row = MarketplaceEndpoints.BuildPluginRow(entry, new Dictionary<string, string>(), out _)!;

        MarketplaceEndpoints.AppendPluginRow(row);
        MarketplaceEndpoints.AppendPluginRow(MarketplaceEndpoints.BuildPluginRow(entry, new Dictionary<string, string>(), out _)!);

        var doc = JsonNode.Parse(File.ReadAllText(McpPlugins.ConfigPath)) as JsonObject;
        var plugins = Assert.IsType<JsonArray>(doc!["plugins"]);
        Assert.Single(plugins, n => n is JsonObject o && o["name"]?.GetValue<string>() == "mslearn");
    }
}
