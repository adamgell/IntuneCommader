using CmProjectX.Api;
using Intune.Commander.Core.Models;
using Intune.Commander.Core.Services;
using Microsoft.Graph.Beta.Models;
using Xunit;

namespace CmProjectX.Tests.Unit;

// Tier-1 hermetic tests for the M17 OFFLINE twin assembler (TwinBuilder). Unlike the
// existing Loader test (which seeds the twin's OWN projection keys), these feed the
// actual PER-SCREEN M12.1 cache shapes the builder reads — raw Graph List<T> under the
// Surfaces.CacheKey / prefetch keys — and prove the twin materializes non-zero nodes and
// edges with NO network and NO Graph call (only a fake in-memory ICacheService).
public class TwinBuilderTests
{
    // ── (a) DoD: offline assembler yields non-zero node/edge counts from per-screen caches ──
    [Fact]
    public void FromCache_AssemblesOffline_NonZeroNodesAndEdges_WithNoGraph()
    {
        var cache = new FakeCache();
        const string tid = "t1";
        SeedPerScreenCaches(cache, tid);

        var (nodes, edges) = TwinBuilder.FromCache(cache, tid);

        // DoD item 1: network unplugged + signed out → non-zero counts.
        Assert.NotEmpty(nodes);
        Assert.NotEmpty(edges);

        // Every node type the offline sources can produce is present, TwinWarm-shaped.
        Assert.Contains(nodes, n => n.Id == "d1" && n.Type == "Device"
            && n.Props.GetValueOrDefault("os") == "Windows"
            && n.Props.GetValueOrDefault("compliance") == "Compliant");
        Assert.Contains(nodes, n => n.Id == "cp1" && n.Type == "Policy"
            && n.Props.GetValueOrDefault("policyType") == "Compliance Policy"
            && n.Props.GetValueOrDefault("platform") == "Windows");
        Assert.Contains(nodes, n => n.Id == "dc1" && n.Type == "Policy"
            && n.Props.GetValueOrDefault("policyType") == "Device Configuration");
        Assert.Contains(nodes, n => n.Id == "sc1" && n.Type == "Policy"
            && n.Props.GetValueOrDefault("policyType") == "Settings Catalog");
        Assert.Contains(nodes, n => n.Id == "f1" && n.Type == "Filter");
        Assert.Contains(nodes, n => n.Id == "ga1" && n.Type == "Group"
            && n.Props.GetValueOrDefault("groupType") == "assigned");
        Assert.Contains(nodes, n => n.Id == "gd1" && n.Type == "Group"
            && n.Props.GetValueOrDefault("groupType") == "dynamic");
        Assert.Contains(nodes, n => n.Id == "ca1" && n.Type == "CAPolicy");

        // The one assignment-reach edge recoverable offline: CA include/exclude group refs.
        Assert.Contains(edges, e => e.From == "ca1" && e.To == "ga1" && e.Type == "includes");
        Assert.Contains(edges, e => e.From == "ca1" && e.To == "gd1" && e.Type == "excludes");
    }

    // ── (b) end-to-end offline analytic over the assembled graph (ca-escape-paths) ──
    // ca-escape-paths is not exercised by the existing TwinTests; here it runs against a
    // graph the OFFLINE builder assembled from the per-screen caches — a CA policy that
    // excludes a DYNAMIC (self-joinable) group is flagged, entirely offline.
    [Fact]
    public void FromCache_ThenCaEscapePaths_FlagsDynamicGroupExclusion()
    {
        var cache = new FakeCache();
        const string tid = "t1";
        SeedPerScreenCaches(cache, tid);

        var (nodes, edges) = TwinBuilder.FromCache(cache, tid);
        var graph = new TwinGraph(nodes, edges, "assembled", null);

        var escapes = graph.CaEscapePaths();
        Assert.Contains(escapes, f => f.Id == "ca1" && f.Kind == "dynamicExclusion");
    }

    // Degrades gracefully: an empty/cold cache produces an empty (not thrown) twin.
    [Fact]
    public void FromCache_ColdCache_ReturnsEmpty_DoesNotThrow()
    {
        var (nodes, edges) = TwinBuilder.FromCache(new FakeCache(), "t1");
        Assert.Empty(nodes);
        Assert.Empty(edges);
    }

