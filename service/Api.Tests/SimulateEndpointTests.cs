using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace CmProjectX.Api.Tests;

// M16 Foresight — live, NON-MUTATING E2E coverage for POST /simulate, focused on the
// cross-policy cascade (the second-order blast radius). Simulation is a pure read — no
// Graph write happens — so these are safe against any tenant, including the Ivy24 sandbox.
// The pure classification is unit-tested hermetically in Api.Tests.Unit/SimulateCrossPolicyTests;
// this asserts the same behaviour end-to-end through the sidecar against a real directory.
[Collection("sidecar")]
public sealed class SimulateEndpointTests
{
    private static readonly string[] Severities = { "info", "low", "medium", "high", "critical" };

    private readonly SidecarFixture _fx;
    private readonly ITestOutputHelper _out;

    public SimulateEndpointTests(SidecarFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    // A CA policy tightened to require a compliant device for All Users always cascades:
    // either into the device-compliance policies that now gate sign-in, or — if the tenant
    // has none — into the hard-lockout warning. So crossPolicyImpacts is non-empty regardless
    // of tenant shape, which makes this a tenant-invariant assertion.
    [Fact]
    public async Task Simulate_CaRequireCompliantDevice_ProducesCrossPolicyCascade()
    {
        Assert.True(_fx.SignedIn, $"Sidecar is not signed in — cannot validate. {_fx.SignInError}");

        var body = """
        {
          "state": "enabled",
          "conditions": { "users": { "includeUsers": ["All"] } },
          "grantControls": { "operator": "OR", "builtInControls": ["compliantDevice"] }
        }
        """;
        var report = await SimulateAsync("update", "conditional-access", objectId: null, body);

        Assert.Contains(report.GetProperty("severity").GetString(), Severities);
        var cross = report.GetProperty("crossPolicyImpacts");
        Assert.Equal(JsonValueKind.Array, cross.ValueKind);
        Assert.True(cross.GetArrayLength() > 0,
            "requiring a compliant device for All Users must surface at least one cross-policy impact");
        foreach (var i in cross.EnumerateArray())
            Assert.Contains("compliant device", i.GetProperty("detail").GetString());

        _out.WriteLine($"CA cascade rows: {cross.GetArrayLength()}");
    }

    // A compliance change targeting All Devices returns a well-formed report; the cascade rows
    // are present iff the tenant has an enabled CA policy requiring a compliant device (so the
    // count is tenant-dependent — we assert shape + coupling wording, not a fixed count).
    [Fact]
    public async Task Simulate_ComplianceAllDevices_ReturnsWellFormedReport()
    {
        Assert.True(_fx.SignedIn, $"Sidecar is not signed in — cannot validate. {_fx.SignInError}");

        var body = """
        { "displayName": "sim-probe (not written)", "assignments": [ { "kind": "allDevices" } ] }
        """;
        var report = await SimulateAsync("update", "compliance-policies", objectId: null, body);

        Assert.Contains(report.GetProperty("severity").GetString(), Severities);
        var cross = report.GetProperty("crossPolicyImpacts");
        Assert.Equal(JsonValueKind.Array, cross.ValueKind);
        foreach (var i in cross.EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(i.GetProperty("policyName").GetString()));
            Assert.Contains("compliant device", i.GetProperty("detail").GetString());
        }

        _out.WriteLine($"compliance cascade rows: {cross.GetArrayLength()}");
    }

    // A non-coupled surface (e.g. a scope-tag assignment) produces no cross-policy cascade.
    [Fact]
    public async Task Simulate_NonCoupledSurface_HasNoCrossPolicyImpacts()
    {
        Assert.True(_fx.SignedIn, $"Sidecar is not signed in — cannot validate. {_fx.SignInError}");

        var body = """[ { "kind": "allUsers" } ]""";
        var report = await SimulateAsync("assign", "device-configs", objectId: null, body);

        var cross = report.GetProperty("crossPolicyImpacts");
        Assert.Equal(JsonValueKind.Array, cross.ValueKind);
        Assert.Equal(0, cross.GetArrayLength());
    }

    private async Task<JsonElement> SimulateAsync(string verb, string path, string? objectId, string bodyJson)
    {
        var req = new { proposer = "test:m16", verb, path, objectId, bodyJson };
        using var resp = await _fx.Http.PostAsJsonAsync("/simulate", req);
        var raw = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"/simulate {path} → {(int)resp.StatusCode}: {raw}");
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
