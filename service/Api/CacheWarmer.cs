using Intune.Commander.Core.Models;
using Intune.Commander.Core.Services;

namespace CmProjectX.Api;

// M12.1 — blob read-through cache warming (docs/CACHE-M12.1.md). Hoisted out of the
// Program.cs sign-in closure so BOTH the sign-in warm (force=false) and the cache-dev
// POST /cache/warm endpoint can invoke the SAME routine. Registered as a singleton.
//
// One routine reused by sign-in (force=false), POST /sync (force=true), and the
// cache-dev warm button (force from the query string); all fire-and-forget, stamping
// LastWarmedUtc only on full success. Reuses the existing 31-type
// PrefetchAllToCacheAsync. GraphServiceClient is fully-qualified on purpose — a
// `using Microsoft.Graph.Beta` here would shadow IResult in Minimal API modules that
// reference this type.
public sealed class CacheWarmer
{
    private readonly ICacheService _cache;
    private readonly AuthSession _auth;
    private readonly ILogger<CacheWarmer> _log;

    public CacheWarmer(ICacheService cache, AuthSession auth, ILogger<CacheWarmer> log)
    {
        _cache = cache;
        _auth = auth;
        _log = log;
    }

    // Warm the blob cache for the given tenant. On an explicit refresh (force=true)
    // every cached LIST key is dropped FIRST: PrefetchAllToCacheAsync only re-fetches
    // the warm-ahead surfaces, so without this the lazy surfaces (vpp-tokens,
    // managed-devices, device-categories, …) would keep serving cached data until
    // their 24h TTL or a write. Invalidating up front makes a forced warm mean fresh
    // data for EVERY surface: warm-ahead keys are immediately re-warmed below; lazy
    // keys re-fill on their next GET. Must run before the warm, never after (that
    // would wipe the freshly-warmed lists). Stamps LastWarmedUtc only on success.
    public async Task WarmAsync(Microsoft.Graph.Beta.GraphServiceClient graph, string tenantId, bool force)
    {
        try
        {
            if (force)
            {
                foreach (var key in Surfaces.All.Select(s => s.CacheKey).Where(k => k is not null).Distinct())
                    _cache.Invalidate(tenantId, key!);
            }
            var checker = new AssignmentCheckerService(graph, _cache, tenantId);
            await checker.PrefetchAllToCacheAsync(forceRefresh: force);
            // M17 — cache the resolved policy→group assignment edges so the never-warmed cold
            // offline twin (TwinBuilder.FromCache) can serve the orphaned-policy /
            // conflicting-assignment analytics. Best-effort: the scan reuses the just-warmed
            // policy caches; a failure leaves the twin degraded (node inventory + CA reach).
            List<AssignmentReportRow> rows = new();
            try
            {
                rows = await checker.GetAllPoliciesWithAssignmentsAsync();
                _cache.Set(tenantId, TwinBuilder.AssignmentEdgesKey, TwinBuilder.AssignmentEdgesFromRows(rows));
            }
            catch (Exception ex) { _log.LogWarning(ex, "Twin assignment-edge caching skipped (continuing)"); }
            // M17 — cache group memberOf edges so the never-warmed cold offline twin resolves
            // effective membership (Twin.EffectiveMembers) like the online walk. Referenced set =
            // groups any device-mgmt policy assigns (the rows above) ∪ CA include/exclude groups —
            // the SAME groupIds TwinWarm expands. Best-effort, per-group guarded: a group whose
            // member fetch fails is simply omitted, degrading that group's membership only.
            try
            {
                var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in rows) if (!string.IsNullOrEmpty(r.GroupId)) referenced.Add(r.GroupId);
                try
                {
                    foreach (var p in await new ConditionalAccessPolicyService(graph).ListPoliciesAsync())
                    {
                        var u = p.Conditions?.Users;
                        foreach (var gid in u?.IncludeGroups ?? new()) if (!string.IsNullOrEmpty(gid)) referenced.Add(gid);
                        foreach (var gid in u?.ExcludeGroups ?? new()) if (!string.IsNullOrEmpty(gid)) referenced.Add(gid);
                    }
                }
                catch (Exception ex) { _log.LogDebug(ex, "Twin memberOf: CA group scan skipped"); }

                var groupSvc = new GroupService(graph);
                var groupMembers = new List<(string, IReadOnlyList<GroupMemberInfo>)>();
                foreach (var gid in referenced)
                {
                    try { groupMembers.Add((gid, await groupSvc.ListGroupMembersAsync(gid))); }
                    catch (Exception ex) { _log.LogDebug(ex, "Twin memberOf: members skipped for {GroupId}", gid); }
                }
                _cache.Set(tenantId, TwinBuilder.MemberEdgesKey, TwinBuilder.MemberEdgesFromMembers(groupMembers));
            }
            catch (Exception ex) { _log.LogWarning(ex, "Twin memberOf-edge caching skipped (continuing)"); }
            _auth.MarkWarmed();
            _log.LogInformation("Cache warm complete (force={Force})", force);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Cache warm failed (continuing)"); }
    }
}
