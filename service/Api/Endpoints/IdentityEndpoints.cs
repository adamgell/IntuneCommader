// Identity group — live Graph endpoints for the Conditional Access / Entra
// identity surfaces, ported from IntuneCommander. Each handler constructs the
// forked Core service with the active GraphServiceClient (AuthSession.Graph) and
// either projects the Graph element type into the normalized ListItemDto row, or
// round-trips the model JSON via CrudJson for detail/CRUD. 409 = signed out.
//
// This file lives outside Program.cs precisely so it MAY name Graph Beta model
// types (TModel for ReadModelAsync<T>); a Graph `using` in Program.cs would
// shadow IResult and break Minimal API lambda overload resolution.
using Microsoft.Graph.Beta.Models;
using Intune.Commander.Core.Services;
// Microsoft.Graph.Beta.Models also defines a `WebApplication` type, which collides
// with the ASP.NET host type used by the extension method. Alias to disambiguate.
using WebApplication = Microsoft.AspNetCore.Builder.WebApplication;

namespace CmProjectX.Api;

public static class IdentityEndpoints
{
    public static void MapIdentity(this WebApplication app)
    {
        // ─── conditional-access — READ ONLY BY DESIGN (list + get; never write) ───
        app.MapGet("/conditional-access", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "ConditionalAccessPolicies", ct => new ConditionalAccessPolicyService(g).ListPoliciesAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.DisplayName ?? "(unnamed policy)",
                CaSummary.ListSubtitle(x),
                x.State?.ToString())));
        });

        app.MapGet("/conditional-access/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "ConditionalAccessPolicies", id, ct => new ConditionalAccessPolicyService(g).GetPolicyAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });

        // ─── conditional-access/pptx — export CA policies to a .pptx (one slide ──
        // per policy, GUIDs resolved to names). Wires the fully-built Core
        // ConditionalAccessPptExportService; binary download mirrors GET /export.
        app.MapGet("/conditional-access/pptx", async (AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var tmp = Path.Combine(Path.GetTempPath(), "cmprojectx-ca-" + Guid.NewGuid().ToString("n") + ".pptx");
            try
            {
                var export = new ConditionalAccessPptExportService(
                    new ConditionalAccessPolicyService(g),
                    new NamedLocationService(g),
                    new AuthenticationStrengthService(g),
                    new AuthenticationContextService(g),
                    new ApplicationService(g),
                    new DirectoryObjectResolver(g),
                    new TermsOfUseService(g));
                await export.ExportAsync(tmp, auth.ActiveProfile?.Name ?? "tenant", ct);
                var bytes = await File.ReadAllBytesAsync(tmp, ct);
                return Results.File(bytes,
                    "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                    $"conditional-access-{DateTime.UtcNow:yyyyMMdd-HHmmss}.pptx");
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort */ }
            }
        });

        // GET /conditional-access/{id}/summary — readable conditions/grant/session,
        // user/app GUIDs resolved to names via one batched directory lookup.
        app.MapGet("/conditional-access/{id}/summary", async (string id, AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await new ConditionalAccessPolicyService(g).GetPolicyAsync(id, ct);
            if (obj is null) return Results.NotFound();
            IReadOnlyDictionary<string, string> names;
            try { names = await new DirectoryObjectResolver(g).ResolveAsync(CaSummary.DirectoryIds(obj), ct); }
            catch { names = new Dictionary<string, string>(); }
            return Results.Ok(CaSummary.Detail(obj, names));
        });

        // GET /conditional-access/list — rich rows for the dedicated CA grid
        // (name, state, summarized users/apps/platforms, grant controls, dates).
        app.MapGet("/conditional-access/list", async (AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await new ConditionalAccessPolicyService(g).ListPoliciesAsync(ct);
            return Results.Ok(items.Select(CaSummary.ListItem));
        });

        // GET /conditional-access/{id}/detail — full include/exclude conditions with
        // user/group/app GUIDs resolved to names (one batched directory lookup).
        app.MapGet("/conditional-access/{id}/detail", async (string id, AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await new ConditionalAccessPolicyService(g).GetPolicyAsync(id, ct);
            if (obj is null) return Results.NotFound();
            IReadOnlyDictionary<string, string> names;
            try { names = await new DirectoryObjectResolver(g).ResolveAsync(CaSummary.DirectoryIds(obj), ct); }
            catch { names = new Dictionary<string, string>(); }
            return Results.Ok(CaSummary.DetailFull(obj, names));
        });

        // ─── named-locations — CRUD (Update rebuilds a subtype-aware patch) ───────
        app.MapGet("/named-locations", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "NamedLocations", ct => new NamedLocationService(g).ListNamedLocationsAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.DisplayName ?? "(unnamed location)",
                x.OdataType?.Split('.').LastOrDefault()?.Replace("microsoftGraph", "") ?? "namedLocation",
                null)));
        });

        app.MapGet("/named-locations/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "NamedLocations", id, ct => new NamedLocationService(g).GetNamedLocationAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });

        app.MapPost("/named-locations", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<NamedLocation>(req, ct);
            var created = await new NamedLocationService(g).CreateNamedLocationAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "NamedLocations");
            return Results.Ok(new { id = created.Id });
        });

        app.MapPatch("/named-locations/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<NamedLocation>(req, ct);
            model.Id = id;
            await new NamedLocationService(g).UpdateNamedLocationAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "NamedLocations", id);
            return Results.NoContent();
        });

        app.MapDelete("/named-locations/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new NamedLocationService(g).DeleteNamedLocationAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "NamedLocations", id);
            return Results.NoContent();
        });

        // ─── auth-strengths — CRUD ────────────────────────────────────────────────
        app.MapGet("/auth-strengths", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "AuthenticationStrengths", ct => new AuthenticationStrengthService(g).ListAuthenticationStrengthPoliciesAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.DisplayName ?? "(unnamed strength)",
                string.Join(" · ", new[]
                {
                    x.RequirementsSatisfied?.ToString(),
                    (x.AllowedCombinations?.Count ?? 0) > 0 ? $"{x.AllowedCombinations!.Count} combination(s)" : null
                }.Where(s => !string.IsNullOrEmpty(s))),
                x.PolicyType?.ToString())));
        });

        app.MapGet("/auth-strengths/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "AuthenticationStrengths", id, ct => new AuthenticationStrengthService(g).GetAuthenticationStrengthPolicyAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });

        app.MapPost("/auth-strengths", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<AuthenticationStrengthPolicy>(req, ct);
            var created = await new AuthenticationStrengthService(g).CreateAuthenticationStrengthPolicyAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "AuthenticationStrengths");
            return Results.Ok(new { id = created.Id });
        });

        app.MapPatch("/auth-strengths/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<AuthenticationStrengthPolicy>(req, ct);
            model.Id = id;
            await new AuthenticationStrengthService(g).UpdateAuthenticationStrengthPolicyAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "AuthenticationStrengths", id);
            return Results.NoContent();
        });

        app.MapDelete("/auth-strengths/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new AuthenticationStrengthService(g).DeleteAuthenticationStrengthPolicyAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "AuthenticationStrengths", id);
            return Results.NoContent();
        });

        // ─── auth-contexts — CRUD ─────────────────────────────────────────────────
        app.MapGet("/auth-contexts", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "AuthenticationContexts", ct => new AuthenticationContextService(g).ListAuthenticationContextsAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.DisplayName ?? "(unnamed context)",
                string.IsNullOrWhiteSpace(x.Description)
                    ? (string.IsNullOrEmpty(x.Id) ? "authenticationContext" : x.Id!)
                    : x.Description!,
                x.IsAvailable == true ? "published" : null)));
        });

        app.MapGet("/auth-contexts/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "AuthenticationContexts", id, ct => new AuthenticationContextService(g).GetAuthenticationContextAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });

        app.MapPost("/auth-contexts", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<AuthenticationContextClassReference>(req, ct);
            var created = await new AuthenticationContextService(g).CreateAuthenticationContextAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "AuthenticationContexts");
            return Results.Ok(new { id = created.Id });
        });

        app.MapPatch("/auth-contexts/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<AuthenticationContextClassReference>(req, ct);
            model.Id = id;
            await new AuthenticationContextService(g).UpdateAuthenticationContextAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "AuthenticationContexts", id);
            return Results.NoContent();
        });

        app.MapDelete("/auth-contexts/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new AuthenticationContextService(g).DeleteAuthenticationContextAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "AuthenticationContexts", id);
            return Results.NoContent();
        });

        // ─── terms-of-use — CRUD (Agreement) ──────────────────────────────────────
        app.MapGet("/terms-of-use", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "TermsOfUseAgreements", ct => new TermsOfUseService(g).ListTermsOfUseAgreementsAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.DisplayName ?? "(unnamed agreement)",
                string.Join(" · ", new[]
                {
                    "agreement",
                    x.IsViewingBeforeAcceptanceRequired == true ? "view required" : null
                }.Where(s => !string.IsNullOrEmpty(s))),
                x.IsPerDeviceAcceptanceRequired == true ? "per-device" : null)));
        });

        app.MapGet("/terms-of-use/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "TermsOfUseAgreements", id, ct => new TermsOfUseService(g).GetTermsOfUseAgreementAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });

        app.MapPost("/terms-of-use", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<Agreement>(req, ct);
            var created = await new TermsOfUseService(g).CreateTermsOfUseAgreementAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "TermsOfUseAgreements");
            return Results.Ok(new { id = created.Id });
        });

        app.MapPatch("/terms-of-use/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<Agreement>(req, ct);
            model.Id = id;
            await new TermsOfUseService(g).UpdateTermsOfUseAgreementAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "TermsOfUseAgreements", id);
            return Results.NoContent();
        });

        app.MapDelete("/terms-of-use/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new TermsOfUseService(g).DeleteTermsOfUseAgreementAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "TermsOfUseAgreements", id);
            return Results.NoContent();
        });
    }
}
