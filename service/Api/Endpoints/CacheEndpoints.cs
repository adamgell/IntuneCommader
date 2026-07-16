using Intune.Commander.Core.Services;
// Microsoft.Graph.Beta.Models defines a `WebApplication` type that collides with the
// ASP.NET host type used by the extension-method signature; alias the host type so
// `this WebApplication app` resolves (mirrors DashboardEndpoints / BulkEndpoints).
using WebApplication = Microsoft.AspNetCore.Builder.WebApplication;

namespace CmProjectX.Api;

// M12.1 cache-dev (Cache Sync) — the dev/admin face of the LiteDB read-through blob
// cache (docs/CACHE-M12.1.md). Inspect per-key cache state (GET /cache), warm it
// (POST /cache/warm) and evict it (POST /cache/evict). Read tenant from
// auth.ActiveProfile.TenantId; 409 when signed out (auth.Graph null) — never read or
// evict another tenant's keys.
//
// Only LIST keys are enumerable: DETAIL keys ({cacheKey}/{id}) are lazy-only and not
// listed by ICacheService, so the inspect grid shows LIST-key metadata only.
public static class CacheEndpoints
{
    // The warm-ahead set — the LIST keys PrefetchAllToCacheAsync warms before first
    // use. Kept byte-for-byte in lockstep with AssignmentCheckerService's
    // AllFetchersAreCached key list (minus the two checker-internal group keys, which
    // are not Surfaces.All rows). A surface whose CacheKey is in this set fills ahead
    // of time; the rest fill lazily on first online GET.
    private static readonly HashSet<string> WarmAheadKeys = new(StringComparer.Ordinal)
    {
        "DeviceConfigurations", "SettingsCatalog", "AdministrativeTemplates",
        "CompliancePolicies", "AppProtectionPolicies", "ManagedDeviceAppConfigurations",
        "Applications", "DeviceManagementScripts", "DeviceHealthScripts",
        "EndpointSecurityIntents", "EnrollmentConfigurations", "ConditionalAccessPolicies",
        "AssignmentFilters", "PolicySets", "TermsAndConditions", "ScopeTags",
        "RoleDefinitions", "IntuneBrandingProfiles", "AzureBrandingLocalizations",
        "AutopilotProfiles", "MacCustomAttributes", "FeatureUpdateProfiles",
        "DeviceShellScripts", "ComplianceScripts", "NamedLocations",
        "AuthenticationStrengths", "AuthenticationContexts", "TermsOfUseAgreements",
        "TargetedManagedAppConfigurations",
    };

    public static void MapCache(this WebApplication app)
    {
        // GET /cache — per-key blob-cache status for the active tenant. Iterates the
        // cacheable LIST surfaces (Surfaces.All with a non-null CacheKey) and reads
        // GetMetadata (no JSON deserialization) for each. On NullCacheService
        // (IsAvailable == false) every row reports CachedAtUtc=null/ItemCount=0; the
        // client treats a signed-in 200 + all-null as "cache unavailable".
        app.MapGet("/cache", (AuthSession auth, ICacheService cache) =>
        {
            if (auth.Graph is null) return Results.Conflict(); // not signed in
            var tid = auth.ActiveProfile?.TenantId;
            if (tid is null) return Results.Conflict();

            var rows = Surfaces.All
                .Where(s => s.CacheKey is not null)
                .Select(s => s.CacheKey!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(k => k, StringComparer.Ordinal)
                .Select(key =>
                {
                    var meta = cache.IsAvailable ? cache.GetMetadata(tid, key) : null;
                    var display = Surfaces.All.First(s => s.CacheKey == key).DisplayName;
                    return new CacheEntryStatusDto(
                        key,
                        display,
                        meta is { } m ? m.CachedAt.ToUniversalTime().ToString("o") : null,
                        meta is { } m2 ? m2.ItemCount : 0,
                        WarmAheadKeys.Contains(key));
                })
                .ToList();

            return Results.Ok(rows);
        });

        // GET /cache/summary — header summary (availability + last warm + live-key
        // counts). Separate from GET /cache (which returns the entry array) so the
        // client can render the header banner + counts and the inspect grid from two
        // typed shapes. 409 when signed out.
        app.MapGet("/cache/summary", (AuthSession auth, ICacheService cache) =>
        {
            if (auth.Graph is null) return Results.Conflict();
            var tid = auth.ActiveProfile?.TenantId;
            if (tid is null) return Results.Conflict();

            var entryCount = 0;
            var totalItems = 0;
            if (cache.IsAvailable)
            {
                foreach (var key in Surfaces.All
                             .Where(s => s.CacheKey is not null)
                             .Select(s => s.CacheKey!)
                             .Distinct(StringComparer.Ordinal))
                {
                    if (cache.GetMetadata(tid, key) is { } m)
                    {
                        entryCount++;
                        totalItems += m.ItemCount;
                    }
                }
            }

            return Results.Ok(new CacheSummaryDto(
                cache.IsAvailable,
                auth.LastWarmedUtc?.ToString("o"),
                entryCount,
                totalItems));
        });

        // POST /cache/warm?force= — fire-and-forget warm via the hoisted CacheWarmer
        // (the SAME routine sign-in + POST /sync use). 202 Accepted; 409 signed out.
        app.MapPost("/cache/warm", (bool? force, AuthSession auth, CacheWarmer warmer) =>
        {
            var graph = auth.Graph;
            var tid = auth.ActiveProfile?.TenantId;
            if (graph is null || tid is null) return Results.Conflict();

            _ = warmer.WarmAsync(graph, tid, force ?? false);
            return Results.Accepted();
        });

        // POST /cache/evict?key= — evict one LIST key (and its lazy DETAIL keys are
        // dropped by the next write), or ALL of the tenant's keys when key is omitted.
        // Scoped to the active tenant; 204 No Content; 409 signed out.
        app.MapPost("/cache/evict", (string? key, AuthSession auth, ICacheService cache) =>
        {
            if (auth.Graph is null) return Results.Conflict();
            var tid = auth.ActiveProfile?.TenantId;
            if (tid is null) return Results.Conflict();

            cache.Invalidate(tid, string.IsNullOrWhiteSpace(key) ? null : key);
            return Results.NoContent();
        });
    }
}
