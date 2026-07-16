using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CmProjectX.Api;
using CmProjectX.Store;
using Intune.Commander.Core.Services;
using Xunit;

namespace CmProjectX.Tests.Unit;

// M19 Continuous Posture — hermetic tests for the evidence-integrity caveat and the
// trend math, added after the adversarial review. The review's central finding: score
// and coverage count policy presence, not per-control verification, so an evidence pack
// could be misread as a compliance attestation. The fix is HONESTY — every pack embeds
// MethodologyCaveat; these tests lock that wording in and cover the trend/NRE paths.
//
// M19 backend completion adds pure-logic coverage for the four gaps filled in:
// benchmark coverage math (broadened denominators), POA&M open→closed inference,
// the M18 remediation-candidate mapping, and the enriched trend delta.
public class PostureTests
{
    // "cis" never touches BaselineService (only "oib" does), so this is fully hermetic.
    private static BenchmarkMapService Map() => new(new BaselineService());

    private static BenchmarkedPostureDto MinimalPosture(int score = 50) => new(
        Benchmark: "cis", BenchmarkVersion: "1.0", Score: score,
        Coverage: new BenchmarkCoverageDto(2, 5, 40),
        Breakdown: new List<BenchmarkedScoreCategoryDto>(),
        Gaps: new List<BenchmarkedGapDto>(),
        Stats: new PostureStatsDto(0, 0, 0, 0, 0, Array.Empty<string>(), 0, 0, 0, 0));

    private static BenchmarkedPostureDto PostureWithGaps(params BenchmarkedGapDto[] gaps) => new(
        Benchmark: "cis", BenchmarkVersion: "1.0", Score: 50,
        Coverage: new BenchmarkCoverageDto(0, 0, 0),
        Breakdown: new List<BenchmarkedScoreCategoryDto>(),
        Gaps: gaps.ToList(),
        Stats: new PostureStatsDto(0, 0, 0, 0, 0, Array.Empty<string>(), 0, 0, 0, 0));

    private static BenchmarkedGapDto Gap(string severity, string category, string description) =>
        new(severity, category, description, new List<BenchmarkControlRefDto>());

    [Fact]
    public void MethodologyCaveat_DisclaimsPerControlAttestation()
    {
        Assert.Contains("per-control attestation", PostureEndpoints.MethodologyCaveat);
        Assert.Contains("gaps as authoritative", PostureEndpoints.MethodologyCaveat);
    }

    [Fact]
    public void RenderScoreMarkdown_EmbedsTheMethodologyCaveat()
    {
        var md = PostureEndpoints.RenderScoreMarkdown(MinimalPosture(), "tenant", DateTime.UtcNow);
        Assert.Contains(PostureEndpoints.MethodologyCaveat, md);
        Assert.Contains("Coverage:", md);
    }

    [Fact]
    public void ComputeDelta_ComputesScoreChangeAndFlagsRegressions()
    {
        var first = new PostureTrendPointDto("2026-01-01T00:00:00Z", 80,
            new Dictionary<string, int> { ["Conditional Access"] = 30, ["Compliance"] = 20 });
        var last = new PostureTrendPointDto("2026-02-01T00:00:00Z", 60,
            new Dictionary<string, int> { ["Conditional Access"] = 20, ["Compliance"] = 20 });

        var delta = PostureEndpoints.ComputeDelta(new[] { first, last });

        Assert.Equal(-20, delta.ScoreChange);
        Assert.Contains(delta.Regressions, r => r.StartsWith("Conditional Access"));
        Assert.DoesNotContain(delta.Regressions, r => r.StartsWith("Compliance")); // unchanged → not a regression
    }

    [Fact]
    public void ComputeDelta_EmptyPoints_IsZero()
    {
        var delta = PostureEndpoints.ComputeDelta(Array.Empty<PostureTrendPointDto>());
        Assert.Equal(0, delta.ScoreChange);
        Assert.Empty(delta.Regressions);
    }

    [Fact]
    public void ProjectPoint_BodyWithoutBreakdown_DoesNotThrow()
    {
        // A stored body missing "breakdown" deserializes Breakdown to null — the trend
        // projection must coalesce, not NRE (the review's robustness finding).
        var body = JsonSerializer.Serialize(new { benchmark = "cis", benchmarkVersion = "1.0", score = 42 });
        var rec = new ConfigSnapshotRecord("id1", "cis", "security-posture", "Posture (cis)", DateTime.UtcNow, body);

        var point = PostureEndpoints.ProjectPoint(rec);

        Assert.NotNull(point);
        Assert.Equal(42, point!.Score);
        Assert.Empty(point.CategoryScores);
    }

