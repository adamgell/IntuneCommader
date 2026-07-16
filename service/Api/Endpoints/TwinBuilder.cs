using Microsoft.Graph.Beta.Models;
using Intune.Commander.Core.Models;
using Intune.Commander.Core.Services;

namespace CmProjectX.Api;

// M17 — the OFFLINE twin assembler. Produces the SAME TwinNodeRow/TwinEdgeRow projection
// the online TwinWarm walk produces, but sourced from the M12.1 per-screen blob caches
// (ICacheService) instead of Graph. This is what lets the twin be non-empty with the
// network unplugged and signed out (M17 DoD item 1) — it never calls Graph.
//
// What each per-screen cache contributes. Keys mirror Surfaces.CacheKey (CachedReader)
// and the AssignmentCheckerService.PrefetchAllToCacheAsync group keys — NOT TwinCacheKeys
// (which are the twin's OWN projection, written only by the warm path):
//
//   "ManagedDevices"            List<ManagedDevice>                       -> Device nodes
//   "CompliancePolicies"        List<DeviceCompliancePolicy>              -> Policy nodes  (Compliance Policy)
//   "DeviceConfigurations"      List<DeviceConfiguration>                 -> Policy nodes  (Device Configuration)
//   "SettingsCatalog"           List<DeviceManagementConfigurationPolicy> -> Policy nodes  (Settings Catalog)
//   "ConditionalAccessPolicies" List<ConditionalAccessPolicy>            -> CAPolicy nodes + includes/excludes edges
//   "AssignmentFilters"         List<DeviceAndAppManagementAssignmentFilter> -> Filter nodes
//   "checker_DynamicGroups"     List<Group>                               -> Group nodes (groupType=dynamic)
//   "checker_AssignedGroups"    List<Group>                               -> Group nodes (groupType=assigned)
//
// OFFLINE COVERAGE (M17 closure): device-management policy→group ASSIGNMENT edges and group
// memberOf edges are now recoverable offline — CacheWarmer.WarmAsync resolves both at sign-in
// (the checker's GetAllPoliciesWithAssignmentsAsync for assignments; GroupService.
// ListGroupMembersAsync for the referenced groups' members) and caches them under
// AssignmentEdgesKey / MemberEdgesKey, which this builder reads and merges. Combined with the
// CA include/exclude reach inline in the cached policy, the never-warmed cold twin can now run
// the orphaned-policy, conflicting-assignment AND effective-membership analytics offline.
//
// memberOf parity + caveat: the warm expands members for the SAME referenced-group set the
// online TwinWarm walk does (groups targeted by an assignment ∪ CA include/exclude groups), so
// the offline projection matches it edge-for-edge. A nested group that is itself unreferenced
// still appears only as a Group stub (its own members aren't warmed) — identical to online.
// Per docs/part-ii/M17-twin.md storage decision A the twin stays refreshable, not authoritative.
internal static class TwinBuilder
{
    // Per-screen cache dataType keys (see the class remarks). Distinct from TwinCacheKeys.
    internal const string DevicesKey = "ManagedDevices";
    internal const string ComplianceKey = "CompliancePolicies";
    internal const string ConfigKey = "DeviceConfigurations";
    internal const string SettingsKey = "SettingsCatalog";
    internal const string CaKey = "ConditionalAccessPolicies";
    internal const string FiltersKey = "AssignmentFilters";
    internal const string DynamicGroupsKey = "checker_DynamicGroups";
    internal const string AssignedGroupsKey = "checker_AssignedGroups";
    // Resolved policy→group assignment edges (M17 closure) — cached at sign-in warm by
    // CacheWarmer.WarmAsync (AssignmentEdgesFromRows), the non-CA assignment reach that no
    // per-screen cached object holds inline. Lets the never-warmed cold twin support the
    // orphaned-policy / conflicting-assignment analytics offline.
    internal const string AssignmentEdgesKey = "twin_assignment_edges";
    // Resolved group memberOf edges (M17 closure) — cached at sign-in warm by CacheWarmer.
    // WarmAsync (MemberEdgesFromMembers) for the referenced-group set. The member's node type
    // (User/Device/nested Group) rides in the row so the offline twin resolves effective
    // membership (Twin.EffectiveMembers) exactly like the online walk.
    internal const string MemberEdgesKey = "twin_memberof_edges";

