using System.Text.Json;
using CmProjectX.Store;
// This module touches no Microsoft.Graph.Beta.Models types, so it needs no
// WebApplication alias — but follow the data-driven-test precedent of /gitops +
// /simulate: autonomy is a CONTROL surface, intentionally NOT registered in
// Surfaces.cs / EndpointInventory (those drive the list/detail contract tests).

namespace CmProjectX.Api;

// M18 Autonomy — the control surface for the closed-loop AI SRE:
//   GET/PUT /autonomy/policy  — which signals may auto-ENQUEUE (never auto-apply)
//   GET     /autonomy/runs    — the loop history (newest first)
//   GET     /autonomy/runs/{id} — one run's full watch→…→verify chain
// The loop itself runs in the AutonomyScheduler (service/Sync) + AutonomyEngine
// (service/Api/Autonomy). These endpoints only read/write its policy + history.
public static class AutonomyEndpoints
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static void MapAutonomy(this Microsoft.AspNetCore.Builder.WebApplication app)
    {
        // GET /autonomy/policy — the active tenant's policy (or safe defaults). 409 when
        // signed out (no tenant to scope to).
        app.MapGet("/autonomy/policy", async (AuthSession auth, ISnapshotStore store, CancellationToken ct) =>
        {
            var tenantId = auth.ActiveProfile?.TenantId;
            if (tenantId is null) return Results.Conflict();
            return Results.Ok(await AutonomyPolicyStore.GetAsync(store, tenantId, ct));
        });

        // PUT /autonomy/policy — edit enqueue-eligibility (the tenantId is stamped
        // server-side from the active profile, never trusted from the body). LOCKED:
        // no field here can auto-apply a write.
        app.MapPut("/autonomy/policy", async (AutonomyPolicyDto policy, AuthSession auth, ISnapshotStore store, CancellationToken ct) =>
        {
            var tenantId = auth.ActiveProfile?.TenantId;
            if (tenantId is null) return Results.Conflict();
            return Results.Ok(await AutonomyPolicyStore.SetAsync(store, tenantId, policy, ct));
        });

        // GET /autonomy/runs — the loop history (the stored AutonomyRunDto chain).
        app.MapGet("/autonomy/runs", async (int? limit, ISnapshotStore store, CancellationToken ct) =>
        {
            var rows = await store.GetAutonomyRunsAsync(limit is > 0 ? limit.Value : 50, ct);
            var runs = rows.Select(r => Deserialize(r)).Where(r => r is not null).Select(r => r!);
            return Results.Ok(runs);
        });

        // GET /autonomy/runs/{id} — one run with its full chain.
        app.MapGet("/autonomy/runs/{id}", async (string id, ISnapshotStore store, CancellationToken ct) =>
        {
            var rec = await store.GetAutonomyRunAsync(id, ct);
            if (rec is null) return Results.NotFound();
            var run = Deserialize(rec);
            return run is null ? Results.NotFound() : Results.Ok(run);
        });
    }

    private static AutonomyRunDto? Deserialize(AutonomyRunRecord rec)
    {
        try { return JsonSerializer.Deserialize<AutonomyRunDto>(rec.RunJson, Web); }
        catch { return null; }
    }
}