    // Only some surfaces cached → the twin still builds from what's present (skips the rest).
    [Fact]
    public void FromCache_PartialCache_SkipsMissingSurfaces()
    {
        var cache = new FakeCache();
        const string tid = "t1";
        cache.Set(tid, "ManagedDevices", new List<ManagedDevice>
        {
            new() { Id = "d1", DeviceName = "Laptop-1", OperatingSystem = "Windows", ComplianceState = ComplianceState.Compliant },
        });

        var (nodes, edges) = TwinBuilder.FromCache(cache, tid);
        Assert.Single(nodes);
        Assert.Equal("Device", nodes[0].Type);
        Assert.Empty(edges); // no CA cache → no edges
    }

    // ── (b) a second uncovered analytic: coverage-gaps (pure TwinGraph traversal) ──
    [Fact]
    public void CoverageGaps_FlagsDeviceCoveredByNoCompliancePolicy()
    {
        var nodes = new List<TwinNodeRow>
        {
            new() { Id = "p1", Type = "Policy", Name = "Win Compliance",
                    Props = new() { ["policyType"] = "Compliance Policy", ["platform"] = "Windows" } },
            new() { Id = "g1", Type = "Group", Name = "Covered", Props = new() { ["memberCount"] = "1" } },
            new() { Id = "d1", Type = "Device", Name = "Covered device" },
            new() { Id = "d2", Type = "Device", Name = "Uncovered device" },
        };
        var edges = new List<TwinEdgeRow>
        {
            new() { From = "p1", To = "g1", Type = "includes" },
            new() { From = "d1", To = "g1", Type = "memberOf" },
        };
        var g = new TwinGraph(nodes, edges, "test", null);

        var gaps = g.CoverageGaps();
        Assert.Contains(gaps, f => f.Id == "d2" && f.Kind == "noCompliancePolicy");
        Assert.DoesNotContain(gaps, f => f.Id == "d1");
    }

    // ── (M17 closure) resolved assignment-edge projection + offline materialization ──

    [Fact]
    public void AssignmentEdgesFromRows_ProjectsIncludeExcludeAllUsers_Deduped()
    {
        var rows = new List<AssignmentReportRow>
        {
            new() { PolicyId = "cp1", GroupId = "ga1", GroupName = "Sales", AssignmentReason = "Group Assignment" },
            new() { PolicyId = "cp1", GroupId = "gd1", GroupName = "Kiosks", AssignmentReason = "Excluded" },
            new() { PolicyId = "dc1", AssignmentReason = "All Users" },
            new() { PolicyId = "sc1", AssignmentReason = "All Devices" },
            new() { PolicyId = "cp1", GroupId = "ga1", GroupName = "Sales", AssignmentReason = "Group Assignment" }, // dup
            new() { PolicyId = "orphan", AssignmentReason = "Unassigned" }, // no group target → no edge
        };
        var edges = TwinBuilder.AssignmentEdgesFromRows(rows);
        Assert.Contains(edges, e => e.From == "cp1" && e.To == "ga1" && e.Type == "includes");
        Assert.Contains(edges, e => e.From == "cp1" && e.To == "gd1" && e.Type == "excludes");
        Assert.Contains(edges, e => e.From == "dc1" && e.To == "AllUsers" && e.Type == "includes");
        Assert.Contains(edges, e => e.From == "sc1" && e.To == "AllDevices" && e.Type == "includes");
        Assert.Equal(4, edges.Count);                       // duplicate collapsed, unassigned skipped
        Assert.DoesNotContain(edges, e => e.From == "orphan");
    }