    // Deserialized per-screen lists — the pure Build input. Any list may be null (a cold
    // miss for that surface); Build skips nulls so the twin degrades one node-type at a time.
    public sealed class Inputs
    {
        public List<ManagedDevice>? Devices { get; set; }
        public List<DeviceCompliancePolicy>? Compliance { get; set; }
        public List<DeviceConfiguration>? DeviceConfigs { get; set; }
        public List<DeviceManagementConfigurationPolicy>? SettingsCatalog { get; set; }
        public List<ConditionalAccessPolicy>? ConditionalAccess { get; set; }
        public List<DeviceAndAppManagementAssignmentFilter>? Filters { get; set; }
        public List<Group>? DynamicGroups { get; set; }
        public List<Group>? AssignedGroups { get; set; }
        // Resolved policy→group assignment edges (cached by the warm flow) — the non-CA
        // assignment reach that isn't inline in any per-screen cached object.
        public List<TwinEdgeRow>? AssignmentEdges { get; set; }
        // Resolved group memberOf edges (cached by the warm flow) — member→group with the
        // member's node type carried alongside, since no per-screen cache holds membership.
        public List<TwinMemberEdge>? MemberEdges { get; set; }
    }

    // A cached group-membership row: a member (User/Device/nested Group) of a group. MemberType
    // is preserved so Build can stub the member with the right node type — EffectiveMembers must
    // distinguish a nested Group member (recursed) from a User/Device principal (terminal).
    public sealed record TwinMemberEdge(string MemberId, string MemberType, string? MemberName, string GroupId);

    // Reads the per-screen caches for one tenant and assembles. Each Get is wrapped so a
    // cold miss or a deserialize error skips that surface rather than throwing (the twin
    // must degrade gracefully). Returns empty lists if nothing is cached.
    public static (List<TwinNodeRow> Nodes, List<TwinEdgeRow> Edges) FromCache(ICacheService cache, string tenantId)
    {
        List<T>? Get<T>(string key) where T : class
        {
            try { return cache.Get<T>(tenantId, key); }
            catch { return null; }
        }

        return Build(new Inputs
        {
            Devices = Get<ManagedDevice>(DevicesKey),
            Compliance = Get<DeviceCompliancePolicy>(ComplianceKey),
            DeviceConfigs = Get<DeviceConfiguration>(ConfigKey),
            SettingsCatalog = Get<DeviceManagementConfigurationPolicy>(SettingsKey),
            ConditionalAccess = Get<ConditionalAccessPolicy>(CaKey),
            Filters = Get<DeviceAndAppManagementAssignmentFilter>(FiltersKey),
            DynamicGroups = Get<Group>(DynamicGroupsKey),
            AssignedGroups = Get<Group>(AssignedGroupsKey),
            AssignmentEdges = Get<TwinEdgeRow>(AssignmentEdgesKey),
            MemberEdges = Get<TwinMemberEdge>(MemberEdgesKey),
        });
    }

