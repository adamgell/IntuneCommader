using CmProjectX.Api;
using Intune.Commander.Core.Services;
using Xunit;

namespace CmProjectX.Tests.Unit;

// Tier-1 hermetic tests for the M17 tenant digital twin. The graph + analytics are
// Graph-free, so the DoD ("seed the cache → it appears in orphaned-/conflicting-")
// is verified entirely offline — building a graph (or seeding a fake ICacheService)
// and asserting the analytics, with no Core read service invoked.
public class TwinTests
{
    private static TwinNodeRow Pol(string id, string name, string policyType = "Compliance Policy", string platform = "Windows")
        => new() { Id = id, Type = "Policy", Name = name, Props = new() { ["policyType"] = policyType, ["platform"] = platform } };
    private static TwinNodeRow Grp(string id, string name, int memberCount, string groupType = "assigned")
        => new() { Id = id, Type = "Group", Name = name, Props = new() { ["memberCount"] = memberCount.ToString(), ["groupType"] = groupType } };
    private static TwinNodeRow Usr(string id) => new() { Id = id, Type = "User", Name = id };
    private static TwinEdgeRow E(string from, string to, string type) => new() { From = from, To = to, Type = type };

    [Fact]
    public void OrphanedPolicies_FlagsEmptyGroupOnly_NoAssignments_DeletedGroup()
    {
        var nodes = new List<TwinNodeRow> { Pol("p1", "Empty-only"), Grp("g0", "Empty Group", 0), Pol("p2", "Unassigned"), Pol("p3", "Deleted-target") };
        var edges = new List<TwinEdgeRow> { E("p1", "g0", "includes"), E("p3", "gX", "includes") }; // p2 has no includes; gX has no node
        var g = new TwinGraph(nodes, edges, "test", null);

        var orphans = g.OrphanedPolicies();
        Assert.Contains(orphans, f => f.Id == "p1" && f.Kind == "emptyGroupOnly");
        Assert.Contains(orphans, f => f.Id == "p2" && f.Kind == "noAssignments");
        Assert.Contains(orphans, f => f.Id == "p3" && f.Kind == "deletedGroup");
    }

    [Fact]
    public void OrphanedPolicies_DoesNotFlag_PopulatedGroup_or_AllDevices()
    {
        var nodes = new List<TwinNodeRow> { Pol("p1", "Has members"), Grp("g1", "Pop", 2), Usr("u1"), Usr("u2"), Pol("p2", "AllDevices") };
        var edges = new List<TwinEdgeRow> { E("p1", "g1", "includes"), E("u1", "g1", "memberOf"), E("u2", "g1", "memberOf"), E("p2", "AllDevices", "includes") };
        var g = new TwinGraph(nodes, edges, "test", null);

        var orphans = g.OrphanedPolicies();
        Assert.DoesNotContain(orphans, f => f.Id == "p1");
        Assert.DoesNotContain(orphans, f => f.Id == "p2");
    }

    [Fact]
    public void ConflictingAssignments_FlagsIncludeExcludeOverlap_WithCorrectOverlap()
    {
        // p includes A {u1,u2}, excludes B {u2} → 1 overlapping principal (u2).
        var nodes = new List<TwinNodeRow> { Pol("p1", "Conflict"), Grp("A", "Inc", 2), Grp("B", "Exc", 1), Usr("u1"), Usr("u2") };
        var edges = new List<TwinEdgeRow>
        {
            E("p1", "A", "includes"), E("p1", "B", "excludes"),
            E("u1", "A", "memberOf"), E("u2", "A", "memberOf"), E("u2", "B", "memberOf"),
        };
        var g = new TwinGraph(nodes, edges, "test", null);

        var f = Assert.Single(g.ConflictingAssignments(), x => x.Id == "p1" && x.Kind == "includeExcludeOverlap");
        Assert.Equal(1, f.Metrics["overlap"]);
        Assert.Contains(f.Refs, r => r.Id == "A" && r.Role == "includeGroup");
        Assert.Contains(f.Refs, r => r.Id == "B" && r.Role == "excludeGroup");
    }

    [Fact]
    public void AssignmentCycles_DetectsNestedGroupCycle()
    {
        var nodes = new List<TwinNodeRow> { Grp("A", "A", 1), Grp("B", "B", 1) };
        var edges = new List<TwinEdgeRow> { E("A", "B", "memberOf"), E("B", "A", "memberOf") };
        var g = new TwinGraph(nodes, edges, "test", null);

        var cycles = g.AssignmentCycles();
        Assert.Single(cycles);
        Assert.Equal(2, cycles[0].Metrics["cycleSize"]);
    }

    [Fact]
    public void Loader_FromSeededCache_RunsOfflineAndFindsOrphan()
    {
        // The DoD literally: seed the CACHE → it appears in orphaned-policies (offline).
        var cache = new FakeCache();
        cache.Set("t1", TwinCacheKeys.Nodes, new List<TwinNodeRow> { Pol("p1", "Empty-only"), Grp("g0", "Empty", 0) });
        cache.Set("t1", TwinCacheKeys.Edges, new List<TwinEdgeRow> { E("p1", "g0", "includes") });

        var g = TwinGraphLoader.Load(cache, "t1");
        Assert.Equal("cache", g.Source);
        Assert.Contains(g.OrphanedPolicies(), f => f.Id == "p1" && f.Kind == "emptyGroupOnly");
    }

    [Fact]
    public void Neighborhood_ReturnsInboundEdgesAndMembers()
    {
        var nodes = new List<TwinNodeRow> { Pol("p1", "Targets G"), Grp("g1", "G", 1), Usr("u1") };
        var edges = new List<TwinEdgeRow> { E("p1", "g1", "includes"), E("u1", "g1", "memberOf") };
        var g = new TwinGraph(nodes, edges, "test", null);

        var nb = g.Neighborhood("g1");
        Assert.NotNull(nb);
        Assert.Contains(nb!.Inbound, e => e.From == "p1" && e.Type == "includes");
        Assert.Contains(nb.Members, m => m.Id == "u1");
    }

    // Minimal in-memory ICacheService for the offline loader test.
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