    [Fact]
    public void FromCache_WithCachedAssignmentEdges_MaterializesEdges_AndOrphanAnalyticDistinguishes()
    {
        var cache = new FakeCache();
        const string tid = "t1";
        SeedPerScreenCaches(cache, tid);
        // Resolved assignment edges as CacheWarmer would cache them: cp1 assigned to ga1;
        // dc1 + sc1 have NO assignment; one edge from a policy the twin doesn't know (dangling).
        cache.Set(tid, TwinBuilder.AssignmentEdgesKey, new List<TwinEdgeRow>
        {
            new() { From = "cp1", To = "ga1", Type = "includes" },
            new() { From = "not-a-twin-policy", To = "ga1", Type = "includes" },
        });

        var (nodes, edges) = TwinBuilder.FromCache(cache, tid);

        // The policy→group assignment edge is materialized OFFLINE (impossible before M17 closure).
        Assert.Contains(edges, e => e.From == "cp1" && e.To == "ga1" && e.Type == "includes");
        // A dangling edge (From is not a twin node) is filtered.
        Assert.DoesNotContain(edges, e => e.From == "not-a-twin-policy");

        // Orphaned-policies now DISTINGUISHES assigned from unassigned offline: cp1 has an
        // assignment (not "noAssignments"); dc1 + sc1 have none → flagged noAssignments.
        var g = new TwinGraph(nodes, edges, "assembled", null);
        var orphans = g.OrphanedPolicies();
        Assert.Contains(orphans, f => f.Id == "dc1" && f.Kind == "noAssignments");
        Assert.Contains(orphans, f => f.Id == "sc1" && f.Kind == "noAssignments");
        Assert.DoesNotContain(orphans, f => f.Id == "cp1" && f.Kind == "noAssignments");
    }

    // ── (M17 closure) resolved memberOf-edge projection + offline effective membership ──

