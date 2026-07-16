using CmProjectX.Api;
using Xunit;

namespace CmProjectX.Tests.Unit;

// Tier-1 hermetic tests for the M14 "Hands" device-action gate (DeviceActionGate.cs):
// the action catalog shape, the reversible/destructive classification, the org-allowlist
// + bulk-cap + opt-in resolution, the required-scope mapping, and the typed-confirm token
// checks. All pure (no Graph, no HTTP). The env-driven Config.FromEnvironment tests run
// serially within this class (xUnit runs a test class as one collection), setting then
// resetting the CMPROJECTX_* vars so they stay order-independent.
public class DeviceActionGateTests
{
    private static DeviceActionGate.Config Cfg(
        bool kill = false, bool allDestructive = false, IEnumerable<string>? enabled = null,
        bool bulkDestructive = false, int revCap = 200, int destCap = 25) =>
        new(kill, allDestructive,
            new HashSet<string>(enabled ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase),
            bulkDestructive, revCap, destCap);

    // ── catalog shape (gap D) ────────────────────────────────────────────────
    [Theory]
    [InlineData("setDeviceName")]
    [InlineData("rotateFileVaultKey")]
    [InlineData("createDeviceLogCollectionRequest")]
    [InlineData("autopilotReset")]
    public void Catalog_ContainsNewVerbs(string id) =>
        Assert.True(DeviceActionCatalog.Actions.ContainsKey(id), $"catalog missing '{id}'");

    [Theory]
    [InlineData("wipe", true)]
    [InlineData("retire", true)]
    [InlineData("cleanWindowsDevice", true)]
    [InlineData("autopilotReset", true)]
    [InlineData("syncDevice", false)]
    [InlineData("setDeviceName", false)]
    [InlineData("rotateFileVaultKey", false)]
    [InlineData("createDeviceLogCollectionRequest", false)]
    public void Catalog_ClassifiesDestructiveCorrectly(string id, bool destructive) =>
        Assert.Equal(destructive, DeviceActionCatalog.Actions[id].Destructive);

    [Fact]
    public void Catalog_AutopilotReset_DispatchesWipeWithAutopilotParams()
    {
        var meta = DeviceActionCatalog.Actions["autopilotReset"];
        Assert.Equal("wipe", meta.EffectiveVerb);       // no standalone autopilotReset Graph verb
        Assert.NotEqual(meta.Id, meta.EffectiveVerb);   // id stays distinct for the client + audit
        Assert.Contains("keepEnrollmentData", meta.DefaultBody);
    }

    [Fact]
    public void Catalog_ReversibleVerb_DefaultVerbEqualsId()
    {
        var meta = DeviceActionCatalog.Actions["syncDevice"];
        Assert.Equal("syncDevice", meta.EffectiveVerb); // EffectiveVerb falls back to Id
    }

    [Fact]
    public void Catalog_CollectDiagnostics_CarriesPredefinedTemplateBody() =>
        Assert.Contains("predefined", DeviceActionCatalog.Actions["createDeviceLogCollectionRequest"].DefaultBody);

    [Fact]
    public void Catalog_DestructiveVerbIds_MatchDestructiveEntries()
    {
        var expected = DeviceActionCatalog.Actions.Values.Where(a => a.Destructive).Select(a => a.Id);
        Assert.Equal(
            expected.OrderBy(x => x, StringComparer.Ordinal),
            DeviceActionCatalog.DestructiveVerbIds.OrderBy(x => x, StringComparer.Ordinal));
    }

    // ── org allowlist (gap C, rail 4) ────────────────────────────────────────
    [Fact]
    public void Destructive_OffByDefault() =>
        Assert.False(DeviceActionGate.IsDestructiveVerbEnabled(Cfg(), "wipe"));

    [Fact]
    public void Destructive_EnabledByPerVerbFlag()
    {
        var c = Cfg(enabled: new[] { "wipe" });
        Assert.True(DeviceActionGate.IsDestructiveVerbEnabled(c, "wipe"));
        Assert.False(DeviceActionGate.IsDestructiveVerbEnabled(c, "retire")); // per-verb, not blanket
    }

    [Fact]
    public void Destructive_EnabledByLegacyMasterSwitch()
    {
        var c = Cfg(allDestructive: true);
        Assert.True(DeviceActionGate.IsDestructiveVerbEnabled(c, "wipe"));
        Assert.True(DeviceActionGate.IsDestructiveVerbEnabled(c, "retire"));
    }

    // ── bulk caps (gap C, rail 5) ────────────────────────────────────────────
    [Fact]
    public void BulkCap_ReversibleVsDestructive()
    {
        var c = Cfg(revCap: 200, destCap: 25);
        Assert.Equal(200, DeviceActionGate.BulkCapFor(c, destructive: false));
        Assert.Equal(25, DeviceActionGate.BulkCapFor(c, destructive: true));
    }

    // ── scope-check (gap B, rail 3) ──────────────────────────────────────────
    [Fact]
    public void RequiredScope_MapsByClass()
    {
        Assert.Equal(DeviceActionGate.ReadWriteScope, DeviceActionGate.RequiredScope(destructive: false));
        Assert.Equal(DeviceActionGate.PrivilegedScope, DeviceActionGate.RequiredScope(destructive: true));
    }

    [Fact]
    public void HasScope_ExactMatch()
    {
        Assert.True(DeviceActionGate.HasDeviceActionScope(new[] { DeviceActionGate.ReadWriteScope }, destructive: false));
        Assert.True(DeviceActionGate.HasDeviceActionScope(new[] { DeviceActionGate.PrivilegedScope }, destructive: true));
    }

