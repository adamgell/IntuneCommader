using System.Text.Json;
using Intune.Commander.Core.Services;

namespace CmProjectX.Api;

// M17 Tenant digital twin — the materialized graph + the offline analytics. PURE
// (no Graph): the graph is assembled from a cached node/edge projection (TwinEndpoints
// reads/writes ICacheService) and traversed here. Kept Graph-free so the hermetic Unit
// tests exercise the analytics + the orphan/conflict injection cases directly.

// The cached projection rows. Plain get/set POCOs so LiteDB (ICacheService) round-trips
// them. An online "warm" computes these from the engines; the analytics path only reads.
public sealed class TwinNodeRow
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";   // Device | User | Group | Policy | CAPolicy | Filter
    public string Name { get; set; } = "";
    public Dictionary<string, string> Props { get; set; } = new();
}

public sealed class TwinEdgeRow
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Type { get; set; } = "";   // memberOf | includes | excludes
    public string? FilterId { get; set; }
    public string? Intent { get; set; }
}

// Cache data-type keys for the projection (per {tenantId}|<key>).
public static class TwinCacheKeys
{
    public const string Nodes = "twin_nodes";
    public const string Edges = "twin_edges";
    public const string BuiltUtc = "twin_built";
}

// The in-memory graph: node map + out/in adjacency. Built once per request from rows.
public sealed class TwinGraph
{
    private readonly Dictionary<string, TwinNodeRow> _nodes;
    private readonly List<TwinEdgeRow> _edges;
    private readonly Dictionary<string, List<TwinEdgeRow>> _out = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<TwinEdgeRow>> _in = new(StringComparer.OrdinalIgnoreCase);

    public string Source { get; }
    public string? BuiltUtc { get; }
    public int NodeCount => _nodes.Count;
    public int EdgeCount => _edges.Count;
    public IEnumerable<TwinNodeRow> Nodes => _nodes.Values;

    public TwinGraph(IEnumerable<TwinNodeRow> nodes, IEnumerable<TwinEdgeRow> edges, string source, string? builtUtc)
    {
        _nodes = nodes.Where(n => !string.IsNullOrEmpty(n.Id))
            .GroupBy(n => n.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _edges = edges.Where(e => !string.IsNullOrEmpty(e.From) && !string.IsNullOrEmpty(e.To)).ToList();
        foreach (var e in _edges)
        {
            (_out.TryGetValue(e.From, out var ol) ? ol : _out[e.From] = new()).Add(e);
            (_in.TryGetValue(e.To, out var il) ? il : _in[e.To] = new()).Add(e);
        }
        Source = source;
        BuiltUtc = builtUtc;
    }

    public TwinNodeRow? Node(string id) => _nodes.TryGetValue(id, out var n) ? n : null;
    public IReadOnlyList<TwinEdgeRow> Out(string id) => _out.TryGetValue(id, out var l) ? l : (IReadOnlyList<TwinEdgeRow>)Array.Empty<TwinEdgeRow>();
    public IReadOnlyList<TwinEdgeRow> In(string id) => _in.TryGetValue(id, out var l) ? l : (IReadOnlyList<TwinEdgeRow>)Array.Empty<TwinEdgeRow>();

    private static bool IsAll(string to) => to is "AllUsers" or "AllDevices";

    // memberCount from props if present, else the transitive memberOf expansion size.
    private int MemberCount(TwinNodeRow g, Dictionary<string, HashSet<string>> memo) =>
        g.Props.TryGetValue("memberCount", out var s) && int.TryParse(s, out var c) ? c : EffectiveMembers(g.Id, memo).Count;

    // Transitive memberOf expansion: the User/Device principals reachable into a group
    // (nested groups recursed; cycle-guarded). Memoized per analytics call.
    public HashSet<string> EffectiveMembers(string groupId, Dictionary<string, HashSet<string>> memo, HashSet<string>? visiting = null)
    {
        if (memo.TryGetValue(groupId, out var cached)) return cached;
        visiting ??= new(StringComparer.OrdinalIgnoreCase);
        if (!visiting.Add(groupId)) return new(StringComparer.OrdinalIgnoreCase); // cycle edge contributes nothing
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in In(groupId).Where(e => e.Type == "memberOf"))
        {
            var m = Node(e.From);
            if (m?.Type == "Group") result.UnionWith(EffectiveMembers(e.From, memo, visiting));
            else result.Add(e.From); // User / Device principal
        }
        visiting.Remove(groupId);
        memo[groupId] = result;
        return result;
    }

