using Microsoft.Graph.Beta.Models;
using Intune.Commander.Core.Services;
using CmProjectX.Store;
// Microsoft.Graph.Beta.Models also defines a `WebApplication` type; alias to the ASP.NET
// host so the extension method resolves (see DevicesEndpoints for why).
using WebApplication = Microsoft.AspNetCore.Builder.WebApplication;
// GraphServiceClient is fully-qualified everywhere (no `using Microsoft.Graph.Beta`) so it
// never shadows IResult / breaks Minimal-API overload resolution.

namespace CmProjectX.Api;

// M17 Tenant digital twin — HTTP surface. The analytics/stats/node/query handlers load
// the materialized graph from the cache + store (NO Graph) and traverse it; only the
// optional `?warm=true` on /twin/rebuild touches Graph (to refresh the cached projection).
public static class TwinEndpoints
{
    private static readonly HashSet<string> ValidQueries = new()
    {
        "orphaned-policies", "redundant-assignments", "conflicting-assignments",
        "assignment-cycles", "ca-escape-paths", "coverage-gaps", "drift-hotspots",
    };

    public static void MapTwin(this WebApplication app)
    {
        // POST /twin/rebuild[?warm=true] — re-materialize from cache+store; with warm,
        // first refresh the cached projection from the engines (requires sign-in).
        app.MapPost("/twin/rebuild", async (bool? warm, AuthSession auth, ICacheService cache, ISnapshotStore store, CancellationToken ct) =>
        {
            var tid = auth.ActiveProfile?.TenantId;
            if (warm == true)
            {
                var g = auth.Graph;
                if (g is null || tid is null) return Results.Conflict();
                var (nodes, edges) = await TwinWarm.BuildAsync(g, ct);
                var builtUtc = DateTime.UtcNow.ToString("o");
                if (cache.IsAvailable)
                {
                    cache.Set(tid, TwinCacheKeys.Nodes, nodes);
                    cache.Set(tid, TwinCacheKeys.Edges, edges);
                    cache.Set(tid, TwinCacheKeys.BuiltUtc, new List<string> { builtUtc });
                }
                TwinStore.Save(nodes, edges, builtUtc);
            }
            else if (tid is not null && cache.IsAvailable
                && cache.Get<TwinNodeRow>(tid, TwinCacheKeys.Nodes) is null && TwinStore.Load() is null)
            {
                // Cold (offline) rebuild — no persisted projection yet: materialize one from
                // the per-screen caches (TwinBuilder, Graph-free) and stamp it, so subsequent
                // /twin/stats + analytics serve a non-empty, timestamped twin with no Graph.
                // Guarded on absence so a warm=false call never clobbers a richer warm build.
                var (nodes, edges) = TwinBuilder.FromCache(cache, tid);
                if (nodes.Count > 0)
                {
                    var builtUtc = DateTime.UtcNow.ToString("o");
                    cache.Set(tid, TwinCacheKeys.Nodes, nodes);
                    cache.Set(tid, TwinCacheKeys.Edges, edges);
                    cache.Set(tid, TwinCacheKeys.BuiltUtc, new List<string> { builtUtc });
                    TwinStore.Save(nodes, edges, builtUtc);
                }
            }
            var graph = TwinGraphLoader.Load(cache, tid, await SnapshotCountsAsync(store, tid));
            return Results.Ok(Stats(graph));
        });

        app.MapGet("/twin/stats", async (AuthSession auth, ICacheService cache, ISnapshotStore store) =>
            Results.Ok(Stats(TwinGraphLoader.Load(cache, auth.ActiveProfile?.TenantId, await SnapshotCountsAsync(store, auth.ActiveProfile?.TenantId)))));

        app.MapGet("/twin/analytics/{query}", async (string query, AuthSession auth, ICacheService cache, ISnapshotStore store) =>
        {
            if (!ValidQueries.Contains(query)) return ApiResults.BadRequest($"unknown query '{query}'");
            var graph = TwinGraphLoader.Load(cache, auth.ActiveProfile?.TenantId, await SnapshotCountsAsync(store, auth.ActiveProfile?.TenantId));
            return Results.Ok(new TwinAnalyticsResult(query, graph.BuiltUtc, graph.Source, graph.Analytics(query)));
        });

        app.MapGet("/twin/node/{id}", async (string id, AuthSession auth, ICacheService cache, ISnapshotStore store) =>
        {
            var graph = TwinGraphLoader.Load(cache, auth.ActiveProfile?.TenantId, await SnapshotCountsAsync(store, auth.ActiveProfile?.TenantId));
            var nb = graph.Neighborhood(id);
            return nb is null ? Results.NotFound() : Results.Ok(nb);
        });

        app.MapPost("/twin/query", async (TwinQueryRequest req, AuthSession auth, ICacheService cache, ISnapshotStore store) =>
        {
            var graph = TwinGraphLoader.Load(cache, auth.ActiveProfile?.TenantId, await SnapshotCountsAsync(store, auth.ActiveProfile?.TenantId));
            return Results.Ok(graph.Query(req.FromType ?? "", req.EdgePath ?? Array.Empty<string>()));
        });
    }