    // ── Benchmark coverage math (broadened denominators) ───────────────────────

    [Fact]
    public void BenchmarkMap_IsBroadened_MultipleControlsPerCategory()
    {
        var map = Map();
        // Every category maps to >1 CIS control now (was one token per bucket).
        foreach (var cat in new[] { "Conditional Access", "Compliance", "Endpoint Security",
                                    "App Protection", "Auth & Locations" })
            Assert.True(map.CategoryControls(cat, "cis").Count > 1,
                $"category '{cat}' should map to multiple CIS controls");
        // The coverage denominator is now a meaningful count, not five.
        Assert.True(map.AllControls("cis").Count >= 15,
            "the benchmark map should be broadened well beyond one control per category");
    }

    [Fact]
    public void ComputeCoverage_CountsControlsOfScoredCategoriesOverAllInScope()
    {
        var map = Map();
        var covered = new[] { "Conditional Access", "Compliance" };

        var cov = PostureEndpoints.ComputeCoverage(covered, "cis", map);

        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cat in covered)
            foreach (var c in map.CategoryControls(cat, "cis"))
                expected.Add($"{c.Framework}|{c.Id}");

        Assert.Equal(expected.Count, cov.ControlsCovered);
        Assert.Equal(map.AllControls("cis").Count, cov.ControlsTotal);
        Assert.True(cov.ControlsCovered < cov.ControlsTotal); // partial coverage is possible
        Assert.Equal((int)Math.Round(100.0 * cov.ControlsCovered / cov.ControlsTotal), cov.Percent);
    }

    // ── POA&M open → closed inference ──────────────────────────────────────────

    [Fact]
    public void InferClosedItems_GapAbsentFromCurrent_IsClosed()
    {
        var map = Map();
        var prior = PostureWithGaps(Gap("high", "Conditional Access", "No CA policy targets all users"));
        var closedId = PostureEndpoints.PoamIdFromParts("Conditional Access", "No CA policy targets all users");

        // Current has NO open items → the historical gap must be inferred closed.
        var closed = PostureEndpoints.InferClosedItems(
            new HashSet<string>(),
            new List<(DateTime, BenchmarkedPostureDto)> { (DateTime.UtcNow.AddDays(-7), prior) },
            "cis", map);

        var item = Assert.Single(closed);
        Assert.Equal(closedId, item.Id);
        Assert.Equal("closed", item.State);
        Assert.Equal("Conditional Access", item.Category);
        Assert.NotEmpty(item.Controls); // controls are re-grafted from the map
    }

    [Fact]
    public void InferClosedItems_GapStillOpen_IsNotClosed()
    {
        var map = Map();
        var prior = PostureWithGaps(Gap("high", "Compliance", "No compliance policies configured"));
        var stillOpenId = PostureEndpoints.PoamIdFromParts("Compliance", "No compliance policies configured");

        var closed = PostureEndpoints.InferClosedItems(
            new HashSet<string> { stillOpenId },
            new List<(DateTime, BenchmarkedPostureDto)> { (DateTime.UtcNow.AddDays(-7), prior) },
            "cis", map);

        Assert.Empty(closed);
    }

    [Fact]
    public void InferClosedItems_DedupesRepeatedHistoricalGap()
    {
        var map = Map();
        var gap = Gap("medium", "App Protection", "No app protection policies — mobile data is unprotected");
        var older = (DateTime.UtcNow.AddDays(-30), PostureWithGaps(gap));
        var newer = (DateTime.UtcNow.AddDays(-2), PostureWithGaps(gap));

        var closed = PostureEndpoints.InferClosedItems(
            new HashSet<string>(),
            new List<(DateTime, BenchmarkedPostureDto)> { older, newer },
            "cis", map);

        Assert.Single(closed); // one id even though it appears in two snapshots
    }

    [Fact]
    public void PoamIdFromParts_IsStableAndCategorySlugged()
    {
        var a = PostureEndpoints.PoamIdFromParts("Conditional Access", "No CA policy targets all users");
        var b = PostureEndpoints.PoamIdFromParts("Conditional Access", "No CA policy targets all users");
        Assert.Equal(a, b);
        Assert.StartsWith("poam-conditional-access-", a);
    }

    // ── M18 feed (read-only remediation candidates) ────────────────────────────

    [Fact]
    public void ComputeDelta_RegressionCarriesRemediationCandidate()
    {
        var first = new PostureTrendPointDto("2026-01-01T00:00:00Z", 80,
            new Dictionary<string, int> { ["App Protection"] = 15, ["Compliance"] = 20 });
        var last = new PostureTrendPointDto("2026-02-01T00:00:00Z", 70,
            new Dictionary<string, int> { ["App Protection"] = 10, ["Compliance"] = 20 });

        var delta = PostureEndpoints.ComputeDelta(new[] { first, last });

        var cand = Assert.Single(delta.RemediationCandidates);
        Assert.Equal("App Protection", cand.Category);
        Assert.Equal(-5, cand.ScoreChange);
        Assert.Equal("m18:app-protection-policy", cand.RemediationCandidateRef);
    }

    [Fact]
    public void ComputeDelta_NoRegression_HasNoCandidates()
    {
        var first = new PostureTrendPointDto("2026-01-01T00:00:00Z", 60,
            new Dictionary<string, int> { ["Compliance"] = 20 });
        var last = new PostureTrendPointDto("2026-02-01T00:00:00Z", 70,
            new Dictionary<string, int> { ["Compliance"] = 25 });

        var delta = PostureEndpoints.ComputeDelta(new[] { first, last });

        Assert.Empty(delta.RemediationCandidates);
        Assert.Empty(delta.Regressions);
    }

    [Theory]
    [InlineData("Conditional Access", "m18:ca-enable-mfa")]
    [InlineData("Compliance", "m18:compliance-baseline")]
    [InlineData("Endpoint Security", "m18:endpoint-security-baseline")]
    [InlineData("App Protection", "m18:app-protection-policy")]
    [InlineData("Auth & Locations", "m18:named-locations")]
    public void RemediationCandidateForCategory_MapsKnownCategories(string category, string expected)
    {
        Assert.Equal(expected, PostureEndpoints.RemediationCandidateForCategory(category));
    }

    [Fact]
    public void RemediationCandidateForCategory_UnknownCategory_IsNull()
    {
        Assert.Null(PostureEndpoints.RemediationCandidateForCategory("Not A Real Category"));
    }

    // ── benchmark map externalization (M19 deferral closed) ──────────────────────────

    [Fact]
    public void BenchmarkMap_ExposesVersionFromExternalizedResource()
    {
        // If the embedded benchmark-map.json failed to load, LoadMap throws at static init
        // and every benchmark test cascades — so a non-empty version proves the resource path.
        Assert.False(string.IsNullOrWhiteSpace(BenchmarkMapService.MapVersion));
    }

    // ── SimplePdf score-summary writer (M19 PDF deferral closed) ─────────────────────

    [Fact]
    public void SimplePdf_ProducesValidPdfStructure()
    {
        var bytes = SimplePdf.FromLines(new[] { "Line one", "Line (two) with parens \\ backslash", "" });
        var text = System.Text.Encoding.Latin1.GetString(bytes); // 1:1 byte<->char, so offsets == indices
        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);
        // The startxref offset must point exactly at the "xref" table.
        var idx = text.LastIndexOf("startxref", StringComparison.Ordinal);
        var off = int.Parse(text.Substring(idx + "startxref".Length).Trim().Split('\n')[0]);
        Assert.Equal("xref", text.Substring(off, 4));
    }

    [Fact]
    public void SimplePdf_IsDeterministic()
    {
        Assert.Equal(SimplePdf.FromLines(new[] { "same", "input" }),
                     SimplePdf.FromLines(new[] { "same", "input" }));
    }

    [Fact]
    public void SimplePdf_PaginatesLongInput()
    {
        var many = Enumerable.Range(0, 200).Select(i => $"line {i}").ToList();
        var text = System.Text.Encoding.Latin1.GetString(SimplePdf.FromLines(many));
        var pages = System.Text.RegularExpressions.Regex.Matches(text, "/Type /Page[^s]").Count;
        Assert.True(pages >= 4, $"200 lines should paginate to multiple pages, got {pages}");
    }
}
