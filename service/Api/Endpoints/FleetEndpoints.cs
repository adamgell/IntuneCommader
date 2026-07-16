using System.Text.Json;
using System.Text.Json.Nodes;
using CmProjectX.Store;
// Microsoft.Graph.Beta defines a `WebApplication` type that collides with the ASP.NET
// host type used by the extension-method signature (CS0104); alias the host type, and
// fully-qualify the Graph client (see SecurityPostureEndpoints/DashboardEndpoints).
using WebApplication = Microsoft.AspNetCore.Builder.WebApplication;
using GraphServiceClient = Microsoft.Graph.Beta.GraphServiceClient;

namespace CmProjectX.Api;

// M20 Fleet — MSP-scale multi-tenant fan-out (Pattern G; docs/part-ii/M20-fleet.md).
// All routes are net-new /fleet/* ACTION/read endpoints (not in Surfaces.cs /
// EndpointInventory.cs — those drive the per-tenant MCP/list contract tests). The
// fan-out plane reuses: FleetSession (N concurrent Graph clients via the factory),
// FleetSurfaceReader (the same single-tenant LIST projections), GoldenResolver (the
// layered merge), JsonDrift (the same /preview-diff engine), and the M13 inbox
// (AppendPendingChangeAsync) as the gate for every per-tenant write — N gated replays,
// never one bulk call.
public static class FleetEndpoints
{
    // Bounded fan-out parallelism so a campaign to many tenants under one clientId
    // doesn't trip Graph's app-wide throttle (an open question in the design; a small,
    // safe default for Phase 1 stored-profile fan-out).
    private const int MaxDegreeOfParallelism = 4;