    [Fact]
    public void HasScope_PrivilegedSubsumesReversible() =>
        Assert.True(DeviceActionGate.HasDeviceActionScope(new[] { DeviceActionGate.PrivilegedScope }, destructive: false));

    [Fact]
    public void HasScope_ReadWriteDoesNotAuthorizeDestructive() =>
        Assert.False(DeviceActionGate.HasDeviceActionScope(new[] { DeviceActionGate.ReadWriteScope }, destructive: true));

    [Fact]
    public void HasScope_IsCaseInsensitive() =>
        Assert.True(DeviceActionGate.HasDeviceActionScope(
            new[] { DeviceActionGate.ReadWriteScope.ToLowerInvariant() }, destructive: false));

    [Fact]
    public void HasScope_MissingReturnsFalse() =>
        Assert.False(DeviceActionGate.HasDeviceActionScope(new[] { "Group.Read.All" }, destructive: false));

    // ── typed-confirm (gap E, rail 2) ────────────────────────────────────────
    [Fact]
    public void SingleConfirm_MatchesDeviceNameCaseInsensitively()
    {
        Assert.True(DeviceActionGate.SingleConfirmMatches("zzz-CMPX-01", "zzz-cmpx-01"));
        Assert.False(DeviceActionGate.SingleConfirmMatches("wrong", "zzz-cmpx-01"));
    }

    [Fact]
    public void SingleConfirm_RejectsWhenNameBlankOrConfirmMissing()
    {
        Assert.False(DeviceActionGate.SingleConfirmMatches("anything", ""));
        Assert.False(DeviceActionGate.SingleConfirmMatches(null, "zzz-cmpx-01"));
    }

    [Fact]
    public void BulkConfirm_TokenIsVerbSpaceCount() =>
        Assert.Equal("wipe 5", DeviceActionGate.BulkConfirmToken("wipe", 5));

    [Fact]
    public void BulkConfirm_IsCaseSensitive()
    {
        Assert.True(DeviceActionGate.BulkConfirmMatches("wipe 5", "wipe", 5));
        Assert.False(DeviceActionGate.BulkConfirmMatches("WIPE 5", "wipe", 5)); // case-SENSITIVE
        Assert.False(DeviceActionGate.BulkConfirmMatches("wipe 4", "wipe", 5)); // count must match
    }

    // ── env resolution (gap C — env overrides keep working) ──────────────────
    [Fact]
    public void FromEnvironment_Defaults_AllDestructiveOff_Caps200And25()
    {
        WithEnv(new()
        {
            ["CMPROJECTX_DEVICE_ACTIONS_DISABLED"] = null,
            ["CMPROJECTX_DEVICE_ACTIONS_DESTRUCTIVE"] = null,
            ["CMPROJECTX_DEVICE_ACTIONS_BULK_DESTRUCTIVE"] = null,
            ["CMPROJECTX_DEVICE_ACTIONS_BULK_CAP"] = null,
            ["CMPROJECTX_DEVICE_ACTIONS_DESTRUCTIVE_BULK_CAP"] = null,
            ["CMPROJECTX_DEVICE_ACTION_WIPE"] = null,
            ["CMPROJECTX_DEVICE_ACTION_RETIRE"] = null,
        }, () =>
        {
            var c = DeviceActionGate.FromEnvironment(DeviceActionCatalog.DestructiveVerbIds);
            Assert.False(c.GlobalKill);
            Assert.False(c.AllDestructive);
            Assert.False(c.BulkDestructive);
            Assert.Empty(c.EnabledDestructiveVerbs);
            Assert.Equal(DeviceActionGate.DefaultReversibleBulkCap, c.ReversibleBulkCap);
            Assert.Equal(DeviceActionGate.DefaultDestructiveBulkCap, c.DestructiveBulkCap);
            Assert.False(DeviceActionGate.IsDestructiveVerbEnabled(c, "wipe"));
        });
    }

    [Fact]
    public void FromEnvironment_PerVerbAllowlist_And_CapOverrides()
    {
        WithEnv(new()
        {
            ["CMPROJECTX_DEVICE_ACTION_WIPE"] = "1",
            ["CMPROJECTX_DEVICE_ACTIONS_BULK_DESTRUCTIVE"] = "true",
            ["CMPROJECTX_DEVICE_ACTIONS_BULK_CAP"] = "50",
            ["CMPROJECTX_DEVICE_ACTIONS_DESTRUCTIVE_BULK_CAP"] = "10",
            ["CMPROJECTX_DEVICE_ACTIONS_DESTRUCTIVE"] = null,
            ["CMPROJECTX_DEVICE_ACTION_RETIRE"] = null,
        }, () =>
        {
            var c = DeviceActionGate.FromEnvironment(DeviceActionCatalog.DestructiveVerbIds);
            Assert.True(DeviceActionGate.IsDestructiveVerbEnabled(c, "wipe"));
            Assert.False(DeviceActionGate.IsDestructiveVerbEnabled(c, "retire")); // only wipe allow-listed
            Assert.True(c.BulkDestructive);
            Assert.Equal(50, c.ReversibleBulkCap);
            Assert.Equal(10, c.DestructiveBulkCap);
        });
    }

    // Set the given env vars (null clears), run the body, then restore prior values.
    private static void WithEnv(Dictionary<string, string?> vars, Action body)
    {
        var prior = vars.Keys.ToDictionary(k => k, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var (k, v) in vars) Environment.SetEnvironmentVariable(k, v);
            body();
        }
        finally
        {
            foreach (var (k, v) in prior) Environment.SetEnvironmentVariable(k, v);
        }
    }
}
