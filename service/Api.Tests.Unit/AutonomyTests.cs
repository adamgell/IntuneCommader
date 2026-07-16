using CmProjectX.Api;
using Xunit;

namespace CmProjectX.Tests.Unit;

// M18 Autonomy — hermetic tests for AutonomyPolicyStore.Normalize, the write-boundary
// guard added after the adversarial review. PUT /autonomy/policy is unvalidated minimal-
// API model-binding, so a partial/hostile body can deserialize null sub-objects or a zero
// cadence; Normalize must clamp/coalesce them before they reach the scheduler/engine
// (a 0 cadence would spin Task.Delay; a null ObjectTypes would NRE the drift scan).
public class AutonomyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Normalize_FloorsCadenceAtOneMinute(int cadence)
    {
        var p = AutonomyPolicyStore.Defaults("t") with { CadenceMinutes = cadence };
        Assert.Equal(1, AutonomyPolicyStore.Normalize(p, "t").CadenceMinutes);
    }

    [Fact]
    public void Normalize_PreservesAValidCadence()
    {
        var p = AutonomyPolicyStore.Defaults("t") with { CadenceMinutes = 30 };
        Assert.Equal(30, AutonomyPolicyStore.Normalize(p, "t").CadenceMinutes);
    }

    [Fact]
    public void Normalize_CoalescesNullScopeToEmpty()
    {
        // A body of {"scope": null} deserializes Scope to null.
        var p = AutonomyPolicyStore.Defaults("t") with { Scope = null! };
        var n = AutonomyPolicyStore.Normalize(p, "t");
        Assert.NotNull(n.Scope);
        Assert.NotNull(n.Scope.ObjectTypes);
    }

    [Fact]
    public void Normalize_CoalescesNullObjectTypesToEmpty()
    {
        // {"scope": {"objectTypes": null}} — the field is declared non-nullable but STJ
        // does not enforce that; the engine's ToHashSet would NRE without this.
        var p = AutonomyPolicyStore.Defaults("t") with
        {
            Scope = new AutonomyScopeDto(ObjectTypes: null!, AssignmentGroupAllowlist: null!),
        };
        var n = AutonomyPolicyStore.Normalize(p, "t");
        Assert.Empty(n.Scope.ObjectTypes);
        Assert.Empty(n.Scope.AssignmentGroupAllowlist);
    }

    [Fact]
    public void Normalize_ClampsNegativeThrottleToZero()
    {
        var p = AutonomyPolicyStore.Defaults("t") with
        {
            Throttle = new AutonomyThrottleDto(MaxProposalsPerRun: -3, MaxInflightPending: -1),
        };
        var n = AutonomyPolicyStore.Normalize(p, "t");
        Assert.Equal(0, n.Throttle.MaxProposalsPerRun);
        Assert.Equal(0, n.Throttle.MaxInflightPending);
    }

    [Fact]
    public void Normalize_BackfillsNullSignals()
    {
        var p = AutonomyPolicyStore.Defaults("t") with { Signals = null! };
        var n = AutonomyPolicyStore.Normalize(p, "t");
        Assert.NotNull(n.Signals);
        Assert.NotNull(n.Signals.Drift);
    }

    [Fact]
    public void Normalize_StampsActiveTenant()
    {
        var n = AutonomyPolicyStore.Normalize(AutonomyPolicyStore.Defaults(null), "tenant-x");
        Assert.Equal("tenant-x", n.TenantId);
    }

    [Fact]
    public void Defaults_NeverAutoApply_OnlyEnqueueEligible()
    {
        // Guardrail: the default policy must keep the locked guarantee legible.
        var d = AutonomyPolicyStore.Defaults("t");
        Assert.Contains("auto-appl", d.Note, System.StringComparison.OrdinalIgnoreCase);
    }

    // ── posture-regression signal (M18 — the previously-dead Posture toggle) ──────────

    [Fact]
    public void Posture_FirstObservation_NoBaseline_NoDetection()
    {
        // prev null ⇒ this tick only establishes the baseline; never a regression.
        Assert.Null(AutonomyEngine.EvaluatePostureRegression(prev: null, current: 80, minScoreDrop: 3));
    }

    [Fact]
    public void Posture_CurrentUnavailable_NoDetection()
    {
        // A failed /posture/score fetch (current null) skips the signal, doesn't crash.
        Assert.Null(AutonomyEngine.EvaluatePostureRegression(prev: 80, current: null, minScoreDrop: 3));
    }

    [Fact]
    public void Posture_DropBelowFloor_NoDetection()
    {
        Assert.Null(AutonomyEngine.EvaluatePostureRegression(prev: 80, current: 78, minScoreDrop: 3)); // drop 2 < 3
    }

    [Fact]
    public void Posture_ScoreImproved_NoDetection()
    {
        // A negative drop (score went UP) is never a regression.
        Assert.Null(AutonomyEngine.EvaluatePostureRegression(prev: 70, current: 85, minScoreDrop: 3));
    }

    [Fact]
    public void Posture_DropMeetsFloor_Detects()
    {
        var d = AutonomyEngine.EvaluatePostureRegression(prev: 80, current: 77, minScoreDrop: 3); // drop 3 == floor
        Assert.NotNull(d);
        Assert.Equal("posture", d!.Signal);
        Assert.Equal("posture:score", d.ObjectId);
        var change = Assert.Single(d.Changes);
        Assert.Equal("posture.score", change.Path);
    }

    [Fact]
    public void Posture_NullMinScoreDrop_UsesDefaultFloorOfThree()
    {
        Assert.Null(AutonomyEngine.EvaluatePostureRegression(prev: 80, current: 78, minScoreDrop: null)); // drop 2 < 3
        Assert.NotNull(AutonomyEngine.EvaluatePostureRegression(prev: 80, current: 76, minScoreDrop: null)); // drop 4 >= 3
    }

    [Theory]
    [InlineData(3, "low")]
    [InlineData(4, "low")]
    [InlineData(5, "medium")]
    [InlineData(9, "medium")]
    [InlineData(10, "high")]
    [InlineData(14, "high")]
    [InlineData(15, "critical")]
    [InlineData(30, "critical")]
    public void Posture_SeverityScalesWithDrop(int drop, string expected)
    {
        var d = AutonomyEngine.EvaluatePostureRegression(prev: 100, current: 100 - drop, minScoreDrop: 3);
        Assert.NotNull(d);
        Assert.Equal(expected, d!.Severity);
    }

    // ── advisory signal (M18 — the previously-dead Advisory toggle, now a real detection) ──

    private static BenchmarkedGapDto Gap(string category, string description, string severity = "high") =>
        new(severity, category, description, System.Array.Empty<BenchmarkControlRefDto>());

    private static readonly AdvisoryCatalog.Advisory LegacyAuth =
        new("ADV-T-1", "Legacy auth not blocked", "high", "Conditional Access", "legacy", "block it");

    [Fact]
    public void Advisory_MatchesGap_ByCategoryAndKeyword()
    {
        var gaps = new[] { Gap("Conditional Access", "Legacy authentication is permitted") };
        var d = AdvisoryCatalog.Match(new[] { LegacyAuth }, gaps, minSeverity: null);
        var only = Assert.Single(d);
        Assert.Equal("advisory", only.Detection.Signal);
        Assert.Equal("advisory:ADV-T-1", only.Detection.ObjectId);
        Assert.Null(only.Advisory.Remediation); // no remediation ⇒ detection-only
    }

    [Fact]
    public void Advisory_WithRemediation_CarriesProposableWrite()
    {
        var rem = new AdvisoryCatalog.AdvisoryRemediation("create", "/compliance-policies", null, "{\"displayName\":\"x\"}");
        var adv = LegacyAuth with
        {
            Id = "ADV-T-2", MatchCategory = "Endpoint Security", MatchKeyword = "encryption", Remediation = rem,
        };
        var gaps = new[] { Gap("Endpoint Security", "Disk encryption is not enforced") };
        var m = Assert.Single(AdvisoryCatalog.Match(new[] { adv }, gaps, null));
        Assert.NotNull(m.Advisory.Remediation);
        Assert.Equal("create", m.Advisory.Remediation!.Verb);
        Assert.Equal("/compliance-policies", m.Advisory.Remediation.Path);
    }

    [Fact]
    public void Advisory_NoMatch_WhenCategoryDiffers() =>
        Assert.Empty(AdvisoryCatalog.Match(new[] { LegacyAuth },
            new[] { Gap("Compliance", "Legacy authentication is permitted") }, null));

    [Fact]
    public void Advisory_NoMatch_WhenKeywordAbsent() =>
        Assert.Empty(AdvisoryCatalog.Match(new[] { LegacyAuth },
            new[] { Gap("Conditional Access", "MFA is not enforced") }, null));

    [Fact]
    public void Advisory_SeverityFloor_FiltersLowerSeverity()
    {
        var gaps = new[] { Gap("Conditional Access", "Legacy authentication is permitted") };
        Assert.Empty(AdvisoryCatalog.Match(new[] { LegacyAuth }, gaps, minSeverity: "critical")); // advisory is "high"
        Assert.NotEmpty(AdvisoryCatalog.Match(new[] { LegacyAuth }, gaps, minSeverity: "medium"));
    }

    [Fact]
    public void Advisory_EmptyKeyword_MatchesAnyGapInCategory() =>
        Assert.Single(AdvisoryCatalog.Match(new[] { LegacyAuth with { MatchKeyword = null } },
            new[] { Gap("Conditional Access", "anything at all") }, null));

    [Fact]
    public void Advisory_EmbeddedCatalog_IsNonEmpty() =>
        Assert.True(AdvisoryCatalog.Count > 0); // the embedded advisories.json loaded
}