    [Fact]
    public void MemberEdgesFromMembers_ProjectsTypedMemberOf_Deduped()
    {
        var members = new (string, IReadOnlyList<GroupMemberInfo>)[]
        {
            ("ga1", new List<GroupMemberInfo>
            {
                new("User", "Alice", "alice@x", "", "", "u1"),
                new("Device", "Laptop", "Windows", "", "", "d1"),
                new("Group", "Nested", "assigned", "", "", "gn"),
                new("User", "Alice", "alice@x", "", "", "u1"), // dup (same member+group) → collapsed
                new("User", "", "", "", "", ""),               // empty id → dropped
            }),
            ("", new List<GroupMemberInfo> { new("User", "X", "", "", "", "u9") }), // empty group → dropped
        };

        var rows = TwinBuilder.MemberEdgesFromMembers(members);

        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, r => r.MemberId == "u1" && r.MemberType == "User" && r.GroupId == "ga1");
        Assert.Contains(rows, r => r.MemberId == "d1" && r.MemberType == "Device" && r.GroupId == "ga1");
        Assert.Contains(rows, r => r.MemberId == "gn" && r.MemberType == "Group" && r.GroupId == "ga1");
    }

    [Fact]
    public void FromCache_WithCachedMemberEdges_MaterializesMemberOf_AndEffectiveMembersRecurses()
    {
        var cache = new FakeCache();
        const string tid = "t1";
        SeedPerScreenCaches(cache, tid);
        // Resolved memberOf edges as CacheWarmer would cache them: ga1 contains a device, a user,
        // and a NESTED group; the nested group contains a second user. Entra device/user ids are
        // a distinct id-space from ManagedDevice ids — same as the online walk's stubs.
        cache.Set(tid, TwinBuilder.MemberEdgesKey, new List<TwinBuilder.TwinMemberEdge>
        {
            new("dev-entra-1", "Device", "AAD Device 1", "ga1"),
            new("user-1", "User", "Alice", "ga1"),
            new("gnest", "Group", "Nested", "ga1"),
            new("user-2", "User", "Bob", "gnest"),
        });

        var (nodes, edges) = TwinBuilder.FromCache(cache, tid);

        // memberOf edges materialize OFFLINE (impossible before M17 closure), members typed.
        Assert.Contains(edges, e => e.From == "dev-entra-1" && e.To == "ga1" && e.Type == "memberOf");
        Assert.Contains(nodes, n => n.Id == "dev-entra-1" && n.Type == "Device");
        Assert.Contains(nodes, n => n.Id == "gnest" && n.Type == "Group");

        // Effective (transitive) membership resolves offline: direct principals + the nested
        // group's members, recursed exactly like the online EffectiveMembers walk.
        var g = new TwinGraph(nodes, edges, "assembled", null);
        var eff = g.EffectiveMembers("ga1", new());
        Assert.Contains("dev-entra-1", eff);
        Assert.Contains("user-1", eff);
        Assert.Contains("user-2", eff); // recursed through nested group gnest
    }

    // Seeds the raw Graph List<T> under the EXACT per-screen cache keys TwinBuilder reads.
    private static void SeedPerScreenCaches(FakeCache cache, string tid)
    {
        cache.Set(tid, "ManagedDevices", new List<ManagedDevice>
        {
            new() { Id = "d1", DeviceName = "Laptop-1", OperatingSystem = "Windows", ComplianceState = ComplianceState.Compliant },
        });
        cache.Set(tid, "CompliancePolicies", new List<DeviceCompliancePolicy>
        {
            new() { Id = "cp1", DisplayName = "Win Baseline", OdataType = "#microsoft.graph.windows10CompliancePolicy" },
        });
        cache.Set(tid, "DeviceConfigurations", new List<DeviceConfiguration>
        {
            new() { Id = "dc1", DisplayName = "Config A", OdataType = "#microsoft.graph.windows10GeneralConfiguration" },
        });
        cache.Set(tid, "SettingsCatalog", new List<DeviceManagementConfigurationPolicy>
        {
            new() { Id = "sc1", Name = "Settings Catalog 1" },
        });
        cache.Set(tid, "AssignmentFilters", new List<DeviceAndAppManagementAssignmentFilter>
        {
            new() { Id = "f1", DisplayName = "Corp Windows", Rule = "(device.osVersion -startsWith \"10.0\")" },
        });
        cache.Set(tid, "checker_AssignedGroups", new List<Group>
        {
            new() { Id = "ga1", DisplayName = "All Sales" },
        });
        cache.Set(tid, "checker_DynamicGroups", new List<Group>
        {
            new() { Id = "gd1", DisplayName = "Self-service group" },
        });
        cache.Set(tid, "ConditionalAccessPolicies", new List<ConditionalAccessPolicy>
        {
            new()
            {
                Id = "ca1", DisplayName = "Require MFA",
                Conditions = new ConditionalAccessConditionSet
                {
                    Users = new ConditionalAccessUsers
                    {
                        IncludeGroups = new() { "ga1" },
                        ExcludeGroups = new() { "gd1" },
                    },
                },
            },
        });
    }

    // Minimal in-memory ICacheService — stores the object reference (no serialization), so
    // the test exercises real key wiring with zero I/O and zero Graph.
    private sealed class FakeCache : ICacheService
    {
        private readonly Dictionary<string, object> _d = new();
        public bool IsAvailable => true;
        public void Set<T>(string tid, string dt, List<T> items, TimeSpan? ttl = null) => _d[$"{tid}|{dt}"] = items;
        public List<T>? Get<T>(string tid, string dt) => _d.TryGetValue($"{tid}|{dt}", out var o) ? (List<T>)o : null;
        public T? GetSingle<T>(string tid, string dt) where T : class => null;
        public void SetSingle<T>(string tid, string dt, T item, TimeSpan? ttl = null) where T : class { }
        public void Invalidate(string tid, string? dt = null) { }
        public int CleanupExpired() => 0;
        public (DateTime CachedAt, int ItemCount)? GetMetadata(string tid, string dt) => null;
        public Task<List<T>?> GetAsync<T>(string tid, string dt) => Task.FromResult(Get<T>(tid, dt));
        public Task SetAsync<T>(string tid, string dt, List<T> items, TimeSpan? ttl = null) { Set(tid, dt, items); return Task.CompletedTask; }
        public Task<T?> GetSingleAsync<T>(string tid, string dt) where T : class => Task.FromResult<T?>(null);
        public Task SetSingleAsync<T>(string tid, string dt, T item, TimeSpan? ttl = null) where T : class => Task.CompletedTask;
        public Task InvalidateAsync(string tid, string? dt = null) => Task.CompletedTask;
        public Task<(DateTime CachedAt, int ItemCount)?> GetMetadataAsync(string tid, string dt) => Task.FromResult<(DateTime, int)?>(null);
        public void Dispose() { }
    }
}
