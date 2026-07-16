using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace CmProjectX.Api.Tests;

// M21 Ecosystem — live, endpoint-level coverage through the sidecar for the packs /
// playbooks / marketplace surfaces. These are BLACK-BOX (need the running sidecar): the
// hermetic logic (catalog parse, {{token}} substitution, plugins.json row build, the
// first-run seed, the required-param/configField guards) is pinned in
// Api.Tests.Unit/EcosystemTests. This asserts the wiring end-to-end:
//   • the seed makes GET /packs|/playbooks non-empty out of the box,
//   • GET /marketplace returns the curated built-ins,
//   • POST /playbooks/{id}/run enqueues a PendingChange (native propose_* → M13 inbox),
//   • the required-parameter and required-configField 400 paths.
//
// Reads + the playbook run don't need Graph (the enqueue path is sign-in-agnostic); the
// pack-adopt 400 needs sign-in (adopt 409s when signed out, before the param check), so
// that one is gated. Nothing here mutates Graph. The run's PendingChange is rejected in
// cleanup so the inbox isn't polluted.
[Collection("sidecar")]
public sealed class EcosystemEndpointsTests
{
    private const string SamplePackId = "baseline-win-compliance";
    private const string SamplePlaybookId = "noncompliant-device-isolate";

    private readonly SidecarFixture _fx;
    private readonly ITestOutputHelper _out;

    public EcosystemEndpointsTests(SidecarFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    // ── GET shapes — the seed makes these non-empty out of the box ────────────

    [Fact]
    public async Task GetPacks_ContainsSeededSamplePack()
    {
        var packs = await GetArrayAsync("/packs");
        Assert.Contains(packs, p => p.GetProperty("id").GetString() == SamplePackId);
        var pack = packs.First(p => p.GetProperty("id").GetString() == SamplePackId);
        Assert.Equal("compliance-policies", pack.GetProperty("targetSurfaces")[0].GetString());
        Assert.True(pack.GetProperty("parameters").GetArrayLength() > 0);
    }

    [Fact]
    public async Task GetPlaybooks_ContainsSeededSamplePlaybook()
    {
        var playbooks = await GetArrayAsync("/playbooks");
        var pb = Assert.Single(playbooks, p => p.GetProperty("id").GetString() == SamplePlaybookId);
        Assert.True(pb.GetProperty("steps").GetArrayLength() >= 2);
    }

    [Fact]
    public async Task GetMarketplace_ReturnsCuratedBuiltIns()
    {
        var entries = await GetArrayAsync("/marketplace");
        Assert.Contains(entries, e => e.GetProperty("id").GetString() == "mslearn");
        var sn = Assert.Single(entries, e => e.GetProperty("id").GetString() == "servicenow-itsm");
        Assert.True(sn.GetProperty("verified").GetBoolean());
    }

    // ── POST /playbooks/{id}/run — native step enqueues a PendingChange ───────

    [Fact]
    public async Task PlaybookRun_EnqueuesPendingChange()
    {
        var body = new
        {
            parameters = new Dictionary<string, string>
            {
                ["deviceId"] = "test-device-00000000-0000-0000-0000-000000000000",
                ["reason"] = "integration test",
            },
        };
        using var resp = await _fx.Http.PostAsJsonAsync($"/playbooks/{SamplePlaybookId}/run", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal($"playbook:{SamplePlaybookId}", result.GetProperty("proposer").GetString());
        // The plugin step is a passthrough; the propose_update step enqueues.
        var steps = result.GetProperty("steps");
        Assert.Contains(steps.EnumerateArray(), s => s.GetProperty("outcome").GetString() == "passthrough");
        var ids = result.GetProperty("pendingChangeIds");
        Assert.True(ids.GetArrayLength() > 0, "the native propose_update step must enqueue a PendingChange");
        _out.WriteLine($"playbook run enqueued {ids.GetArrayLength()} change(s)");

        // Cleanup — reject the changes this test created so the inbox stays clean.
        foreach (var id in ids.EnumerateArray())
        {
            var cid = id.GetString();
            if (string.IsNullOrEmpty(cid) || cid == "(unknown)") continue;
            try { await _fx.Http.PostAsJsonAsync($"/pending-changes/{cid}/reject", new { reason = "test cleanup" }); }
            catch { /* best-effort cleanup */ }
        }
    }

    // ── 400 paths — required parameter / required configField ─────────────────

    [Fact]
    public async Task PlaybookRun_MissingRequiredParam_Returns400()
    {
        // No deviceId → the required-parameter guard fires (no Graph needed).
        using var resp = await _fx.Http.PostAsJsonAsync(
            $"/playbooks/{SamplePlaybookId}/run",
            new { parameters = new Dictionary<string, string>() });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("deviceId", err.GetProperty("error").GetString());
    }

    [Fact]
    public async Task MarketplaceInstall_MissingRequiredConfigField_Returns400()
    {
        // servicenow-itsm requires 'instance' + 'token'; empty config → 400 (no Graph,
        // and NON-destructive: the guard rejects before any plugins.json write).
        using var resp = await _fx.Http.PostAsJsonAsync(
            "/marketplace/servicenow-itsm/install",
            new { config = new Dictionary<string, string>() });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("configField", err.GetProperty("error").GetString());
    }

    [Fact]
    public async Task PackAdopt_MissingRequiredParam_Returns400()
    {
        // adopt 409s when signed out (the live-tenant guard precedes the param check),
        // so the 400 path is only reachable signed in.
        Assert.True(_fx.SignedIn, $"Sidecar is not signed in — cannot validate. {_fx.SignInError}");

        using var resp = await _fx.Http.PostAsJsonAsync(
            $"/packs/{SamplePackId}/adopt",
            new { parameters = new Dictionary<string, string>(), confirm = false });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("targetGroupId", err.GetProperty("error").GetString());
    }

    // ── helper ────────────────────────────────────────────────────────────────

    private async Task<List<JsonElement>> GetArrayAsync(string path)
    {
        using var resp = await _fx.Http.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var arr = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, arr.ValueKind);
        return arr.EnumerateArray().ToList();
    }
}
