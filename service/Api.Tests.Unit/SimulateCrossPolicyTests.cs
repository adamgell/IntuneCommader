using CmProjectX.Api;
using Xunit;

namespace CmProjectX.Tests.Unit;

// M16 Foresight — hermetic tests for the cross-policy cascade (the second-order blast
// radius, M16-simulator.md Examples A/B). Compliance and Conditional Access are coupled
// through the "compliantDevice" grant control; BuildCaLockoutImpacts (CA → compliance) and
// BuildComplianceFlipImpacts (compliance → CA) are the pure classifiers the sidecar calls
// once it has enumerated the other policy type. No Graph, no sidecar — pure decision logic.
public class SimulateCrossPolicyTests
{
    private static readonly (string Id, string Name)[] TwoComp =
    {
        ("comp-win-baseline", "Windows Compliance Baseline"),
        ("comp-mac-baseline", "macOS Compliance Baseline"),
    };

    // ── Direction A: CA change requiring a compliant device (Example A) ──────────

    [Fact]
    public void CaLockout_ScopeWidened_EmitsOneRowPerCompliancePolicy()
    {
        // Pilot → All Users widening of a compliant-device CA policy: 4,217 newly in scope.
        var impacts = BlastRadiusSimulator.BuildCaLockoutImpacts(
            proposedRequiresCompliant: true, proposedDisabled: false, liveRequiresCompliant: true,
            proposedAllUsers: true, proposedUserTargetCount: 0, affectedUsers: 4217, TwoComp);

        Assert.Equal(2, impacts.Count);
        Assert.Contains(impacts, i => i.PolicyId == "comp-win-baseline");
        Assert.All(impacts, i => Assert.Contains("All Users", i.Detail));
        Assert.All(impacts, i => Assert.Contains("compliant device", i.Detail));
    }

    [Fact]
    public void CaLockout_NewlyAddedGrant_NoScopeChange_StillFires()
    {
        // The scope didn't widen (affectedUsers == 0) but the compliantDevice grant is newly
        // added to a policy already targeting All Users — a real, easily-missed lockout.
        var impacts = BlastRadiusSimulator.BuildCaLockoutImpacts(
            proposedRequiresCompliant: true, proposedDisabled: false, liveRequiresCompliant: false,
            proposedAllUsers: true, proposedUserTargetCount: 0, affectedUsers: 0, TwoComp);

        Assert.Equal(2, impacts.Count);
    }

    [Fact]
    public void CaLockout_AlreadyRequired_NoScopeChange_IsSilent()
    {
        // Live already required it and nothing widened → no new population is gated.
        var impacts = BlastRadiusSimulator.BuildCaLockoutImpacts(
            proposedRequiresCompliant: true, proposedDisabled: false, liveRequiresCompliant: true,
            proposedAllUsers: true, proposedUserTargetCount: 0, affectedUsers: 0, TwoComp);

        Assert.Empty(impacts);
    }

    [Fact]
    public void CaLockout_NoCompliancePolicies_WarnsHardLockout()
    {
        // Requiring a compliant device when no compliance policy exists = everyone blocked.
        var impacts = BlastRadiusSimulator.BuildCaLockoutImpacts(
            proposedRequiresCompliant: true, proposedDisabled: false, liveRequiresCompliant: false,
            proposedAllUsers: true, proposedUserTargetCount: 0, affectedUsers: 4217,
            System.Array.Empty<(string, string)>());

        var only = Assert.Single(impacts);
        Assert.Equal("", only.PolicyId);
        Assert.Contains("no device compliance policies", only.Detail);
    }

    [Fact]
    public void CaLockout_ProposedDisabled_IsSilent()
    {
        var impacts = BlastRadiusSimulator.BuildCaLockoutImpacts(
            proposedRequiresCompliant: true, proposedDisabled: true, liveRequiresCompliant: false,
            proposedAllUsers: true, proposedUserTargetCount: 0, affectedUsers: 4217, TwoComp);

        Assert.Empty(impacts); // a disabled CA policy never enforces the grant
    }

    [Fact]
    public void CaLockout_NotRequiringCompliantDevice_IsSilent()
    {
        var impacts = BlastRadiusSimulator.BuildCaLockoutImpacts(
            proposedRequiresCompliant: false, proposedDisabled: false, liveRequiresCompliant: false,
            proposedAllUsers: true, proposedUserTargetCount: 0, affectedUsers: 4217, TwoComp);

        Assert.Empty(impacts);
    }

    [Fact]
    public void CaLockout_GroupScope_UsesAffectedCountInDetail()
    {
        var impacts = BlastRadiusSimulator.BuildCaLockoutImpacts(
            proposedRequiresCompliant: true, proposedDisabled: false, liveRequiresCompliant: false,
            proposedAllUsers: false, proposedUserTargetCount: 212, affectedUsers: 212, TwoComp);

        Assert.All(impacts, i => Assert.Contains("212 newly-in-scope user(s)", i.Detail));
    }

    // ── Direction B: compliance change flipping devices (Example B) ──────────────

    private static readonly (string Id, string Name, bool ReportOnly)[] OneEnabledCa =
    {
        ("ca-9f12-require-compliant", "Require compliant device", false),
    };

    [Fact]
    public void ComplianceFlip_AffectedDevices_EmitsRowPerCompliantDeviceCa()
    {
        // BitLocker requirement on the Windows baseline flips 812 devices non-compliant.
        var impacts = BlastRadiusSimulator.BuildComplianceFlipImpacts(
            proposedAllDevices: true, affectedDevices: 812, OneEnabledCa);

        var only = Assert.Single(impacts);
        Assert.Equal("ca-9f12-require-compliant", only.PolicyId);
        Assert.Equal("Require compliant device", only.PolicyName);
        Assert.Contains("All Devices", only.Detail);
        Assert.Contains("block their owners' sign-in", only.Detail);
    }

    [Fact]
    public void ComplianceFlip_NoAffectedDevices_NotAllDevices_IsSilent()
    {
        var impacts = BlastRadiusSimulator.BuildComplianceFlipImpacts(
            proposedAllDevices: false, affectedDevices: 0, OneEnabledCa);

        Assert.Empty(impacts);
    }

    [Fact]
    public void ComplianceFlip_ReportOnlyCa_IsAnnotated()
    {
        var reportOnly = new (string, string, bool)[] { ("ca-ro", "Pilot compliant device", true) };
        var impacts = BlastRadiusSimulator.BuildComplianceFlipImpacts(
            proposedAllDevices: false, affectedDevices: 300, reportOnly);

        var only = Assert.Single(impacts);
        Assert.Contains("(report-only)", only.PolicyName);
        Assert.Contains("once this policy is enforced", only.Detail);
        Assert.Contains("300 newly-targeted device(s)", only.Detail);
    }

    [Fact]
    public void ComplianceFlip_CapsAtMaxCrossPolicy()
    {
        var many = new (string, string, bool)[40];
        for (var i = 0; i < many.Length; i++) many[i] = ($"ca-{i}", $"CA {i}", false);

        var impacts = BlastRadiusSimulator.BuildComplianceFlipImpacts(
            proposedAllDevices: true, affectedDevices: 1, many);

        Assert.Equal(25, impacts.Count); // MaxCrossPolicy
    }
}
