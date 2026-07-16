using Microsoft.Graph.Beta.Models;
using Intune.Commander.Core.Services;
// `Microsoft.Graph.Beta.Models` also defines a `WebApplication` type, which collides
// with the ASP.NET host type in the `this WebApplication app` extension signature
// (CS0104). Alias the host type so it resolves unambiguously in this module.
using WebApplication = Microsoft.AspNetCore.Builder.WebApplication;

namespace CmProjectX.Api;

// "Apps" endpoint group — ported IntuneCommander app surfaces, served live off the
// active Graph sign-in. Lives here (not Program.cs) so it can name Graph
// `Microsoft.Graph.Beta.Models` types: a Graph `using` in Program.cs shadows IResult
// and breaks Minimal API lambda overload resolution.
//
//   apps          ApplicationService                 READ  (list + get; content-upload flow, no CRUD)
//   app-protection AppProtectionPolicyService        CRUD
//   app-configs   ManagedAppConfigurationService     CRUD  (ManagedDeviceMobileAppConfiguration)
//   vpp-tokens    VppTokenService                    READ  (list + get)
//
// 409 (Conflict) = signed out (auth.Graph is null) so the client shows a "sign in"
// empty state.
public static class AppsEndpoints
{
    public static void MapApps(this WebApplication app)
    {
        // ─── apps (READ) — polymorphic MobileApp; reuse the ported appType/platform
        // projection so the rows match what users saw in the original app. ──────────
        app.MapGet("/apps", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "Applications", ct => new ApplicationService(g).ListApplicationsAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.DisplayName ?? "(unnamed)",
                string.Join(" · ", new[]
                {
                    ApplicationMapper.FormatAppType(x),
                    ApplicationMapper.DetectPlatform(x),
                    string.IsNullOrEmpty(x.Publisher) ? null : x.Publisher
                }.Where(s => !string.IsNullOrEmpty(s))),
                (x.IsAssigned ?? false) ? "assigned" : null)));
        });

        app.MapGet("/apps/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "Applications", id, ct => new ApplicationService(g).GetApplicationAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });

        // ─── app-protection (CRUD) — ManagedAppPolicy ───────────────────────────────
        app.MapGet("/app-protection", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "AppProtectionPolicies", ct => new AppProtectionPolicyService(g).ListAppProtectionPoliciesAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.DisplayName ?? "(unnamed)",
                string.Join(" · ", new[]
                {
                    x.OdataType?.Split('.').LastOrDefault()?.Replace("microsoftGraph", "") ?? "managedAppPolicy",
                    x.LastModifiedDateTime is { } lm ? lm.ToString("yyyy-MM-dd") : null
                }.Where(s => !string.IsNullOrEmpty(s))),
                x.Version is { } v && !string.IsNullOrEmpty(v) ? "v" + v : null,
                ListProjection.PlatformOf(x.OdataType),
                x.LastModifiedDateTime?.ToString("yyyy-MM-dd"))));
        });

        app.MapGet("/app-protection/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "AppProtectionPolicies", id, ct => new AppProtectionPolicyService(g).GetAppProtectionPolicyAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });

        app.MapPost("/app-protection", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<ManagedAppPolicy>(req, ct);
            var created = await new AppProtectionPolicyService(g).CreateAppProtectionPolicyAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "AppProtectionPolicies");
            return Results.Ok(new { id = created.Id });
        });

        app.MapPatch("/app-protection/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<ManagedAppPolicy>(req, ct);
            model.Id = id;
            await new AppProtectionPolicyService(g).UpdateAppProtectionPolicyAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "AppProtectionPolicies", id);
            return Results.NoContent();
        });

        app.MapDelete("/app-protection/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new AppProtectionPolicyService(g).DeleteAppProtectionPolicyAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "AppProtectionPolicies", id);
            return Results.NoContent();
        });

        // ─── app-configs (CRUD) — ManagedDeviceMobileAppConfiguration ───────────────
        app.MapGet("/app-configs", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "ManagedDeviceAppConfigurations", ct => new ManagedAppConfigurationService(g).ListManagedDeviceAppConfigurationsAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.DisplayName ?? "(unnamed)",
                string.Join(" · ", new[]
                {
                    x.OdataType?.Split('.').LastOrDefault()?.Replace("microsoftGraph", "") ?? "appConfiguration",
                    (x.TargetedMobileApps?.Count ?? 0) > 0 ? $"{x.TargetedMobileApps!.Count} app(s)" : null,
                    x.LastModifiedDateTime is { } lm ? lm.ToString("yyyy-MM-dd") : null
                }.Where(s => !string.IsNullOrEmpty(s))),
                x.Version is { } v ? "v" + v : null,
                ListProjection.PlatformOf(x.OdataType),
                x.LastModifiedDateTime?.ToString("yyyy-MM-dd"))));
        });

        app.MapGet("/app-configs/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "ManagedDeviceAppConfigurations", id, ct => new ManagedAppConfigurationService(g).GetManagedDeviceAppConfigurationAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });

        app.MapPost("/app-configs", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<ManagedDeviceMobileAppConfiguration>(req, ct);
            var created = await new ManagedAppConfigurationService(g).CreateManagedDeviceAppConfigurationAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "ManagedDeviceAppConfigurations");
            return Results.Ok(new { id = created.Id });
        });

        app.MapPatch("/app-configs/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<ManagedDeviceMobileAppConfiguration>(req, ct);
            model.Id = id;
            await new ManagedAppConfigurationService(g).UpdateManagedDeviceAppConfigurationAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "ManagedDeviceAppConfigurations", id);
            return Results.NoContent();
        });

        app.MapDelete("/app-configs/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new ManagedAppConfigurationService(g).DeleteManagedDeviceAppConfigurationAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "ManagedDeviceAppConfigurations", id);
            return Results.NoContent();
        });

        // ─── vpp-tokens (READ) — VppToken ───────────────────────────────────────────
        app.MapGet("/vpp-tokens", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "VppTokens", ct => new VppTokenService(g).ListVppTokensAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                !string.IsNullOrEmpty(x.OrganizationName) ? x.OrganizationName!
                    : !string.IsNullOrEmpty(x.DisplayName) ? x.DisplayName!
                    : !string.IsNullOrEmpty(x.AppleId) ? x.AppleId! : "(unnamed)",
                string.Join(" · ", new[]
                {
                    x.VppTokenAccountType?.ToString(),
                    x.ExpirationDateTime is { } exp ? "expires " + exp.ToString("yyyy-MM-dd") : null
                }.Where(s => !string.IsNullOrEmpty(s))),
                x.State?.ToString())));
        });

        app.MapGet("/vpp-tokens/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "VppTokens", id, ct => new VppTokenService(g).GetVppTokenAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });

        // ─── apps assignments (Intune) — MobileAppAssignment carries install Intent ──
        app.MapGet("/apps/{id}/assignments", async (string id, AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var asgs = await new ApplicationService(g).GetAssignmentsAsync(id, ct);
            var rows = asgs.Select(a => Assignments.ReadTarget(a.Target, a.Intent?.ToString())).ToList();
            return Results.Ok(await Assignments.ResolveNamesAsync(rows, g, ct));
        });

        app.MapPost("/apps/{id}/assignments", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var dtos = await req.ReadFromJsonAsync<List<AssignmentDto>>(cancellationToken: ct) ?? new();
            var typed = dtos.Select(d => new MobileAppAssignment
            {
                Target = Assignments.BuildTarget(d),
                Intent = Assignments.ParseIntent(d.Intent)
            }).ToList();
            await new ApplicationService(g).AssignApplicationAsync(id, typed, ct);
            CacheInvalidation.OnWrite(cache, auth, "Applications");
            return Results.NoContent();
        });

        // POST /apps/assign — apply the SAME Assignment[] to many apps (replace-all per
        // app). dryRun (default true) previews without writing; a live run loops each app
        // in its own try/catch so one failure never aborts the batch, then evicts once.
        app.MapPost("/apps/assign", async (BulkAssignRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            if (req.AppIds is null || req.AppIds.Count == 0)
                return ApiResults.BadRequest("appIds is required");

            var typed = (req.Assignments ?? new List<AssignmentDto>()).Select(d => new MobileAppAssignment
            {
                Target = Assignments.BuildTarget(d),
                Intent = Assignments.ParseIntent(d.Intent)
            }).ToList();

            var dryRun = req.DryRun ?? true;
            var svc = new ApplicationService(g);
            var results = new List<BulkAssignItemResult>();
            foreach (var appId in req.AppIds)
            {
                if (dryRun) { results.Add(new(appId, true, null)); continue; }
                try
                {
                    await svc.AssignApplicationAsync(appId, typed, ct);
                    results.Add(new(appId, true, null));
                }
                catch (Exception ex) { results.Add(new(appId, false, ex.Message)); }
            }
            if (!dryRun) CacheInvalidation.OnWrite(cache, auth, "Applications");
            return Results.Ok(new BulkAssignResult(results));
        });
    }
}