    // PURE assembly: deserialized per-screen lists -> twin rows. No Graph, no cache, no I/O
    // — unit-testable directly (TwinBuilderTests). Node/edge SHAPES mirror TwinWarm exactly
    // (same Type strings, same Props keys) so the analytics in Twin.cs consume them unchanged.
    public static (List<TwinNodeRow> Nodes, List<TwinEdgeRow> Edges) Build(Inputs inputs)
    {
        var nodes = new Dictionary<string, TwinNodeRow>(StringComparer.OrdinalIgnoreCase);
        var edges = new List<TwinEdgeRow>();

        void Rich(string? id, string type, string name, Dictionary<string, string> props)
        { if (!string.IsNullOrEmpty(id)) nodes[id!] = new TwinNodeRow { Id = id!, Type = type, Name = name, Props = props }; }
        void Stub(string? id, string type, string name)
        { if (!string.IsNullOrEmpty(id)) nodes.TryAdd(id!, new TwinNodeRow { Id = id!, Type = type, Name = name }); }

        // Groups first, so CA edges below reference already-typed Group nodes. groupType is
        // implied by the cache the group came from (the two prefetch queries partition
        // dynamic vs. non-dynamic server-side). memberCount is unknown offline and omitted.
        foreach (var g in inputs.AssignedGroups ?? new())
            Rich(g.Id, "Group", g.DisplayName ?? g.Id ?? "(unnamed)", new() { ["groupType"] = "assigned" });
        foreach (var g in inputs.DynamicGroups ?? new())
            Rich(g.Id, "Group", g.DisplayName ?? g.Id ?? "(unnamed)", new() { ["groupType"] = "dynamic" });

        // Devices
        foreach (var d in inputs.Devices ?? new())
            Rich(d.Id, "Device", d.DeviceName ?? d.Id ?? "(unnamed)", new()
            {
                ["os"] = d.OperatingSystem ?? "",
                ["compliance"] = d.ComplianceState?.ToString() ?? "",
            });

        // Policies (three assignable device-management surfaces cached as raw Graph lists).
        foreach (var p in inputs.Compliance ?? new())
            Rich(p.Id, "Policy", p.DisplayName ?? "(unnamed)",
                new() { ["policyType"] = "Compliance Policy", ["platform"] = ListProjection.PlatformOf(p.OdataType) ?? "" });
        foreach (var p in inputs.DeviceConfigs ?? new())
            Rich(p.Id, "Policy", p.DisplayName ?? "(unnamed)",
                new() { ["policyType"] = "Device Configuration", ["platform"] = ListProjection.PlatformOf(p.OdataType) ?? "" });
        foreach (var p in inputs.SettingsCatalog ?? new())
            Rich(p.Id, "Policy", p.Name ?? "(unnamed)",
                new() { ["policyType"] = "Settings Catalog", ["platform"] = p.Platforms?.ToString() ?? "" });

        // Filters
        foreach (var f in inputs.Filters ?? new())
            Rich(f.Id, "Filter", f.DisplayName ?? "(unnamed)",
                new() { ["platform"] = f.Platform?.ToString() ?? "", ["rule"] = f.Rule ?? "" });

        // Conditional Access — nodes plus the include/exclude group edges that live inline
        // in the cached policy (conditions.users). This is the only assignment reach that
        // survives offline; groups it references but that aren't in the group caches become
        // Group stubs (TryAdd never clobbers a real assigned/dynamic node above).
        foreach (var p in inputs.ConditionalAccess ?? new())
        {
            if (p.Id is null) continue;
            Rich(p.Id, "CAPolicy", p.DisplayName ?? "(unnamed)", new() { ["state"] = p.State?.ToString() ?? "" });
            var u = p.Conditions?.Users;
            foreach (var gid in u?.IncludeGroups ?? new())
            {
                if (string.IsNullOrEmpty(gid)) continue;
                edges.Add(new TwinEdgeRow { From = p.Id, To = gid, Type = "includes" });
                Stub(gid, "Group", gid);
            }
            foreach (var gid in u?.ExcludeGroups ?? new())
            {
                if (string.IsNullOrEmpty(gid)) continue;
                edges.Add(new TwinEdgeRow { From = p.Id, To = gid, Type = "excludes" });
                Stub(gid, "Group", gid);
            }
        }

        // Cached policy→group assignment edges (M17 closure): the non-CA assignment reach.
        // Added only for policies we actually have as nodes (skip dangling edges to non-twin
        // surfaces); referenced groups become stubs. AllUsers/AllDevices are edge-only
        // pseudo-targets, mirroring the online TwinWarm walk.
        foreach (var e in inputs.AssignmentEdges ?? new())
        {
            if (string.IsNullOrEmpty(e.From) || string.IsNullOrEmpty(e.To) || !nodes.ContainsKey(e.From)) continue;
            edges.Add(e);
            if (e.To is not ("AllUsers" or "AllDevices")) Stub(e.To, "Group", e.To);
        }

        // Cached group memberOf edges (M17 closure): member → group. The member is stubbed with
        // its real type (a nested Group must stay typed so EffectiveMembers recurses into it; a
        // User/Device is terminal); Stub's TryAdd never clobbers a richer node already present
        // (e.g. a managed Device or a cached Group). The group end is TryAdd'd as a Group stub
        // for the referenced-but-uncatalogued case. Mirrors TwinWarm's per-group expansion.
        foreach (var m in inputs.MemberEdges ?? new())
        {
            if (string.IsNullOrEmpty(m.MemberId) || string.IsNullOrEmpty(m.GroupId)) continue;
            Stub(m.MemberId, m.MemberType, m.MemberName ?? m.MemberId);
            Stub(m.GroupId, "Group", m.GroupId);
            edges.Add(new TwinEdgeRow { From = m.MemberId, To = m.GroupId, Type = "memberOf" });
        }

        return (nodes.Values.ToList(), edges);
    }