    private static TwinStats Stats(TwinGraph g) => new(g.NodeCount, g.EdgeCount, g.BuiltUtc, IsStale(g.BuiltUtc), g.Source);

    private static bool IsStale(string? builtUtc) =>
        builtUtc is null ||
        (DateTime.TryParse(builtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)
            && DateTime.UtcNow - t > TimeSpan.FromHours(24));

    // Snapshot counts per object (for drift-hotspots) — read from the append-only store.
    private static async Task<Dictionary<string, int>> SnapshotCountsAsync(ISnapshotStore store, string? tenantId)
    {
        try
        {
            var objs = await store.GetSnapshottedObjectsAsync(tenantId: tenantId);
            var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in objs) d[o.ObjectId] = o.SnapshotCount;
            return d;
        }
        catch { return new(); }
    }
}

// The ONLINE warm — assembles the node/edge projection from the engines so a later
// offline rebuild can serve it. Best-effort + live-verification pending (it is the only
// part of M17 that calls Graph). Covers the main assignable device-management surfaces +
// Conditional Access; more surfaces follow the same per-surface pattern.
internal static class TwinWarm
{
    public static async Task<(List<TwinNodeRow> Nodes, List<TwinEdgeRow> Edges)> BuildAsync(
        Microsoft.Graph.Beta.GraphServiceClient g, CancellationToken ct)
    {
        var nodes = new Dictionary<string, TwinNodeRow>(StringComparer.OrdinalIgnoreCase);
        var edges = new List<TwinEdgeRow>();
        var groupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Rich(string id, string type, string name, Dictionary<string, string> props)
        { if (!string.IsNullOrEmpty(id)) nodes[id] = new TwinNodeRow { Id = id, Type = type, Name = name, Props = props }; }
        void Stub(string id, string type, string name)
        { if (!string.IsNullOrEmpty(id)) nodes.TryAdd(id, new TwinNodeRow { Id = id, Type = type, Name = name }); }

        void AddTargets(string policyId, IEnumerable<DeviceAndAppManagementAssignmentTarget?> targets)
        {
            foreach (var t in targets)
            {
                var a = Assignments.ReadTarget(t);
                switch (a.Kind)
                {
                    case "exclusionGroup": if (a.GroupId is { } eg) { edges.Add(new TwinEdgeRow { From = policyId, To = eg, Type = "excludes" }); groupIds.Add(eg); } break;
                    case "allUsers": edges.Add(new TwinEdgeRow { From = policyId, To = "AllUsers", Type = "includes" }); break;
                    case "allDevices": edges.Add(new TwinEdgeRow { From = policyId, To = "AllDevices", Type = "includes" }); break;
                    default: if (a.GroupId is { } gid) { edges.Add(new TwinEdgeRow { From = policyId, To = gid, Type = "includes", FilterId = a.FilterId, Intent = a.Intent }); groupIds.Add(gid); } break;
                }
            }
        }

        // Compliance policies
        try
        {
            var svc = new CompliancePolicyService(g);
            foreach (var p in await svc.ListCompliancePoliciesAsync(ct))
            {
                if (p.Id is null) continue;
                Rich(p.Id, "Policy", p.DisplayName ?? "(unnamed)", new() { ["policyType"] = "Compliance Policy", ["platform"] = ListProjection.PlatformOf(p.OdataType) ?? "" });
                try { AddTargets(p.Id, (await svc.GetAssignmentsAsync(p.Id, ct)).Select(x => x.Target)); } catch { }
            }
        }
        catch { }

        // Device configurations
        try
        {
            var svc = new ConfigurationProfileService(g);
            foreach (var p in await svc.ListDeviceConfigurationsAsync(ct))
            {
                if (p.Id is null) continue;
                Rich(p.Id, "Policy", p.DisplayName ?? "(unnamed)", new() { ["policyType"] = "Device Configuration", ["platform"] = ListProjection.PlatformOf(p.OdataType) ?? "" });
                try { AddTargets(p.Id, (await svc.GetAssignmentsAsync(p.Id, ct)).Select(x => x.Target)); } catch { }
            }
        }
        catch { }

        // Settings catalog
        try
        {
            var svc = new SettingsCatalogService(g);
            foreach (var p in await svc.ListSettingsCatalogPoliciesAsync(ct))
            {
                if (p.Id is null) continue;
                Rich(p.Id, "Policy", p.Name ?? "(unnamed)", new() { ["policyType"] = "Settings Catalog", ["platform"] = p.Platforms?.ToString() ?? "" });
                try { AddTargets(p.Id, (await svc.GetAssignmentsAsync(p.Id, ct)).Select(x => x.Target)); } catch { }
            }
        }
        catch { }

        // Conditional Access (targets live in conditions.users)
        try
        {
            foreach (var p in await new ConditionalAccessPolicyService(g).ListPoliciesAsync(ct))
            {
                if (p.Id is null) continue;
                Rich(p.Id, "CAPolicy", p.DisplayName ?? "(unnamed)", new() { ["state"] = p.State?.ToString() ?? "" });
                var u = p.Conditions?.Users;
                foreach (var gid in u?.IncludeGroups ?? new()) { edges.Add(new TwinEdgeRow { From = p.Id, To = gid, Type = "includes" }); groupIds.Add(gid); }
                foreach (var gid in u?.ExcludeGroups ?? new()) { edges.Add(new TwinEdgeRow { From = p.Id, To = gid, Type = "excludes" }); groupIds.Add(gid); }
            }
        }
        catch { }

        // Dynamic-group ids (to flag CA escape paths).
        var dynamicIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try { foreach (var dg in await new GroupService(g).ListDynamicGroupsAsync(ct)) if (dg.Id is not null) dynamicIds.Add(dg.Id); }
        catch { }

        // Group nodes + memberOf edges for every referenced group.
        var groups = new GroupService(g);
        foreach (var gid in groupIds)
        {
            var props = new Dictionary<string, string> { ["groupType"] = dynamicIds.Contains(gid) ? "dynamic" : "assigned" };
            try { props["memberCount"] = (await groups.GetMemberCountsAsync(gid, ct)).Total.ToString(); } catch { props["memberCount"] = "0"; }
            Rich(gid, "Group", gid, props);
            try
            {
                foreach (var m in await groups.ListGroupMembersAsync(gid, ct))
                {
                    if (string.IsNullOrEmpty(m.Id)) continue;
                    var nt = m.MemberType switch { "Device" => "Device", "Group" => "Group", _ => "User" };
                    Stub(m.Id, nt, m.DisplayName);
                    edges.Add(new TwinEdgeRow { From = m.Id, To = gid, Type = "memberOf" });
                }
            }
            catch { }
        }

        return (nodes.Values.ToList(), edges);
    }
}