    // ── analytics ──────────────────────────────────────────────────────────────

    public List<TwinFinding> Analytics(string query) => query switch
    {
        "orphaned-policies" => OrphanedPolicies(),
        "redundant-assignments" => RedundantAssignments(),
        "conflicting-assignments" => ConflictingAssignments(),
        "assignment-cycles" => AssignmentCycles(),
        "ca-escape-paths" => CaEscapePaths(),
        "coverage-gaps" => CoverageGaps(),
        "drift-hotspots" => DriftHotspots(),
        _ => throw new ArgumentOutOfRangeException(nameof(query)),
    };

    private IEnumerable<TwinNodeRow> Policies => _nodes.Values.Where(n => n.Type is "Policy" or "CAPolicy");

    public List<TwinFinding> OrphanedPolicies()
    {
        var memo = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var findings = new List<TwinFinding>();
        foreach (var p in Policies)
        {
            var includes = Out(p.Id).Where(e => e.Type == "includes").ToList();
            if (includes.Count == 0)
            {
                findings.Add(F(p.Id, p.Name, "noAssignments", "Policy has zero assignment targets.",
                    new(), new() { ["includeTargets"] = 0 }));
                continue;
            }
            var refs = new List<TwinRef>();
            var allEmpty = true;
            var anyDeleted = false;
            foreach (var e in includes)
            {
                if (IsAll(e.To)) { allEmpty = false; continue; }
                var g = Node(e.To);
                if (g is null) { refs.Add(new TwinRef(e.To, e.To, "includeGroup", 0)); anyDeleted = true; continue; }
                var mc = MemberCount(g, memo);
                refs.Add(new TwinRef(g.Id, g.Name, "includeGroup", mc));
                if (mc != 0) allEmpty = false;
            }
            if (allEmpty)
            {
                var reason = anyDeleted ? "deletedGroup" : "emptyGroupOnly";
                var detail = anyDeleted
                    ? "Assignment targets a group id that no longer resolves."
                    : "Only assignment targets group(s) with 0 members.";
                findings.Add(F(p.Id, p.Name, reason, detail, refs, new() { ["includeTargets"] = includes.Count }));
            }
        }
        return findings;
    }

    public List<TwinFinding> ConflictingAssignments()
    {
        var memo = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var findings = new List<TwinFinding>();

        // include ∩ exclude overlap per policy
        foreach (var p in Policies)
        {
            var incGroups = Out(p.Id).Where(e => e.Type == "includes" && !IsAll(e.To)).Select(e => e.To).ToList();
            var incAll = Out(p.Id).Any(e => e.Type == "includes" && IsAll(e.To));
            var excGroups = Out(p.Id).Where(e => e.Type == "excludes").Select(e => e.To).ToList();
            if (excGroups.Count == 0) continue;

            var incMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var gid in incGroups) incMembers.UnionWith(EffectiveMembers(gid, memo));

            foreach (var exGid in excGroups)
            {
                var exMembers = EffectiveMembers(exGid, memo);
                var overlap = incAll ? exMembers.Count : incMembers.Intersect(exMembers, StringComparer.OrdinalIgnoreCase).Count();
                if (overlap <= 0) continue;
                var refs = new List<TwinRef>();
                if (incAll) refs.Add(new TwinRef("all", "All Users/Devices", "includeGroup", null));
                foreach (var gid in incGroups) { var g = Node(gid); refs.Add(new TwinRef(gid, g?.Name ?? gid, "includeGroup", EffectiveMembers(gid, memo).Count)); }
                var exg = Node(exGid);
                refs.Add(new TwinRef(exGid, exg?.Name ?? exGid, "excludeGroup", exMembers.Count));
                findings.Add(F(p.Id, p.Name, "includeExcludeOverlap",
                    $"{overlap} principal(s) are both included and excluded (net excluded); likely unintended.",
                    refs, new() { ["overlap"] = overlap }));
            }
        }

