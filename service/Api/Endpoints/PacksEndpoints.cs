using System.Net;
using System.Text;
using System.Text.Json;
// Microsoft.Graph.Beta.Models also defines a `WebApplication` type; alias the host
// type so the extension method resolves (see DevicesEndpoints for the rationale).
using WebApplication = Microsoft.AspNetCore.Builder.WebApplication;

namespace CmProjectX.Api;

// M21 Ecosystem — policy packs. A pack is a versioned M15 desired-state repo plus a
// pack.json manifest. Adopting one is exactly the M15 round-trip against the
// operator's tenant, parameter-substituted: bind params → plan (JsonDrift) → gated
// apply. This module does NOT reimplement GitOps — it drives the M15 /gitops/plan and
// /gitops/apply endpoints over the loopback contract (the same in-process HTTP the
// approval-apply path uses). Every object create/update the apply produces flows
// through the normal write pipeline → snapshot-on-write → audit.
//
// The M15 /gitops routes are wired (Program.cs MapGitOps), so /gitops/plan and
// /gitops/apply resolve over loopback; a non-200 from either is a real failure and is
// surfaced as an error PackAdoptResult. PackAdoptResult.RequiresM15 is retained in the
// contract but is always false now the GitOps routes are present.
public static class PacksEndpoints
{
    public static void MapPacks(this WebApplication app)
    {
        // GET /packs — installed/available pack manifests.
        app.MapGet("/packs", () => Results.Ok(EcosystemCatalog.ListPacks()));

        // GET /packs/{id} — one pack's manifest + repo summary.
        app.MapGet("/packs/{id}", (string id) =>
        {
            var pack = EcosystemCatalog.FindPack(id);
            return pack is null ? Results.NotFound() : Results.Ok(pack);
        });

        // POST /packs/{id}/adopt — bind params → M15 plan (confirm:false, default), or
        // apply the reviewed plan (confirm:true + planId). Plan-only is the preview.
        app.MapPost("/packs/{id}/adopt", async (string id, PackAdoptRequest? req, AuthSession auth) =>
        {
            var pack = EcosystemCatalog.FindPack(id);
            if (pack is null) return Results.NotFound();
            // Signed-in check up front so a clear 409 beats a confusing loopback error.
            if (auth.Graph is null)
                return ApiResults.Conflict("not signed in — pack adopt runs the M15 plan against the live tenant");
            if (string.IsNullOrWhiteSpace(pack.RepoPath))
                return Results.Ok(new PackAdoptResult(id, "error", false, "pack has no on-disk repo path"));

            req ??= new PackAdoptRequest(null, false, null, false);
            var (_, missing) = EcosystemCatalog.ResolveParameters(pack.Parameters, req.Parameters);
            if (missing is not null)
                return ApiResults.BadRequest($"missing required parameter '{missing}'");

            // NOTE: M15's MigrationTable resolves GUID-bearing fields and applies the
            // manifest's parameter substitution on import; we surface the bound values
            // to the operator but the substitution itself is M15's job (don't rewrite
            // the export tree here). The surfaces the plan scopes to are the pack's
            // targetSurfaces — a surface the token can't write fails at the M15 gate.
            var surfaces = pack.TargetSurfaces;

            return req.Confirm
                ? await ApplyAsync(id, pack.RepoPath!, req)
                : await PlanAsync(id, pack.RepoPath!, surfaces);
        });
    }

    // POST /gitops/plan over loopback. Graceful when the route is absent (M15 not built).
    private static async Task<IResult> PlanAsync(string packId, string repoPath, IReadOnlyList<string> surfaces)
    {
        var payload = JsonSerializer.Serialize(new { repoPath, surfaces });
        var (status, body) = await Loopback(HttpMethod.Post, "/gitops/plan", payload);

        // /gitops/plan is wired (MapGitOps). A non-200 is a real failure — surface it
        // (status 0 = loopback transport error, e.g. the sidecar busy). RequiresM15 stays
        // false: the GitOps routes are present in this build.
        if (status != HttpStatusCode.OK)
            return Results.Ok(new PackAdoptResult(packId, "error", false,
                Message: $"plan failed ({(int)status}): {Truncate(body)}"));

        // Echo the M15 plan through (planId + summary lifted for convenience).
        var (planId, summary) = ExtractPlan(body);
        return Results.Ok(new PackAdoptResult(
            packId, "plan", RequiresM15: false, Message: null,
            PlanId: planId, Summary: summary, Plan: ParseOrNull(body)));
    }

    // POST /gitops/apply over loopback. Same graceful-absence handling as plan.
    private static async Task<IResult> ApplyAsync(string packId, string repoPath, PackAdoptRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.PlanId))
            return ApiResults.BadRequest("confirm:true requires the planId returned by a prior plan");

        var payload = JsonSerializer.Serialize(new
        {
            repoPath,
            planId = req.PlanId,
            confirm = true,
            confirmDestroy = req.ConfirmDestroy,
        });
        var (status, body) = await Loopback(HttpMethod.Post, "/gitops/apply", payload);

        // /gitops/apply is wired (MapGitOps). A non-200 is a real failure — surface it
        // (status 0 = loopback transport error).
        if (status != HttpStatusCode.OK)
            return Results.Ok(new PackAdoptResult(packId, "error", false,
                Message: $"apply failed ({(int)status}): {Truncate(body)}"));

        return Results.Ok(new PackAdoptResult(
            packId, "apply", RequiresM15: false, Message: null,
            PlanId: req.PlanId, ApplyResult: ParseOrNull(body)));
    }

    // ── loopback + json helpers ───────────────────────────────────────────────

    // Sends an in-process request to the sidecar's own contract. Returns status 0 on a
    // transport failure (so the caller can treat "route missing / sidecar busy" the same
    // graceful way as a 404).
    private static async Task<(HttpStatusCode Status, string Body)> Loopback(HttpMethod method, string url, string json)
    {
        try
        {
            using var msg = new HttpRequestMessage(method, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            using var resp = await CmProjectX.Api.Loopback.Http.SendAsync(msg);
            return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
        }
        catch
        {
            return ((HttpStatusCode)0, "");
        }
    }

    private static (string? PlanId, string? Summary) ExtractPlan(string body)
    {
        try
        {
            using var d = JsonDocument.Parse(body);
            var root = d.RootElement;
            string? planId = root.TryGetProperty("planId", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            string? summary = root.TryGetProperty("summary", out var s) ? s.ToString() : null;
            return (planId, summary);
        }
        catch { return (null, null); }
    }

    private static object? ParseOrNull(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try { return JsonSerializer.Deserialize<JsonElement>(body); }
        catch { return null; }
    }

    private static string Truncate(string s) => s.Length <= 400 ? s : s[..400] + "…";
}
