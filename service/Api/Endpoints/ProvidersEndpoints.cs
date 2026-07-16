using CmProjectX.Api.Providers;
// No Microsoft.Graph.Beta using here — this module is Graph-free by construction
// (it only ever touches the shared DTOs through IMdmProvider). The WebApplication
// alias still mirrors the other endpoint modules for consistency.
using WebApplication = Microsoft.AspNetCore.Builder.WebApplication;

namespace CmProjectX.Api;

// ─── M22 spike — provider-dispatch endpoint ───────────────────────────────────
//
// Demonstrates the seam: a single READ surface served by a NON-Intune provider,
// behind the SAME ListItem contract, so the existing client renders it unchanged.
//
// Route shape `/providers/{providerId}/{surface}` is deliberately explicit for the
// spike so the stub can be exercised next to the live Intune endpoints WITHOUT
// touching the active-tenant selection or the persisted profile model. In the full
// M22 this collapses away: the plain `/managed-devices` route dispatches to
// registry.Get(activeProfile.ProviderId).ListAsync(...) instead of newing a Core
// service inline, and `providerId` defaults to "intune" so today's routes are
// unchanged. See docs/part-ii/M22-spike.md.
public static class ProvidersEndpoints
{
    public static void MapProviders(this WebApplication app)
    {
        // NOTE: these spike routes are intentionally UNauthenticated — the stub provider has
        // no real backend or auth adapter, and they read only in-memory fake data (no tenant
        // state). In the full M22 the dispatch collapses into the per-surface routes, which
        // already carry the active-profile auth; the standalone /providers/* routes go away.

        // LIST — the proof. Projects the provider's native inventory into ListItemDto.
        app.MapGet("/providers/{providerId}/{surface}", async (
            string providerId, string surface, IProviderRegistry registry, CancellationToken ct) =>
        {
            var provider = registry.Get(providerId);
            if (provider is null) return ApiResults.NotFound($"no provider '{providerId}'");
            try
            {
                var rows = await provider.ListAsync(surface, ct);
                return Results.Ok(rows);
            }
            // Unsupported surface/verb on this provider → 404, exactly the response the
            // uniform client already tolerates (it treats a missing surface like 409).
            catch (ProviderUnsupportedException ex)
            {
                return ApiResults.NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                // Spike robustness: an unexpected provider error becomes a clean 500 with a
                // message, not a raw unhandled exception (a real provider could throw anything).
                return ApiResults.ServerError($"provider '{providerId}' failed: {ex.Message}");
            }
        });

        // DETAIL — raw provider-native JSON (DETAIL parity is explicitly out of M22 scope).
        app.MapGet("/providers/{providerId}/{surface}/{id}", async (
            string providerId, string surface, string id, IProviderRegistry registry, CancellationToken ct) =>
        {
            var provider = registry.Get(providerId);
            if (provider is null) return ApiResults.NotFound($"no provider '{providerId}'");
            try
            {
                var body = await provider.GetAsync(surface, id, ct);
                return body is null ? Results.NotFound() : Results.Content(body, "application/json");
            }
            catch (ProviderUnsupportedException ex)
            {
                return ApiResults.NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                // Spike robustness: an unexpected provider error becomes a clean 500 with a
                // message, not a raw unhandled exception (a real provider could throw anything).
                return ApiResults.ServerError($"provider '{providerId}' failed: {ex.Message}");
            }
        });

        // PROVIDERS catalog — what's registered + each one's supported surfaces.
        // Lets the (future) nav filter gray-out unsupported surfaces per active provider.
        app.MapGet("/providers", (IProviderRegistry registry) =>
            Results.Ok(registry.All
                .Select(p => new MdmProviderDescriptorDto(p.Id, p.SupportedSurfaces.Select(s => s.Key).ToArray()))
                .ToList()));
    }
}