        // cross-policy contention: same policyType + platform whose effective member-sets intersect
        var bySig = Policies.Where(p => p.Type == "Policy")
            .GroupBy(p => $"{p.Props.GetValueOrDefault("policyType")}|{p.Props.GetValueOrDefault("platform")}")
            .Where(grp => grp.Count() > 1);
        foreach (var grp in bySig)
        {
            var list = grp.ToList();
            var sets = list.ToDictionary(p => p.Id, p => Effective(p, memo), StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < list.Count; i++)
                for (var j = i + 1; j < list.Count; j++)
                {
                    var overlap = sets[list[i].Id].Intersect(sets[list[j].Id], StringComparer.OrdinalIgnoreCase).Count();
                    if (overlap <= 0) continue;
                    findings.Add(F(list[i].Id, list[i].Name, "crossPolicyContention",
                        $"Two {grp.Key.Replace('|', '/')} policies target {overlap} overlapping principals; last-writer-wins on conflicting settings.",
                        new() { new TwinRef(list[j].Id, list[j].Name, "contendsWith", null) },
                        new() { ["overlap"] = overlap }));
                }
        }
        return findings;
    }

    // effective principals a policy targets (union of include groups; All* not expanded here)
    private HashSet<string> Effective(TwinNodeRow p, Dictionary<string, HashSet<string>> memo)
    {
        var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in Out(p.Id).Where(e => e.Type == "includes" && !IsAll(e.To)))
            s.UnionWith(EffectiveMembers(e.To, memo));
        return s;
    }

    public List<TwinFinding> RedundantAssignments()
    {
        var memo = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var findings = new List<TwinFinding>();
        foreach (var p in Policies)
        {
            var incGroups = Out(p.Id).Where(e => e.Type == "includes" && !IsAll(e.To)).Select(e => e.To).Distinct().ToList();
            var incAll = Out(p.Id).Any(e => e.Type == "includes" && IsAll(e.To));
            if (incAll && incGroups.Count > 0)
            {
                findings.Add(F(p.Id, p.Name, "subsetCovered",
                    $"{incGroups.Count} explicit include group(s) are covered by an All Users/Devices include and are redundant.",
                    incGroups.Select(g => new TwinRef(g, Node(g)?.Name ?? g, "includeGroup", EffectiveMembers(g, memo).Count)).ToList(),
                    new() { ["redundantGroups"] = incGroups.Count }));
                continue;
            }
            // pairwise subset among include groups
            for (var i = 0; i < incGroups.Count; i++)
                for (var j = 0; j < incGroups.Count; j++)
                {
                    if (i == j) continue;
                    var a = EffectiveMembers(incGroups[i], memo);
                    var b = EffectiveMembers(incGroups[j], memo);
                    if (a.Count > 0 && a.IsSubsetOf(b) && a.Count < b.Count)
                    {
                        findings.Add(F(p.Id, p.Name, "subsetCovered",
                            $"Include group '{Node(incGroups[i])?.Name ?? incGroups[i]}' ({a.Count}) is a subset of '{Node(incGroups[j])?.Name ?? incGroups[j]}' ({b.Count}) on the same policy.",
                            new() { new TwinRef(incGroups[i], Node(incGroups[i])?.Name ?? incGroups[i], "target", a.Count), new TwinRef(incGroups[j], Node(incGroups[j])?.Name ?? incGroups[j], "target", b.Count) },
                            new() { ["subset"] = a.Count, ["superset"] = b.Count }));
                    }
                }
        }
        return findings;
    }

    public List<TwinFinding> AssignmentCycles()
    {
        // Tarjan SCC over the Group→Group memberOf subgraph; SCC size > 1 == a cycle.
        var groups = _nodes.Values.Where(n => n.Type == "Group").Select(n => n.Id).ToList();
        var groupSet = new HashSet<string>(groups, StringComparer.OrdinalIgnoreCase);
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var low = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var onStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        var idx = 0;
        var sccs = new List<List<string>>();

        // group g is a member of group h  ⇔ edge memberOf from g to h, both groups
        IEnumerable<string> Succ(string g) => Out(g).Where(e => e.Type == "memberOf" && groupSet.Contains(e.To)).Select(e => e.To);

        void Strong(string v)
        {
            index[v] = low[v] = idx++; stack.Push(v); onStack.Add(v);
            foreach (var w in Succ(v))
            {
                if (!index.ContainsKey(w)) { Strong(w); low[v] = Math.Min(low[v], low[w]); }
                else if (onStack.Contains(w)) low[v] = Math.Min(low[v], index[w]);
            }
            if (low[v] == index[v])
            {
                var comp = new List<string>();
                string w;
                do { w = stack.Pop(); onStack.Remove(w); comp.Add(w); } while (!string.Equals(w, v, StringComparison.OrdinalIgnoreCase));
                sccs.Add(comp);
            }
        }
        foreach (var g in groups) if (!index.ContainsKey(g)) Strong(g);

        return sccs.Where(c => c.Count > 1).Select(c => F(
            c[0], Node(c[0])?.Name, "membershipCycle",
            $"Nested-group membership cycle of {c.Count} groups makes effective assignment ill-defined.",
            c.Select(id => new TwinRef(id, Node(id)?.Name ?? id, "target", null)).ToList(),
            new() { ["cycleSize"] = c.Count })).ToList();
    }

    public List<TwinFinding> CaEscapePaths()
    {
        var memo = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var findings = new List<TwinFinding>();
        foreach (var ca in _nodes.Values.Where(n => n.Type == "CAPolicy"))
        {
            foreach (var e in Out(ca.Id).Where(e => e.Type == "excludes"))
            {
                var g = Node(e.To);
                if (g is null) continue;
                var dynamic = g.Props.GetValueOrDefault("groupType") == "dynamic";
                if (dynamic)
                    findings.Add(F(ca.Id, ca.Name, "dynamicExclusion",
                        $"CA policy excludes dynamic group '{g.Name}' — its membership is rule-driven and may be self-joinable, bypassing the policy.",
                        new() { new TwinRef(g.Id, g.Name, "excludeGroup", MemberCount(g, memo)) },
                        new() { ["excludedMembers"] = MemberCount(g, memo) }));
            }
        }
        return findings;
    }

    public List<TwinFinding> CoverageGaps()
    {
        var memo = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var anyCompliance = false;
        foreach (var p in Policies.Where(p => p.Props.GetValueOrDefault("policyType") == "Compliance Policy"))
        {
            anyCompliance = true;
            if (Out(p.Id).Any(e => e.Type == "includes" && e.To == "AllDevices"))
            {
                // All Devices covers every Device node
                foreach (var d in _nodes.Values.Where(n => n.Type == "Device")) covered.Add(d.Id);
            }
            foreach (var e in Out(p.Id).Where(e => e.Type == "includes" && !IsAll(e.To)))
                foreach (var id in EffectiveMembers(e.To, memo))
                    if (Node(id)?.Type == "Device") covered.Add(id);
        }
        if (!anyCompliance) return new();
        var gaps = _nodes.Values.Where(n => n.Type == "Device" && !covered.Contains(n.Id)).ToList();
        return gaps.Select(d => F(d.Id, d.Name, "noCompliancePolicy",
            "Device is targeted by no compliance policy.", new(), new())).ToList();
    }

    public List<TwinFinding> DriftHotspots()
    {
        // snapshotCount is stamped into node props by the rebuild (from SnapshotStore).
        return _nodes.Values
            .Select(n => (n, c: int.TryParse(n.Props.GetValueOrDefault("snapshotCount"), out var v) ? v : 0))
            .Where(x => x.c >= 2)
            .OrderByDescending(x => x.c)
            .Take(25)
            .Select(x =>
            {
                var reach = Out(x.n.Id).Any(e => e.Type == "includes" && IsAll(e.To)) ? "AllUsers/AllDevices" : $"{Out(x.n.Id).Count(e => e.Type == "includes")} group(s)";
                return F(x.n.Id, x.n.Name, "driftHotspot",
                    $"Changed {x.c} times over the retained window; reach: {reach}.",
                    new(), new() { ["snapshotCount"] = x.c });
            }).ToList();
    }

    // ── neighborhood + query ─────────────────────────────────────────────────────

    public TwinNeighborhood? Neighborhood(string id)
    {
        var n = Node(id);
        if (n is null) return null;
        var inbound = In(id).Select(e => new TwinEdgeView(e.From, e.To, e.Type, Wire(Node(e.From) ?? Stub(e.From)))).ToList();
        var outbound = Out(id).Select(e => new TwinEdgeView(e.From, e.To, e.Type, Wire(Node(e.To) ?? Stub(e.To)))).ToList();
        // group members = principals with a memberOf edge into this node
        var members = In(id).Where(e => e.Type == "memberOf").Select(e => Wire(Node(e.From) ?? Stub(e.From))).ToList();
        var warnings = new List<string>();
        if (n.Type == "Group")
        {
            var mc = int.TryParse(n.Props.GetValueOrDefault("memberCount"), out var c) ? c : members.Count;
            var targeting = In(id).Count(e => e.Type is "includes" or "excludes");
            if (mc == 0 && targeting > 0)
                warnings.Add($"Group has 0 members; {targeting} policy(ies) target it (see orphaned-policies).");
        }
        return new TwinNeighborhood(Wire(n), inbound, outbound, members, warnings);
    }

    public TwinQueryResult Query(string fromType, IReadOnlyList<string> edgePath)
    {
        var frontier = _nodes.Values.Where(n => string.Equals(n.Type, fromType, StringComparison.OrdinalIgnoreCase))
            .Select(n => n.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nodes = new HashSet<string>(frontier, StringComparer.OrdinalIgnoreCase);
        var edges = new List<TwinEdgeRow>();
        foreach (var edgeType in edgePath)
        {
            var next = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in frontier)
                foreach (var e in Out(id).Where(e => string.Equals(e.Type, edgeType, StringComparison.OrdinalIgnoreCase)))
                {
                    edges.Add(e); next.Add(e.To); nodes.Add(e.To);
                }
            frontier = next;
        }
        return new TwinQueryResult(
            nodes.Select(id => Wire(Node(id) ?? Stub(id))).ToList(),
            edges.Select(e => new TwinEdge(e.From, e.To, e.Type, e.FilterId, e.Intent)).ToList());
    }

    private static TwinNodeRow Stub(string id) => new() { Id = id, Type = "Unresolved", Name = id };
    public static TwinNode Wire(TwinNodeRow n) => new(n.Id, n.Type, n.Name, n.Props);

    private static TwinFinding F(string? id, string? name, string kind, string detail, List<TwinRef> refs, Dictionary<string, int> metrics) =>
        new(id, name, kind, detail, refs, metrics);
}