    // PURE projection: the checker's resolved assignment rows -> policy→group edges. One edge
    // per (policy, group, include/exclude), deduped. AllUsers/AllDevices reasons map to the
    // shared pseudo-targets. Rows with no group target (unassigned) yield no edge — so the
    // policy node stays orphaned, which is exactly what the orphaned-policy analytic detects.
    // Cached at sign-in warm; unit-tested (TwinBuilderTests).
    internal static List<TwinEdgeRow> AssignmentEdgesFromRows(IEnumerable<AssignmentReportRow> rows)
    {
        var edges = new List<TwinEdgeRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            if (string.IsNullOrEmpty(r.PolicyId)) continue;
            string? to = !string.IsNullOrEmpty(r.GroupId) ? r.GroupId
                : r.AssignmentReason.Contains("All Users", StringComparison.OrdinalIgnoreCase) ? "AllUsers"
                : r.AssignmentReason.Contains("All Devices", StringComparison.OrdinalIgnoreCase) ? "AllDevices"
                : null;
            if (to is null) continue;
            var kind = r.AssignmentReason.Contains("Exclud", StringComparison.OrdinalIgnoreCase) ? "excludes" : "includes";
            if (seen.Add($"{r.PolicyId}|{to}|{kind}"))
                edges.Add(new TwinEdgeRow { From = r.PolicyId, To = to, Type = kind });
        }
        return edges;
    }

    // PURE projection: resolved group memberships -> memberOf rows (member→group). One row per
    // (member, group), deduped; the member's type (User/Device/nested Group) is normalized to
    // the twin node types so Build stubs it correctly. Empty-id members/groups are dropped.
    // Cached at sign-in warm; unit-tested (TwinBuilderTests).
    internal static List<TwinMemberEdge> MemberEdgesFromMembers(
        IEnumerable<(string GroupId, IReadOnlyList<GroupMemberInfo> Members)> groupMembers)
    {
        var rows = new List<TwinMemberEdge>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (groupId, members) in groupMembers)
        {
            if (string.IsNullOrEmpty(groupId)) continue;
            foreach (var m in members)
            {
                if (string.IsNullOrEmpty(m.Id)) continue;
                var type = m.MemberType switch { "Device" => "Device", "Group" => "Group", _ => "User" };
                if (seen.Add($"{m.Id}|{groupId}"))
                    rows.Add(new TwinMemberEdge(m.Id, type, string.IsNullOrEmpty(m.DisplayName) ? null : m.DisplayName, groupId));
            }
        }
        return rows;
    }
}
