using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace CmProjectX.Api.Tests;

// M20 Fleet — live, black-box coverage for the /fleet/* fan-out plane (Pattern G).
// These hit the running sidecar exactly like the WinUI client would. The deterministic
// contract invariants (unknown-surface/group → 404, dry-run enqueues nothing, per-tenant
// error isolation) are asserted unconditionally; the merge/tagging and cross-tenant
// approve-refusal paths need real signed-in tenants and are guarded on _fx.SignedIn.
//
// Isolation guarantee under test: one tenant's failure (here forced with a bogus tenant
// id that has no saved profile) is reported PER TENANT — the fan-out still returns 200
// with the healthy tenants' rows, never a fleet-wide abort.
[Collection("sidecar")]
public sealed class FleetEndpointsTests
{
    // A syntactically-valid tenant GUID that has no saved profile — EnsureAsync throws
    // "No saved profile…", which the fan-out classifies as that tenant's error, not a fleet abort.
    private const string BogusTenantId = "00000000-0000-0000-0000-0000000000ff";

    private readonly SidecarFixture _fx;
    private readonly ITestOutputHelper _out;

    public FleetEndpointsTests(SidecarFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    // ── deterministic contract guards (pass signed-in or not) ─────────────────

    [Fact]
    public async Task FleetList_UnknownSurface_Returns404()
    {
        using var resp = await _fx.Http.GetAsync("/fleet/list/not-a-real-surface?group=whatever");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task FleetList_UnknownGroup_Returns404()
    {
        using var resp = await _fx.Http.GetAsync($"/fleet/list/compliance-policies?group=missing-{Guid.NewGuid():n}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // Per-tenant error isolation: a group whose members include a bogus (profile-less)
    // tenant still returns 200; that tenant is reported in `errors` + a non-"ok" status
    // row, while any healthy tenant's rows merge in tenant-tagged. This is the DoD's
    // "one tenant's throttle/error reported per tenant, not a fleet abort".
    [Fact]
    public async Task FleetList_MergesTenantTaggedRows_AndIsolatesPerTenantError()
    {
        var active = await ActiveTenantIdAsync();
        // Fan out over the active tenant (healthy when signed in) + a bogus one (always fails).
        // Restricting to the active tenant avoids triggering interactive-auth flows on other profiles.
        var members = active is null ? new[] { BogusTenantId } : new[] { active, BogusTenantId };
        var groupId = $"test-m20-list-{Guid.NewGuid():n}";
        await UpsertGroupAsync(groupId, members, goldenTenantId: active);
        try
        {
            using var resp = await _fx.Http.GetAsync($"/fleet/list/compliance-policies?group={groupId}");
            var raw = await resp.Content.ReadAsStringAsync();
            Assert.True(resp.IsSuccessStatusCode, $"/fleet/list → {(int)resp.StatusCode}: {raw}");
            var body = JsonDocument.Parse(raw).RootElement;

            // The bogus tenant must surface as a per-tenant error, never abort the fan-out.
            var errors = body.GetProperty("errors").EnumerateArray().ToList();
            Assert.Contains(errors, e => e.GetProperty("tenantId").GetString() == BogusTenantId);
            var statuses = body.GetProperty("tenants").EnumerateArray().ToList();
            Assert.Contains(statuses, s =>
                s.GetProperty("tenantId").GetString() == BogusTenantId &&
                s.GetProperty("status").GetString() != "ok");

            // Every merged row is tenant-tagged (this is what makes a fleet list a fleet list).
            foreach (var it in body.GetProperty("items").EnumerateArray())
            {
                Assert.False(string.IsNullOrWhiteSpace(it.GetProperty("tenantId").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(it.GetProperty("tenantName").GetString()));
            }

            if (_fx.SignedIn && active is not null)
            {
                // The active tenant should fan out cleanly → at least one "ok" status row.
                Assert.Contains(statuses, s => s.GetProperty("status").GetString() == "ok");
            }
            _out.WriteLine($"fleet list: {statuses.Count} tenants, {body.GetProperty("items").GetArrayLength()} rows, {errors.Count} errors");
        }
        finally { await DeleteGroupAsync(groupId); }
    }

    // dryRun must resolve + diff only and enqueue NOTHING: summary.pending == 0, no result
    // carries a pendingId, and the pending-changes inbox count is unchanged across the call.
    [Fact]
    public async Task FleetCampaign_DryRun_ComputesPreview_AndWritesNothing()
    {
        var active = await ActiveTenantIdAsync();
        var members = active is null ? new[] { BogusTenantId } : new[] { active, BogusTenantId };
        var groupId = $"test-m20-camp-{Guid.NewGuid():n}";
        await UpsertGroupAsync(groupId, members, goldenTenantId: active);

        var pendingBefore = await PendingCountAsync();
        try
        {
            var req = new
            {
                campaignId = (string?)null,
                group = groupId,
                golden = new { kind = "goldenTenant", tenantId = active, repoRef = (string?)null },
                surface = "compliance-policies",
                objectName = "Win10 Baseline",
                verb = "PATCH",
                dryRun = true,
                gate = "perTenantConfirm",
                overrides = (object?)null,
            };
            using var resp = await _fx.Http.PostAsJsonAsync("/fleet/campaign", req);
            var raw = await resp.Content.ReadAsStringAsync();
            Assert.True(resp.IsSuccessStatusCode, $"/fleet/campaign → {(int)resp.StatusCode}: {raw}");
            var body = JsonDocument.Parse(raw).RootElement;

            Assert.True(body.GetProperty("dryRun").GetBoolean(), "dryRun must round-trip true");
            Assert.Equal(0, body.GetProperty("summary").GetProperty("pending").GetInt32());
            foreach (var r in body.GetProperty("results").EnumerateArray())
            {
                var pid = r.GetProperty("pendingId");
                Assert.True(pid.ValueKind == JsonValueKind.Null,
                    $"dry-run must not enqueue a gated change (tenant {r.GetProperty("tenantId").GetString()} got pendingId {pid})");
                Assert.NotEqual("pending", r.GetProperty("outcome").GetString());
            }
            _out.WriteLine($"dry-run outcomes: {string.Join(", ", body.GetProperty("results").EnumerateArray().Select(r => r.GetProperty("outcome").GetString()))}");
        }
        finally { await DeleteGroupAsync(groupId); }

        // The dry run wrote nothing — the inbox is exactly as large as before.
        Assert.Equal(pendingBefore, await PendingCountAsync());
    }

    // Cross-tenant approve refusal (Program.cs /pending-changes/{id}/approve): a campaign's
    // gated replay is bound to its TARGET tenant, so approving it while a DIFFERENT tenant is
    // active must 409 ("switch to that tenant"). Setting golden == active means every pending
    // result targets a non-active tenant, so any of them exercises the refusal. Needs ≥2
    // signed-in tenants that share the named object, so it is guarded/skipped otherwise.
    [Fact]
    public async Task FleetCampaign_LiveGate_CrossTenantApprovalRefused()
    {
        var active = await ActiveTenantIdAsync();
        if (!_fx.SignedIn || active is null)
        {
            _out.WriteLine($"skipped: not signed in ({_fx.SignInError})");
            return;
        }

        var known = await KnownTenantIdsAsync();
        if (known.Count < 2)
        {
            _out.WriteLine($"skipped: need ≥2 known tenant profiles for a cross-tenant gate (have {known.Count})");
            return;
        }

        var groupId = $"test-m20-gate-{Guid.NewGuid():n}";
        await UpsertGroupAsync(groupId, known, goldenTenantId: active);
        try
        {
            var req = new
            {
                campaignId = (string?)null,
                group = groupId,
                golden = new { kind = "goldenTenant", tenantId = active, repoRef = (string?)null },
                surface = "compliance-policies",
                objectName = "Win10 Baseline",
                verb = "PATCH",
                dryRun = false,
                gate = "perTenantConfirm",
                overrides = (object?)null,
            };
            using var resp = await _fx.Http.PostAsJsonAsync("/fleet/campaign", req);
            var raw = await resp.Content.ReadAsStringAsync();
            Assert.True(resp.IsSuccessStatusCode, $"/fleet/campaign → {(int)resp.StatusCode}: {raw}");
            var body = JsonDocument.Parse(raw).RootElement;

            var pending = body.GetProperty("results").EnumerateArray()
                .FirstOrDefault(r => r.GetProperty("outcome").GetString() == "pending"
                    && r.GetProperty("pendingId").ValueKind == JsonValueKind.String);
            if (pending.ValueKind != JsonValueKind.Object)
            {
                _out.WriteLine("skipped: no gated (pending) target — no non-golden tenant had the object with a diff");
                return;
            }

            var pendingId = pending.GetProperty("pendingId").GetString();
            // golden == active, so this pending targets a NON-active tenant → approval must be refused.
            using var approve = await _fx.Http.PostAsync($"/pending-changes/{pendingId}/approve", content: null);
            var approveRaw = await approve.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
            Assert.Contains("tenant", approveRaw, StringComparison.OrdinalIgnoreCase);
            _out.WriteLine($"cross-tenant approve refused: {approveRaw}");
        }
        finally { await DeleteGroupAsync(groupId); }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<string?> ActiveTenantIdAsync()
    {
        using var resp = await _fx.Http.GetAsync("/health");
        if (!resp.IsSuccessStatusCode) return null;
        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        return doc.TryGetProperty("tenantId", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
    }

    private async Task<List<string>> KnownTenantIdsAsync()
    {
        using var resp = await _fx.Http.GetAsync("/fleet/tenants");
        if (!resp.IsSuccessStatusCode) return new();
        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        return doc.EnumerateArray()
            .Select(e => e.GetProperty("tenantId").GetString())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .ToList();
    }

    private async Task<int> PendingCountAsync()
    {
        using var resp = await _fx.Http.GetAsync("/pending-changes?state=pending");
        if (!resp.IsSuccessStatusCode) return 0;
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetArrayLength();
    }

    private async Task UpsertGroupAsync(string id, IReadOnlyList<string> tenantIds, string? goldenTenantId)
    {
        var group = new { id, name = id, tenantIds, goldenTenantId, createdUtc = (string?)null };
        using var resp = await _fx.Http.PostAsJsonAsync("/fleet/groups", group);
        Assert.True(resp.IsSuccessStatusCode, $"/fleet/groups upsert → {(int)resp.StatusCode}");
    }

    private async Task DeleteGroupAsync(string id)
    {
        try { using var _ = await _fx.Http.DeleteAsync($"/fleet/groups/{id}"); } catch { /* best-effort cleanup */ }
    }
}