// Reads/writes the cached projection. Pure file/cache I/O — no Graph.
public static class TwinStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string Dir()
    {
        var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cmProjectX");
        Directory.CreateDirectory(d);
        return d;
    }
    private static string TwinJson => Path.Combine(Dir(), "twin.json");

    public sealed class Persisted
    {
        public List<TwinNodeRow> Nodes { get; set; } = new();
        public List<TwinEdgeRow> Edges { get; set; } = new();
        public string? BuiltUtc { get; set; }
    }

    public static void Save(IReadOnlyList<TwinNodeRow> nodes, IReadOnlyList<TwinEdgeRow> edges, string builtUtc)
    {
        var p = new Persisted { Nodes = nodes.ToList(), Edges = edges.ToList(), BuiltUtc = builtUtc };
        File.WriteAllText(TwinJson, JsonSerializer.Serialize(p, Json));
    }

    public static Persisted? Load()
    {
        if (!File.Exists(TwinJson)) return null;
        try { return JsonSerializer.Deserialize<Persisted>(File.ReadAllText(TwinJson), Json); }
        catch { return null; }
    }
}

// Loads the materialized graph OFFLINE — from ICacheService (the projection the warm
// wrote) falling back to twin.json, then stamps snapshot counts from the store onto
// nodes. No Graph. Static + cache-only so the hermetic tests can seed a fake cache.
public static class TwinGraphLoader
{
    public static TwinGraph Load(ICacheService? cache, string? tenantId, IReadOnlyDictionary<string, int>? snapshotCounts = null)
    {
        List<TwinNodeRow>? nodes = null;
        List<TwinEdgeRow>? edges = null;
        string? builtUtc = null;
        var source = "empty";

        if (cache is not null && cache.IsAvailable && tenantId is not null)
        {
            nodes = cache.Get<TwinNodeRow>(tenantId, TwinCacheKeys.Nodes);
            edges = cache.Get<TwinEdgeRow>(tenantId, TwinCacheKeys.Edges);
            builtUtc = cache.Get<string>(tenantId, TwinCacheKeys.BuiltUtc)?.FirstOrDefault();
            if (nodes is not null) source = "cache";
        }
        if (nodes is null)
        {
            var p = TwinStore.Load();
            if (p is not null) { nodes = p.Nodes; edges = p.Edges; builtUtc = p.BuiltUtc; source = "twinjson"; }
        }
        // OFFLINE fallback: no persisted twin projection yet (never warmed) — assemble it
        // on the fly from the M12.1 per-screen caches (managed-devices/policies/CA/filters/
        // groups). Graph-free, so /twin/stats + analytics are non-empty with the network
        // unplugged and signed out. A later warm rebuild replaces this with the richer
        // (memberOf + full assignment) projection under the twin's own cache keys.
        if ((nodes is null || nodes.Count == 0) && cache is not null && cache.IsAvailable && tenantId is not null)
        {
            var (bn, be) = TwinBuilder.FromCache(cache, tenantId);
            if (bn.Count > 0) { nodes = bn; edges = be; source = "assembled"; }
        }
        nodes ??= new();
        edges ??= new();

        if (snapshotCounts is not null)
            foreach (var n in nodes)
                if (snapshotCounts.TryGetValue(n.Id, out var c)) n.Props["snapshotCount"] = c.ToString();

        return new TwinGraph(nodes, edges, source, builtUtc);
    }
}