    public static void MapFleet(this WebApplication app)
    {
        // GET /fleet/tenants — known profiles + which have a live fleet session.
        app.MapGet("/fleet/tenants", (FleetSession fleet) =>
        {
            var rows = fleet.Profiles.Select(p => new FleetTenantDto(
                p.TenantId, p.Id, p.Name,
                fleet.IsSignedIn(p.TenantId) ? "SignedIn" : "SignedOut",
                fleet.IsSignedIn(p.TenantId)));
            return Results.Ok(rows);
        });

        // ─── Tenant groups (saved fan-out targets) ──────────────────────────────
        app.MapGet("/fleet/groups", (FleetStore store) => Results.Ok(store.ListGroups()));

        app.MapPost("/fleet/groups", (TenantGroupDto group, FleetStore store) =>
        {
            if (string.IsNullOrWhiteSpace(group.Name) || group.TenantIds is null || group.TenantIds.Count == 0)
                return ApiResults.BadRequest("name and at least one tenantId are required");
            return Results.Ok(store.UpsertGroup(group));
        });

        app.MapDelete("/fleet/groups/{id}", (string id, FleetStore store) =>
            store.DeleteGroup(id) ? Results.NoContent() : Results.NotFound());

        // ─── Golden templates + override layers ─────────────────────────────────
        app.MapGet("/fleet/templates", (FleetStore store) => Results.Ok(store.ListTemplates()));

        app.MapPost("/fleet/templates", (GoldenTemplateDto template, FleetStore store) =>
        {
            if (string.IsNullOrWhiteSpace(template.Id) || string.IsNullOrWhiteSpace(template.Surface)
                || string.IsNullOrWhiteSpace(template.ObjectName))
                return ApiResults.BadRequest("id, surface and objectName are required");
            return Results.Ok(store.UpsertTemplate(template));
        });

        // ─── GET /fleet/list/{surface}?group= — tenant-tagged fan-out LIST ───────
        app.MapGet("/fleet/list/{surface}", async (string surface, string? group, FleetSession fleet, FleetStore store, CancellationToken ct) =>
        {
            if (!FleetSurfaceReader.IsSupported(surface)) return ApiResults.NotFound($"unknown surface '{surface}'");
            var grp = group is null ? null : store.GetGroup(group);
            if (grp is null) return ApiResults.NotFound("group not found");

            var statuses = new List<FleetTenantStatusDto>();
            var items = new List<FleetListItemDto>();
            var errors = new List<FleetErrorDto>();

            await FanOut(grp.TenantIds, fleet, async (tenantId, tenantName) =>
            {
                try
                {
                    var g = await fleet.EnsureAsync(tenantId, ct);
                    var rows = await FleetSurfaceReader.ListAsync(g, surface, ct);
                    lock (items)
                    {
                        statuses.Add(new FleetTenantStatusDto(tenantId, tenantName, "ok"));
                        items.AddRange(rows.Select(r => new FleetListItemDto(
                            r.Id, r.Title, r.Subtitle, r.Badge, r.Platform, r.Modified, tenantId, tenantName)));
                    }
                }
                catch (Exception ex)
                {
                    var (status, code) = Classify(ex);
                    lock (items)
                    {
                        statuses.Add(new FleetTenantStatusDto(tenantId, tenantName, status));
                        errors.Add(new FleetErrorDto(tenantId, code, ex.Message));
                    }
                }
            });

            return Results.Ok(new FleetListResponseDto(
                NormalizeSurface(surface), grp.Id,
                statuses.OrderBy(s => s.TenantName, StringComparer.Ordinal).ToList(),
                items, errors));
        });

        // ─── POST /fleet/campaign — broadcast a baseline, gated per target ──────
        app.MapPost("/fleet/campaign", async (CampaignRequestDto req, FleetSession fleet, FleetStore store, ISnapshotStore snap, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Group) || string.IsNullOrWhiteSpace(req.Surface) || string.IsNullOrWhiteSpace(req.ObjectName))
                return ApiResults.BadRequest("group, surface and objectName are required");
            if (!FleetSurfaceReader.IsSupported(req.Surface))
                return ApiResults.BadRequest($"unknown surface '{req.Surface}'");

            var grp = store.GetGroup(req.Group);
            if (grp is null) return ApiResults.NotFound("group not found");

            var surfacePath = FleetSurfaceReader.SurfacePath(req.Surface)!;
            var dryRun = req.DryRun ?? true;
            var campaignId = string.IsNullOrWhiteSpace(req.CampaignId)
                ? $"camp-{DateTime.UtcNow:yyyyMMddHHmmss}-{req.Surface}" : req.CampaignId!;

            // 1) Resolve the golden BASELINE once. goldenTenant → read the named object
            //    from the canonical tenant; goldenRepo (M15) is deferred.
            var goldenTenantId = req.Golden?.TenantId ?? grp.GoldenTenantId;
            string? baselineJson = null;
            string? baselineErr = null;
            if (req.Golden?.Kind == "goldenRepo")
            {
                baselineErr = "goldenRepo baseline (M15 GitOps) is deferred — use kind=goldenTenant";
            }
            else if (goldenTenantId is null)
            {
                baselineErr = "no goldenTenantId on the request or the group";
            }
            else
            {
                try
                {
                    var gg = await fleet.EnsureAsync(goldenTenantId, ct);
                    var found = await FleetSurfaceReader.FindByNameAsync(gg, req.Surface, req.ObjectName, ct);
                    if (found is null) baselineErr = $"golden object '{req.ObjectName}' not found in the golden tenant";
                    else baselineJson = found.Value.BodyJson;
                }
                catch (Exception ex) { baselineErr = $"golden read failed: {ex.Message}"; }
            }

            // Load the STORED golden template governing this (surface, objectName), if any.
            // Its persisted group/tenant override layers (fleet-templates.json) are folded
            // into every target's effective golden alongside the inline req.Overrides — so an
            // accepted per-tenant override the operator saved once is honored on every campaign,
            // not only when re-supplied inline. (Previously only inline overrides were consumed.)
            var template = FindTemplate(store, req.Surface, req.ObjectName);
            var results = new List<CampaignTenantResultDto>();

            await FanOut(grp.TenantIds, fleet, async (tenantId, tenantName) =>
            {
                // The canonical tenant is the source of truth — never write to it.
                if (string.Equals(tenantId, goldenTenantId, StringComparison.OrdinalIgnoreCase))
                {
                    AddResult(results, new CampaignTenantResultDto(
                        tenantId, tenantName, "skip", "goldenSource — no write to canonical tenant",
                        null, null, [], []));
                    return;
                }
                if (baselineJson is null)
                {
                    AddResult(results, new CampaignTenantResultDto(
                        tenantId, tenantName, "error", baselineErr ?? "no golden baseline", null, null, [], []));
                    return;
                }

                try
                {
                    var g = await fleet.EnsureAsync(tenantId, ct);
                    var live = await FleetSurfaceReader.FindByNameAsync(g, req.Surface, req.ObjectName, ct);
                    if (live is null)
                    {
                        AddResult(results, new CampaignTenantResultDto(
                            tenantId, tenantName, "skip", $"target has no object named '{req.ObjectName}'",
                            null, null, [], []));
                        return;
                    }

                    // effective = golden ⊕ template.groupOverride ⊕ template.tenantOverride
                    //                    ⊕ inline.groupOverride ⊕ inline.tenantOverride.
                    var layers = GoldenResolver.LayersFor(template, req.Overrides, grp.Id, tenantId);
                    var effective = GoldenResolver.Resolve(baselineJson, layers);

                    // Field-level diff of effective vs the target's live object (the same
                    // engine /preview-diff uses) — this is the per-tenant change set.
                    var diff = JsonDrift.Diff(live.Value.BodyJson, effective);
                    var appliedFields = diff.Select(c => c.Path).ToList();

                    if (diff.Count == 0)
                    {
                        AddResult(results, new CampaignTenantResultDto(
                            tenantId, tenantName, "skip", "already matches the resolved golden",
                            null, live.Value.Id, [], []));
                        return;
                    }

                    if (dryRun)
                    {
                        // Resolve + diff only; apply nothing.
                        AddResult(results, new CampaignTenantResultDto(
                            tenantId, tenantName, "success", "dryRun — diff computed, nothing applied",
                            null, live.Value.Id, appliedFields, diff));
                        return;
                    }

                    // GATE: enqueue ONE gated M13 replay for this target (POST
                    // /pending-changes). Each target is a separate approval — N gates,
                    // not one. Apply (and the per-tenant snapshot + audit) happens when
                    // the operator approves it in the inbox; the campaign reports `pending`.
                    var pendingId = Guid.NewGuid().ToString("n");
                    // Bind the proposal to THIS target tenant — the inbox refuses to apply
                    // it unless the operator is signed into the same tenant (M20 isolation).
                    await snap.AppendPendingChangeAsync(new PendingChangeRecord(
                        pendingId,
                        $"fleet campaign {campaignId} (tenant {tenantName})",
                        "update", surfacePath, live.Value.Id, req.ObjectName,
                        effective, JsonSerializer.Serialize(diff, Web),
                        "pending", DateTime.UtcNow, TenantId: tenantId), ct);

                    AddResult(results, new CampaignTenantResultDto(
                        tenantId, tenantName, "pending", "gated through the pending-changes inbox",
                        pendingId, live.Value.Id, appliedFields, diff));
                }
                catch (Exception ex)
                {
                    // A Graph 429 is a retryable throttle, NOT an etag/state conflict — report
                    // it as an error with a clear message rather than mislabeling it "conflict".
                    var (_, code) = Classify(ex);
                    AddResult(results, new CampaignTenantResultDto(
                        tenantId, tenantName, "error",
                        code == "throttled" ? $"throttled (retryable): {ex.Message}" : ex.Message,
                        null, null, [], []));
                }
            });

            var ordered = results.OrderBy(r => r.TenantName, StringComparer.Ordinal).ToList();
            var result = new CampaignResultDto(campaignId, NormalizeSurface(req.Surface), req.ObjectName, dryRun,
                ordered, Summarize(ordered));
            store.SaveCampaign(result); // persist so /fleet/campaign/{id} can poll it
            return Results.Ok(result);
        });

        // GET /fleet/campaign/{id} — poll a campaign's per-tenant outcomes.
        app.MapGet("/fleet/campaign/{id}", (string id, FleetStore store) =>
        {
            var c = store.GetCampaign(id);
            return c is null ? Results.NotFound() : Results.Ok(c);
        });

        // ─── GET /fleet/drift — per-tenant divergence from the resolved golden ──
        app.MapGet("/fleet/drift", async (string? group, string? surface, string? objectName, string? golden, FleetSession fleet, FleetStore store, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(surface) || !FleetSurfaceReader.IsSupported(surface))
                return ApiResults.NotFound($"unknown surface '{surface}'");
            if (string.IsNullOrWhiteSpace(objectName))
                return ApiResults.BadRequest("objectName is required");
            var grp = group is null ? null : store.GetGroup(group);
            if (grp is null) return ApiResults.NotFound("group not found");

            var goldenTenantId = string.IsNullOrWhiteSpace(golden) ? grp.GoldenTenantId : golden;

            // The stored golden template governing (surface, objectName) — its persisted
            // group/tenant override layers make accepted overrides NOT count as drift.
            var template = FindTemplate(store, surface, objectName);

            string? baselineJson = null;
            if (goldenTenantId is not null)
            {
                try
                {
                    var gg = await fleet.EnsureAsync(goldenTenantId, ct);
                    var found = await FleetSurfaceReader.FindByNameAsync(gg, surface, objectName, ct);
                    baselineJson = found?.BodyJson;
                }
                catch { /* golden unreachable — every tenant reports error below */ }
            }

            var tenants = new List<FleetDriftTenantDto>();
            await FanOut(grp.TenantIds, fleet, async (tenantId, tenantName) =>
            {
                if (string.Equals(tenantId, goldenTenantId, StringComparison.OrdinalIgnoreCase))
                {
                    AddDrift(tenants, new FleetDriftTenantDto(tenantId, tenantName, "golden", 0, null, []));
                    return;
                }
                if (baselineJson is null)
                {
                    AddDrift(tenants, new FleetDriftTenantDto(tenantId, tenantName, "error", 0, "golden baseline unavailable", []));
                    return;
                }
                try
                {
                    var g = await fleet.EnsureAsync(tenantId, ct);
                    var live = await FleetSurfaceReader.FindByNameAsync(g, surface, objectName, ct);
                    if (live is null)
                    {
                        AddDrift(tenants, new FleetDriftTenantDto(tenantId, tenantName, "diverged", 1,
                            "object missing in this tenant", []));
                        return;
                    }
                    // Effective golden = golden ⊕ template.groupOverride ⊕ template.tenantOverride.
                    // Accepted overrides (stored on the template) are folded in, so they read as
                    // inSync, not drift (drift has no inline overrides — those are a campaign concept).
                    var layers = GoldenResolver.LayersFor(template, null, grp.Id, tenantId);
                    var effective = GoldenResolver.Resolve(baselineJson, layers);
                    var diff = JsonDrift.Diff(effective, live.Value.BodyJson);
                    if (diff.Count == 0)
                    {
                        AddDrift(tenants, new FleetDriftTenantDto(tenantId, tenantName, "inSync", 0, null, []));
                    }
                    else
                    {
                        var fields = diff.Select(c => new FleetDriftFieldDto(c.Path, c.Before, c.After)).ToList();
                        AddDrift(tenants, new FleetDriftTenantDto(tenantId, tenantName, "diverged", fields.Count, null, fields));
                    }
                }
                catch (Exception ex)
                {
                    AddDrift(tenants, new FleetDriftTenantDto(tenantId, tenantName, "error", 0, ex.Message, []));
                }
            });

            var ordered = tenants.OrderBy(t => t.TenantName, StringComparer.Ordinal).ToList();
            var summary = new FleetDriftSummaryDto(
                ordered.Count(t => t.Status == "golden"),
                ordered.Count(t => t.Status == "inSync"),
                ordered.Count(t => t.Status == "diverged"),
                ordered.Count(t => t.Status == "error"));
            return Results.Ok(new FleetDriftResponseDto(grp.Id, goldenTenantId, NormalizeSurface(surface), objectName, ordered, summary));
        });

        // ─── GET /fleet/posture — per-tenant posture score + fleet roll-up ─────
        app.MapGet("/fleet/posture", async (string? group, FleetSession fleet, FleetStore store, CancellationToken ct) =>
        {
            var grp = group is null ? null : store.GetGroup(group);
            if (grp is null) return ApiResults.NotFound("group not found");

            var tenants = new List<FleetPostureTenantDto>();
            await FanOut(grp.TenantIds, fleet, async (tenantId, tenantName) =>
            {
                try
                {
                    var g = await fleet.EnsureAsync(tenantId, ct);
                    var score = await ComputePostureScoreAsync(g, ct);
                    AddPosture(tenants, new FleetPostureTenantDto(tenantId, tenantName, "ok", score));
                }
                catch (Exception ex)
                {
                    var (status, _) = Classify(ex);
                    AddPosture(tenants, new FleetPostureTenantDto(
                        tenantId, tenantName, status == "signedOut" ? "signedOut" : "error", null));
                }
            });

            var ordered = tenants.OrderBy(t => t.TenantName, StringComparer.Ordinal).ToList();
            var scored = ordered.Where(t => t.Score is not null).Select(t => t.Score!.Value).ToList();
            var avg = scored.Count == 0 ? 0 : (int)Math.Round(scored.Average());
            return Results.Ok(new FleetPostureResponseDto(grp.Id, avg, scored.Count, ordered));
        });
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // Bounded-parallelism fan-out over a tenant set; each tenant's work is isolated so
    // one tenant's failure never aborts the fleet (the body must catch its own errors).
    private static async Task FanOut(
        IReadOnlyList<string> tenantIds, FleetSession fleet, Func<string, string, Task> body)
    {
        using var gate = new SemaphoreSlim(MaxDegreeOfParallelism);
        // Null-safe: a group loaded with a null TenantIds (malformed persistence) must
        // fan out over nothing, not NRE the whole endpoint.
        var tasks = (tenantIds ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).Select(async tenantId =>
        {
            var name = fleet.ProfileForTenant(tenantId)?.Name ?? tenantId;
            await gate.WaitAsync();
            try { await body(tenantId, name); }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);
    }

    private static void AddResult(List<CampaignTenantResultDto> list, CampaignTenantResultDto r)
    {
        lock (list) list.Add(r);
    }

    private static void AddDrift(List<FleetDriftTenantDto> list, FleetDriftTenantDto r)
    {
        lock (list) list.Add(r);
    }

    private static void AddPosture(List<FleetPostureTenantDto> list, FleetPostureTenantDto r)
    {
        lock (list) list.Add(r);
    }

    // Map an exception to a per-tenant (status, code). A Graph 429 is reported as that
    // tenant's throttle, never a fleet abort.
    private static (string Status, string Code) Classify(Exception ex)
    {
        var msg = ex.Message ?? "";
        if (msg.Contains("429") || msg.Contains("throttl", StringComparison.OrdinalIgnoreCase) || msg.Contains("TooManyRequests"))
            return ("throttled", "throttled");
        if (ex is InvalidOperationException && msg.Contains("No saved profile"))
            return ("signedOut", "noProfile");
        return ("error", "error");
    }

    private static CampaignSummaryDto Summarize(IReadOnlyList<CampaignTenantResultDto> results) => new(
        results.Count(r => r.Outcome == "success"),
        results.Count(r => r.Outcome == "skip"),
        results.Count(r => r.Outcome == "conflict"),
        results.Count(r => r.Outcome == "error"),
        results.Count(r => r.Outcome == "pending"),
        results.Count);

    private static string NormalizeSurface(string surface) => surface.Trim().TrimStart('/').ToLowerInvariant();

    // The stored golden template governing (surface, objectName), or null. There is no
    // templateId on the campaign/drift request — a template is defined per {surface,
    // objectName}, so that pair is the natural join key. Matched on the normalized surface
    // + case-insensitive objectName; first match wins (upsert keeps at most one per id).
    private static GoldenTemplateDto? FindTemplate(FleetStore store, string surface, string objectName)
    {
        var norm = NormalizeSurface(surface);
        var name = objectName.Trim();
        return store.ListTemplates().FirstOrDefault(t =>
            NormalizeSurface(t.Surface) == norm &&
            string.Equals(t.ObjectName?.Trim(), name, StringComparison.OrdinalIgnoreCase));
    }

    // A lightweight per-tenant posture score (0-100), reused for the fleet roll-up. A
    // compact port of the single-tenant weighting in SecurityPostureEndpoints so fleet
    // posture is comparable across tenants without re-fetching the full breakdown.
    private static async Task<int> ComputePostureScoreAsync(GraphServiceClient g, CancellationToken ct)
    {
        var caTask = new Intune.Commander.Core.Services.ConditionalAccessPolicyService(g).ListPoliciesAsync(ct);
        var compTask = new Intune.Commander.Core.Services.CompliancePolicyService(g).ListCompliancePoliciesAsync(ct);
        var endpTask = new Intune.Commander.Core.Services.EndpointSecurityService(g).ListEndpointSecurityIntentsAsync(ct);
        var appTask = new Intune.Commander.Core.Services.AppProtectionPolicyService(g).ListAppProtectionPoliciesAsync(ct);
        await Task.WhenAll(caTask, compTask, endpTask, appTask);

        var ca = await caTask;
        var comp = await compTask;
        var endp = await endpTask;
        var appp = await appTask;

        var caEnabled = ca.Count(p => p.State == Microsoft.Graph.Beta.Models.ConditionalAccessPolicyState.Enabled);
        var caReport = ca.Count(p => p.State == Microsoft.Graph.Beta.Models.ConditionalAccessPolicyState.EnabledForReportingButNotEnforced);
        var caScore = Math.Min(30, caEnabled * 5 + caReport * 2);
        var compScore = Math.Min(25, comp.Count * 4);
        var endpScore = Math.Min(20, endp.Count * 5);
        var appScore = Math.Min(15, appp.Count * 5);
        // Remaining 10 points (auth strengths / named locations) omitted from the
        // lightweight roll-up; the single-tenant /security-posture/summary has the full split.
        return caScore + compScore + endpScore + appScore;
    }
}
