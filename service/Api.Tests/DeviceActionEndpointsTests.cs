using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace CmProjectX.Api.Tests;

// M14 "Hands" device-action endpoints — black-box integration checks against the live
// sidecar (same convention as ListEndpointTests: skips/asserts on _fx.SignedIn, hits real
// routes). These validate the GATES that must fire BEFORE any Graph call, so they need no
// real device and cause no remote action:
//   • unknown verb                         → 400
//   • destructive verb, default env        → 403 (org allowlist off by default)
//   • destructive bulk, default env        → 403 (bulk-destructive opt-in off)
//   • reversible bulk over the 200 cap      → 400
//   • catalog GET shape (incl. the M14 additions)
//   • action-history GET shape (live device)
//
// The paths that require a sidecar launched WITH a verb enabled (typed-confirm 400, a
// destructive success) are authored as Skipped stubs — the fixture reuses whatever
// sidecar is already running and can't inject env into that separate process.
[Collection("sidecar")]
public sealed class DeviceActionEndpointsTests
{
    private readonly SidecarFixture _fx;
    private readonly ITestOutputHelper _out;

    public DeviceActionEndpointsTests(SidecarFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    private static readonly string DummyId = "00000000-0000-0000-0000-000000000000";

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    [Fact]
    public async Task Catalog_ReturnsVerbs_IncludingM14Additions()
    {
        using var resp = await _fx.Http.GetAsync("/managed-devices/actions");
        Assert.Equal(200, (int)resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);

        var byId = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var el in doc.RootElement.EnumerateArray())
            byId[el.GetProperty("id").GetString()!] = el.GetProperty("destructive").GetBoolean();

        // M14 catalog additions must be present.
        foreach (var id in new[] { "syncDevice", "wipe", "retire", "setDeviceName",
                                   "rotateFileVaultKey", "createDeviceLogCollectionRequest", "autopilotReset" })
            Assert.True(byId.ContainsKey(id), $"catalog missing '{id}'");

        // Classification.
        Assert.True(byId["wipe"]);
        Assert.True(byId["retire"]);
        Assert.True(byId["autopilotReset"]);
        Assert.True(byId["cleanWindowsDevice"]);
        Assert.False(byId["syncDevice"]);
        Assert.False(byId["setDeviceName"]);
        Assert.False(byId["rotateFileVaultKey"]);
    }

    [Fact]
    public async Task UnknownAction_Returns400()
    {
        Assert.True(_fx.SignedIn, $"Sidecar is not signed in. {_fx.SignInError}");
        using var resp = await _fx.Http.PostAsync($"/managed-devices/{DummyId}/actions/notARealVerb", Json("{}"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task DestructiveVerb_DisabledByDefault_Returns403()
    {
        // With no CMPROJECTX_DEVICE_ACTION_WIPE / _DESTRUCTIVE env set, the org allowlist
        // rejects wipe with 403 BEFORE any Graph call — a dummy id is fine.
        Assert.True(_fx.SignedIn, $"Sidecar is not signed in. {_fx.SignInError}");
        using var resp = await _fx.Http.PostAsync($"/managed-devices/{DummyId}/actions/wipe", Json("{}"));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task BulkReversible_OverCap_Returns400()
    {
        // 201 ids exceeds the reversible cap (200); the cap check fires before any Graph
        // call, so this is safe against dummy ids.
        Assert.True(_fx.SignedIn, $"Sidecar is not signed in. {_fx.SignInError}");
        var ids = string.Join(",", Enumerable.Range(0, 201).Select(i => $"\"dev-{i}\""));
        using var resp = await _fx.Http.PostAsync("/managed-devices/actions/syncDevice",
            Json($"{{\"deviceIds\":[{ids}]}}"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task BulkDestructive_DisabledByDefault_Returns403()
    {
        // wipe is off (allowlist) AND bulk-destructive opt-in is off by default; either
        // way this 403s before any Graph call.
        Assert.True(_fx.SignedIn, $"Sidecar is not signed in. {_fx.SignInError}");
        using var resp = await _fx.Http.PostAsync("/managed-devices/actions/wipe",
            Json("{\"deviceIds\":[\"dev-1\",\"dev-2\"]}"));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task ActionHistory_ForLiveDevice_ReturnsArray()
    {
        Assert.True(_fx.SignedIn, $"Sidecar is not signed in. {_fx.SignInError}");

        // Find a real device; skip cleanly if the tenant has none.
        using var list = await _fx.Http.GetAsync("/managed-devices");
        Assert.Equal(200, (int)list.StatusCode);
        using var listDoc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        if (listDoc.RootElement.GetArrayLength() == 0)
        {
            _out.WriteLine("no managed devices in tenant — skipping history shape check");
            return;
        }
        var id = listDoc.RootElement[0].GetProperty("id").GetString()!;

        using var resp = await _fx.Http.GetAsync($"/managed-devices/{id}/actions");
        Assert.Equal(200, (int)resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            Assert.True(row.TryGetProperty("id", out _), "history row missing id");
            Assert.True(row.TryGetProperty("action", out _), "history row missing action");
        }
    }

    // Requires a sidecar launched with CMPROJECTX_DEVICE_ACTION_WIPE=1 AND a real device id
    // whose name you type wrong/omit. The shared fixture can't inject env into the running
    // sidecar process, so this is documented rather than executed.
    [Fact(Skip = "needs a sidecar launched with CMPROJECTX_DEVICE_ACTION_WIPE=1 + a live device")]
    public void DestructiveVerb_Enabled_BadTypedConfirm_Returns400() { }
}
