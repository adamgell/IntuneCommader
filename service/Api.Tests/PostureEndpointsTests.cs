using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace CmProjectX.Api.Tests;

// M19 Continuous Posture — black-box E2E coverage for the four /posture/* routes against
// the live sidecar (same convention as SimulateEndpointTests: reuses the running sidecar,
// asserts on _fx.SignedIn). All reads are NON-MUTATING — /posture/score self-snapshots a
// synthetic object into the local time-machine but never writes to the tenant, and the
// evidence pack only reads. Safe against any tenant incl. the Ivy24 sandbox.
//
// The pure logic (coverage math, POA&M closed-item diff, trend delta) is unit-tested
// hermetically in Api.Tests.Unit/PostureTests; this asserts the same behaviour end-to-end.
[Collection("sidecar")]
public sealed class PostureEndpointsTests
{
    private readonly SidecarFixture _fx;
    private readonly ITestOutputHelper _out;

    public PostureEndpointsTests(SidecarFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    // The three auth-gated routes 409 when signed out, mirroring /security-posture/summary.
    // On a signed-in sidecar they must NOT 409 (they compute a score); on a signed-out one
    // they must be exactly 409. Either way, never a 500 — the auth gate fires before Graph.
    [Theory]
    [InlineData("/posture/score")]
    [InlineData("/posture/poam")]
    public async Task AuthGatedGet_Is409WhenSignedOut(string path)
    {
        using var resp = await _fx.Http.GetAsync(path);
        if (_fx.SignedIn)
            Assert.NotEqual(HttpStatusCode.Conflict, resp.StatusCode);
        else
            Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, resp.StatusCode);
    }

    [Fact]
    public async Task EvidencePack_Is409WhenSignedOut()
    {
        using var resp = await _fx.Http.PostAsJsonAsync("/posture/evidence-pack",
            new { asOf = "2026-01-01T00:00:00Z", benchmark = "cis" });
        if (_fx.SignedIn)
            Assert.NotEqual(HttpStatusCode.Conflict, resp.StatusCode);
        else
            Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, resp.StatusCode);
    }

    // /posture/trend reads only the local store (no auth), so it is 200 even signed out.
    [Fact]
    public async Task Trend_NoAuthRequired_Returns200()
    {
        using var resp = await _fx.Http.GetAsync("/posture/trend?benchmark=cis");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // /posture/score self-snapshots into the time-machine, deduped by content hash: a
    // SECOND identical score (tenant unchanged) must NOT add a new trend point.
    [Fact]
    public async Task Score_SelfSnapshots_RowGrowsOnlyOnRealChange()
    {
        Assert.True(_fx.SignedIn, $"Sidecar is not signed in — cannot validate. {_fx.SignInError}");

        await ScoreAsync("cis");                 // warm-up: lands the current score (if changed)
        var before = await TrendPointCountAsync("cis");
        await ScoreAsync("cis");                 // identical body → dedup-by-hash → no new row
        var after = await TrendPointCountAsync("cis");

        Assert.Equal(before, after);
        _out.WriteLine($"trend points stable across an identical re-score: {after}");
    }

    // Trend points come back oldest→newest with a well-formed delta (scoreChange +
    // regressions + the M18 remediationCandidates feed).
    [Fact]
    public async Task Trend_ReturnsOrderedPointsWithDelta()
    {
        using var resp = await _fx.Http.GetAsync("/posture/trend?benchmark=cis");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        var points = root.GetProperty("points");
        Assert.Equal(JsonValueKind.Array, points.ValueKind);

        DateTimeOffset? prev = null;
        foreach (var p in points.EnumerateArray())
        {
            var captured = DateTimeOffset.Parse(p.GetProperty("capturedUtc").GetString()!);
            if (prev is not null)
                Assert.True(captured >= prev, "trend points must be ordered oldest → newest");
            prev = captured;
            Assert.Equal(JsonValueKind.Object, p.GetProperty("categoryScores").ValueKind);
        }

        var delta = root.GetProperty("delta");
        Assert.Equal(JsonValueKind.Number, delta.GetProperty("scoreChange").ValueKind);
        Assert.Equal(JsonValueKind.Array, delta.GetProperty("regressions").ValueKind);
        // M18 feed — a parallel structured candidate list, one per regressed category.
        var candidates = delta.GetProperty("remediationCandidates");
        Assert.Equal(JsonValueKind.Array, candidates.ValueKind);
        Assert.Equal(delta.GetProperty("regressions").GetArrayLength(), candidates.GetArrayLength());
        foreach (var c in candidates.EnumerateArray())
            Assert.False(string.IsNullOrWhiteSpace(c.GetProperty("category").GetString()));
    }

    // Reproducibility: the SAME asOf produces byte-identical deterministic artifacts, so
    // the manifestHash matches across two independent generations (the DoD guarantee).
    [Fact]
    public async Task EvidencePack_SameAsOf_ProducesIdenticalManifestHash()
    {
        Assert.True(_fx.SignedIn, $"Sidecar is not signed in — cannot validate. {_fx.SignInError}");

        var req = new { asOf = "2026-01-01T00:00:00Z", benchmark = "cis" };
        var first = await EvidencePackAsync(req);
        var second = await EvidencePackAsync(req);

        Assert.Equal(first.GetProperty("manifestHash").GetString(),
                     second.GetProperty("manifestHash").GetString());
        // And the manifest names the REAL export engines that produced the pack.
        var engines = first.GetProperty("artifacts").EnumerateArray()
            .Select(a => a.GetProperty("sourceEngine").GetString()).ToHashSet();
        Assert.Contains("PostureRenderer", engines);
        Assert.Contains("BaselineService.CompareSettingsCatalog", engines);
        Assert.Contains("AssignmentReportExporter.GenerateCsv", engines);
        _out.WriteLine($"reproducible manifestHash: {first.GetProperty("manifestHash").GetString()}");
    }

    // POA&M items are well-formed and every item's state is a valid open|closed value —
    // the state machine the closed-item inference relies on (diff itself is unit-tested).
    [Fact]
    public async Task Poam_ItemsAreWellFormed_WithValidState()
    {
        Assert.True(_fx.SignedIn, $"Sidecar is not signed in — cannot validate. {_fx.SignInError}");

        using var resp = await _fx.Http.GetAsync("/posture/poam?benchmark=cis");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("cis", root.GetProperty("benchmark").GetString());
        foreach (var item in root.GetProperty("items").EnumerateArray())
        {
            var state = item.GetProperty("state").GetString();
            Assert.Contains(state, new[] { "open", "closed" });
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("id").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("finding").GetString()));
            Assert.Equal(JsonValueKind.Array, item.GetProperty("controls").ValueKind);
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private async Task ScoreAsync(string benchmark)
    {
        using var resp = await _fx.Http.GetAsync($"/posture/score?benchmark={benchmark}");
        Assert.True(resp.IsSuccessStatusCode, $"/posture/score → {(int)resp.StatusCode}");
    }

    private async Task<int> TrendPointCountAsync(string benchmark)
    {
        using var resp = await _fx.Http.GetAsync($"/posture/trend?benchmark={benchmark}");
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("points").GetArrayLength();
    }

    private async Task<JsonElement> EvidencePackAsync(object req)
    {
        using var resp = await _fx.Http.PostAsJsonAsync("/posture/evidence-pack", req);
        var raw = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"/posture/evidence-pack → {(int)resp.StatusCode}: {raw}");
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
