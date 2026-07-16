using System.Text.Json.Nodes;
using Microsoft.Graph.Beta.Models;
using Intune.Commander.Core.Services;
// Microsoft.Graph.Beta.Models also defines a `WebApplication` type; alias to the
// ASP.NET host so the extension method resolves (see DevicesEndpoints for why).
using WebApplication = Microsoft.AspNetCore.Builder.WebApplication;
// GraphServiceClient is fully-qualified at every use (no `using Microsoft.Graph.Beta`)
// so it never shadows IResult / breaks Minimal API overload resolution.

namespace CmProjectX.Api;

// M16 Foresight — the blast-radius simulator. Resolves a proposed change's target
// set vs the live target set over the assignment graph and reports WHO it hits,
// before any write. Pure read: GroupService member expansion + the per-surface
// assignments read (loopback) + tenant $count. No Graph write. The result rides on
// the M6 gate / M13 inbox / M18 severity decision.
public static class SimulateEndpoints
{
    public static void MapSimulate(this WebApplication app)
    {
        // POST /simulate — blast radius of any proposed write.
        app.MapPost("/simulate", async (SimulateRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            if (string.IsNullOrWhiteSpace(req.Verb) || string.IsNullOrWhiteSpace(req.Path))
                return ApiResults.BadRequest("verb and path are required");
            var report = await new BlastRadiusSimulator(g, cache, auth.ActiveProfile?.TenantId).SimulateAsync(req, ct);
            return Results.Ok(report);
        });

        // POST /simulate/assignment — convenience alias (forces verb=assign).
        app.MapPost("/simulate/assignment", async (SimulateRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            if (string.IsNullOrWhiteSpace(req.Path))
                return ApiResults.BadRequest("path is required");
            var report = await new BlastRadiusSimulator(g, cache, auth.ActiveProfile?.TenantId)
                .SimulateAsync(req with { Verb = "assign" }, ct);
            return Results.Ok(report);
        });

        // GET /simulate/graph?objectId=&path= — the live target set a sim diffs against.
        app.MapGet("/simulate/graph", async (string objectId, string? path, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var view = await new BlastRadiusSimulator(g, cache, auth.ActiveProfile?.TenantId)
                .ResolveLiveTargetViewAsync(objectId, path, ct);
            return Results.Ok(view);
        });
    }
}

// The engine. Constructed per call like AssignmentExplorerEndpoints constructs its
// checker: `new BlastRadiusSimulator(graph, cache, tenantId)`. No DI.
internal sealed class BlastRadiusSimulator
{
    private readonly Microsoft.Graph.Beta.GraphServiceClient _g;
    private readonly ICacheService? _cache;
    private readonly string? _tenantId;
    private int _tenantUsers = -1, _tenantDevices = -1;

    public BlastRadiusSimulator(Microsoft.Graph.Beta.GraphServiceClient g, ICacheService? cache, string? tenantId)
    {
        _g = g; _cache = cache; _tenantId = tenantId;
    }

    // One assignment/target leaf. Kind ∈ group | exclusionGroup | allUsers | allDevices
    // | user | excludeUser (the last two are CA direct-user targets).
    private sealed record Tgt(string Kind, string? Id);

    private sealed class ResolvedSet
    {
        public HashSet<string> IncludeUserIds = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> IncludeDeviceIds = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ExcludeUserIds = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ExcludeDeviceIds = new(StringComparer.OrdinalIgnoreCase);
        public bool AllUsers, AllDevices;
        // groupId -> display (for conflict/redundancy text); principal id -> (type,name,upn)
        public List<(string Kind, string Id)> Targets = new();
        public Dictionary<string, (string Type, string Name, string? Upn)> Principals = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> EffectiveUsers()
        {
            var s = new HashSet<string>(IncludeUserIds, StringComparer.OrdinalIgnoreCase);
            s.ExceptWith(ExcludeUserIds);
            return s;
        }
        public HashSet<string> EffectiveDevices()
        {
            var s = new HashSet<string>(IncludeDeviceIds, StringComparer.OrdinalIgnoreCase);
            s.ExceptWith(ExcludeDeviceIds);
            return s;
        }
    }

    public async Task<BlastRadiusReportDto> SimulateAsync(SimulateRequest req, CancellationToken ct)
    {
        var path = Normalize(req.Path);
        var proposedTargets = ParseProposedTargets(req, path);
        var liveTargets = await ReadLiveTargetsAsync(req.Verb, path, req.ObjectId, ct);

        var proposed = await ResolveAsync(proposedTargets, ct);
        var live = await ResolveAsync(liveTargets, ct);

        var newlyUsers = proposed.EffectiveUsers(); newlyUsers.ExceptWith(live.EffectiveUsers());
        var newlyDevices = proposed.EffectiveDevices(); newlyDevices.ExceptWith(live.EffectiveDevices());
        var goneUsers = live.EffectiveUsers(); goneUsers.ExceptWith(proposed.EffectiveUsers());
        var goneDevices = live.EffectiveDevices(); goneDevices.ExceptWith(proposed.EffectiveDevices());

        int affectedUsers = newlyUsers.Count;
        int affectedDevices = newlyDevices.Count;
        int noLonger = goneUsers.Count + goneDevices.Count;

        // All Users / All Devices: count-based (enumerating the tenant is prohibitive).
        // newly = AllTenant \ (live ∪ proposedExclude); count via the EXACT union size of
        // the explicit id sets, so a principal in both live-effective and the proposed
        // exclude isn't subtracted twice (the union de-dups it).
        if (proposed.AllUsers && !live.AllUsers)
            affectedUsers += Math.Max(0, await TenantUsersAsync(ct) - Union(live.EffectiveUsers(), proposed.ExcludeUserIds));
        if (proposed.AllDevices && !live.AllDevices)
            affectedDevices += Math.Max(0, await TenantDevicesAsync(ct) - Union(live.EffectiveDevices(), proposed.ExcludeDeviceIds));
        // no-longer = liveAll \ (proposed ∪ liveExclude): live "All" covers everyone except
        // live's own excludes; subtract whoever the proposed set still targets.
        if (live.AllUsers && !proposed.AllUsers)
            noLonger += Math.Max(0, await TenantUsersAsync(ct) - Union(proposed.EffectiveUsers(), live.ExcludeUserIds));
        if (live.AllDevices && !proposed.AllDevices)
            noLonger += Math.Max(0, await TenantDevicesAsync(ct) - Union(proposed.EffectiveDevices(), live.ExcludeDeviceIds));

        var sample = BuildSample(newlyUsers, newlyDevices, proposed);
        var conflicts = DetectConflicts(proposed, live);
        var redundancies = DetectRedundancies(proposedTargets, liveTargets, proposed);
        var crossPolicyImpacts = await DetectCrossPolicyImpactsAsync(path, req, proposed, affectedUsers, affectedDevices, ct);

        var severity = Score(affectedUsers + affectedDevices, conflicts.Count, path);
        var summary = Summarize(path, req.Verb, affectedUsers, affectedDevices, noLonger, conflicts.Count, redundancies.Count,
            proposed.AllUsers, proposed.AllDevices);

        return new BlastRadiusReportDto(
            severity, summary, affectedUsers, affectedDevices, noLonger,
            sample, conflicts, redundancies, crossPolicyImpacts);
    }

    public async Task<object> ResolveLiveTargetViewAsync(string objectId, string? path, CancellationToken ct)
    {
        var p = Normalize(path ?? "");
        var targets = await ReadLiveTargetsAsync("update", p, objectId, ct);
        var set = await ResolveAsync(targets, ct);
        return new
        {
            objectId,
            path = p.Length == 0 ? null : p,
            includeGroups = targets.Where(t => t.Kind == "group").Select(t => t.Id).Where(id => id is not null).ToArray(),
            excludeGroups = targets.Where(t => t.Kind == "exclusionGroup").Select(t => t.Id).Where(id => id is not null).ToArray(),
            allUsers = set.AllUsers,
            allDevices = set.AllDevices,
            affectedUserCount = set.EffectiveUsers().Count,
            affectedDeviceCount = set.EffectiveDevices().Count,
        };
    }

    // ── target parsing ────────────────────────────────────────────────────────

    private static string Normalize(string path)
    {
        var p = path.Trim();
        if (p.Length == 0) return "";
        return p.StartsWith('/') ? p : "/" + p;
    }

    private static List<Tgt> ParseProposedTargets(SimulateRequest req, string path)
    {
        var targets = new List<Tgt>();
        if (string.IsNullOrWhiteSpace(req.BodyJson)) return targets;
        JsonNode? root;
        try { root = JsonNode.Parse(req.BodyJson); } catch { return targets; }
        if (root is null) return targets;

        if (path == "/conditional-access")
        {
            var u = root["conditions"]?["users"];
            AddCaUsers(targets, u?["includeUsers"] as JsonArray, isExclude: false);
            AddCaUsers(targets, u?["excludeUsers"] as JsonArray, isExclude: true);
            AddGroupArray(targets, u?["includeGroups"] as JsonArray, "group");
            AddGroupArray(targets, u?["excludeGroups"] as JsonArray, "exclusionGroup");
            return targets;
        }

        // Assignment surfaces: body is an Assignment[] (verb=assign), { assignments:[…] },
        // or a single Assignment object. Every index is guarded so a malformed body
        // (scalar, array-of-array, missing kind) yields empty targets, never a 500.
        var arr = root as JsonArray ?? (root as JsonObject)?["assignments"] as JsonArray;
        if (arr is not null)
            foreach (var node in arr) AddAssignment(targets, node);
        else if (root is JsonObject obj && obj["kind"] is not null)
            AddAssignment(targets, obj);
        return targets;
    }

    private static void AddAssignment(List<Tgt> targets, JsonNode? node)
    {
        if (node is not JsonObject o) return;
        if (o["kind"]?.GetValue<string>() is { } kind)
            targets.Add(new Tgt(kind, TryStr(o["groupId"])));
    }

    private async Task<List<Tgt>> ReadLiveTargetsAsync(string verb, string path, string? objectId, CancellationToken ct)
    {
        var targets = new List<Tgt>();
        if (string.Equals(verb, "create", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(objectId))
            return targets;

        if (path == "/conditional-access")
        {
            try
            {
                var pol = await new ConditionalAccessPolicyService(_g).GetPolicyAsync(objectId, ct);
                var u = pol?.Conditions?.Users;
                AddCaUserList(targets, u?.IncludeUsers, isExclude: false);
                AddCaUserList(targets, u?.ExcludeUsers, isExclude: true);
                AddGroupList(targets, u?.IncludeGroups, "group");
                AddGroupList(targets, u?.ExcludeGroups, "exclusionGroup");
            }
            catch { /* best-effort */ }
            return targets;
        }

        // Assignment surfaces: reuse the per-surface assignments read over loopback.
        try
        {
            var resp = await Loopback.Http.GetAsync($"{path}/{Uri.EscapeDataString(objectId)}/assignments", ct);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync(ct);
                if (JsonNode.Parse(json) is JsonArray arr)
                    foreach (var node in arr) AddAssignment(targets, node);
            }
        }
        catch { /* best-effort: live target unknown → treat as empty */ }
        return targets;
    }

    private static void AddCaUsers(List<Tgt> targets, JsonArray? arr, bool isExclude)
    {
        if (arr is null) return;
        foreach (var v in arr)
            ClassifyCaUser(targets, TryStr(v), isExclude);
    }
    private static void AddCaUserList(List<Tgt> targets, List<string>? ids, bool isExclude)
    {
        foreach (var id in ids ?? new()) ClassifyCaUser(targets, id, isExclude);
    }
    private static void ClassifyCaUser(List<Tgt> targets, string? id, bool isExclude)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        if (id == "All") { if (!isExclude) targets.Add(new Tgt("allUsers", null)); return; }
        if (id is "None" or "GuestsOrExternalUsers") return;
        if (Guid.TryParse(id, out _)) targets.Add(new Tgt(isExclude ? "excludeUser" : "user", id));
    }
    private static void AddGroupArray(List<Tgt> targets, JsonArray? arr, string kind)
    {
        if (arr is null) return;
        foreach (var v in arr)
            if (TryStr(v) is { } id && Guid.TryParse(id, out _)) targets.Add(new Tgt(kind, id));
    }
    private static void AddGroupList(List<Tgt> targets, List<string>? ids, string kind)
    {
        foreach (var id in ids ?? new())
            if (Guid.TryParse(id, out _)) targets.Add(new Tgt(kind, id));
    }
    private static string? TryStr(JsonNode? n)
    {
        try { return n?.GetValue<string>(); } catch { return null; }
    }

    // ── resolution ──────────────────────────────────────────────────────────

    private async Task<ResolvedSet> ResolveAsync(List<Tgt> targets, CancellationToken ct)
    {
        var set = new ResolvedSet { Targets = targets.Select(t => (t.Kind, t.Id ?? "")).ToList() };
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in targets)
        {
            switch (t.Kind)
            {
                case "allUsers": set.AllUsers = true; break;
                case "allDevices": set.AllDevices = true; break;
                case "user": if (t.Id is { } u) { set.IncludeUserIds.Add(u); set.Principals.TryAdd(u, ("user", u, u)); } break;
                case "excludeUser": if (t.Id is { } eu) set.ExcludeUserIds.Add(eu); break;
                case "exclusionGroup": if (t.Id is { } eg) await ResolveGroupAsync(eg, set, isExclude: true, visited, 0, ct); break;
                default: if (t.Id is { } gid) await ResolveGroupAsync(gid, set, isExclude: false, visited, 0, ct); break;
            }
        }
        return set;
    }

    private async Task ResolveGroupAsync(string groupId, ResolvedSet set, bool isExclude, HashSet<string> visited, int depth, CancellationToken ct)
    {
        if (depth > 5 || !visited.Add($"{(isExclude ? "x" : "i")}:{groupId}")) return; // bound nesting; dedup
        List<GroupMemberInfo> members;
        try { members = await new GroupService(_g).ListGroupMembersAsync(groupId, ct); }
        catch { return; }
        foreach (var m in members)
        {
            switch (m.MemberType)
            {
                case "User":
                    (isExclude ? set.ExcludeUserIds : set.IncludeUserIds).Add(m.Id);
                    if (!isExclude) set.Principals.TryAdd(m.Id, ("user", m.DisplayName, string.IsNullOrEmpty(m.SecondaryInfo) ? null : m.SecondaryInfo));
                    break;
                case "Device":
                    (isExclude ? set.ExcludeDeviceIds : set.IncludeDeviceIds).Add(m.Id);
                    if (!isExclude) set.Principals.TryAdd(m.Id, ("device", m.DisplayName, null));
                    break;
                case "Group":
                    await ResolveGroupAsync(m.Id, set, isExclude, visited, depth + 1, ct);
                    break;
            }
        }
    }

    private async Task<int> TenantUsersAsync(CancellationToken ct)
    {
        if (_tenantUsers >= 0) return _tenantUsers;
        try { _tenantUsers = await _g.Users.Count.GetAsync(rc => rc.Headers.Add("ConsistencyLevel", "eventual"), ct) ?? 0; }
        catch { _tenantUsers = 0; }
        return _tenantUsers;
    }
    private async Task<int> TenantDevicesAsync(CancellationToken ct)
    {
        if (_tenantDevices >= 0) return _tenantDevices;
        try { _tenantDevices = await _g.Devices.Count.GetAsync(rc => rc.Headers.Add("ConsistencyLevel", "eventual"), ct) ?? 0; }
        catch { _tenantDevices = 0; }
        return _tenantDevices;
    }

    // |a ∪ b| over id sets (de-dups overlap, so a shared id isn't counted twice).
    private static int Union(IEnumerable<string> a, IEnumerable<string> b)
    {
        var s = new HashSet<string>(a, StringComparer.OrdinalIgnoreCase);
        s.UnionWith(b);
        return s.Count;
    }

    // ── classification ────────────────────────────────────────────────────────

    private static List<SamplePrincipalDto> BuildSample(HashSet<string> newlyUsers, HashSet<string> newlyDevices, ResolvedSet proposed)
    {
        const string reason = "newly in the target set";
        var sample = new List<SamplePrincipalDto>();
        foreach (var id in newlyUsers.Take(5))
        {
            var (_, name, upn) = proposed.Principals.TryGetValue(id, out var p) ? p : ("user", id, (string?)id);
            sample.Add(new SamplePrincipalDto("user", name, upn, id, reason));
        }
        foreach (var id in newlyDevices.Take(5))
        {
            var (_, name, _) = proposed.Principals.TryGetValue(id, out var p) ? p : ("device", id, (string?)null);
            sample.Add(new SamplePrincipalDto("device", name, null, id, reason));
        }
        return sample;
    }

    private static List<SimConflictDto> DetectConflicts(ResolvedSet proposed, ResolvedSet live)
    {
        var conflicts = new List<SimConflictDto>();
        var hasExclude = proposed.ExcludeUserIds.Count + proposed.ExcludeDeviceIds.Count
                       + live.ExcludeUserIds.Count + live.ExcludeDeviceIds.Count > 0;

        // include All * while an exclude exists → the exclude still applies (confirm intent).
        if ((proposed.AllUsers || proposed.AllDevices) && hasExclude)
            conflicts.Add(new SimConflictDto("includeExcludeOverlap",
                "Proposed include All Users/Devices overlaps an existing exclude group; the excluded principals remain excluded — confirm intent."));

        // a principal landing in both an include and an exclude in the proposed set.
        var bothU = new HashSet<string>(proposed.IncludeUserIds, StringComparer.OrdinalIgnoreCase);
        bothU.IntersectWith(proposed.ExcludeUserIds);
        var bothD = new HashSet<string>(proposed.IncludeDeviceIds, StringComparer.OrdinalIgnoreCase);
        bothD.IntersectWith(proposed.ExcludeDeviceIds);
        if (bothU.Count + bothD.Count > 0)
            conflicts.Add(new SimConflictDto("includeExcludeOverlap",
                $"{bothU.Count + bothD.Count} principal(s) are in both an include and an exclude target; they will be excluded."));

        return conflicts;
    }

    private static List<SimRedundancyDto> DetectRedundancies(List<Tgt> proposedTargets, List<Tgt> liveTargets, ResolvedSet proposed)
    {
        var redundancies = new List<SimRedundancyDto>();

        // Subset-covered: explicit includes already covered by include All Users/Devices.
        // Key on the RESOLVED include id counts (users covered only by AllUsers, devices
        // only by AllDevices), so a device-only group isn't flagged under All-Users-only.
        var coveredUsers = proposed.AllUsers ? proposed.IncludeUserIds.Count : 0;
        var coveredDevices = proposed.AllDevices ? proposed.IncludeDeviceIds.Count : 0;
        if (coveredUsers + coveredDevices > 0)
            redundancies.Add(new SimRedundancyDto("subsetCovered",
                $"{coveredUsers + coveredDevices} explicitly-included principal(s) are already covered by include All Users/Devices and are redundant."));

        // re-adding a group already present in the live includes.
        var liveGroups = liveTargets.Where(t => t.Kind == "group").Select(t => t.Id).Where(id => id is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var readded = proposedTargets.Where(t => t.Kind == "group" && t.Id is not null && liveGroups.Contains(t.Id!)).ToList();
        if (readded.Count > 0)
            redundancies.Add(new SimRedundancyDto("alreadyAssigned",
                $"{readded.Count} include group(s) are already assigned to this object; re-adding them is a no-op."));

        return redundancies;
    }

    // ── cross-policy cascade ────────────────────────────────────────────────────
    // The second-order blast radius (M16-simulator.md Examples A/B). Compliance and
    // Conditional Access are coupled through the "compliantDevice" grant control:
    //   • A CA change that (newly) requires a compliant device makes every in-scope
    //     user's sign-in depend on the device-compliance policies (Example A).
    //   • A compliance change that flips devices non-compliant makes those devices'
    //     owners fail any *enabled* CA policy that requires a compliant device (Example B).
    // We enumerate the *other* policy type live and report the coupling, grounded in the
    // real tenant. Best-effort: the cascade is advisory and never fails the primary sim.
    // Cross-dimension precision (which exact device→owner will flip) is intentionally
    // left to a follow-up — see the M16 doc's tenant-scale open questions.
    private const int MaxCrossPolicy = 25;

    // Graph I/O for the cascade: enumerate the *other* policy type, then hand the pure
    // classification to Build{CaLockout,ComplianceFlip}Impacts (hermetically tested). The
    // cascade is advisory — any failure here yields an empty list, never a failed sim.
    private async Task<List<CrossPolicyImpactDto>> DetectCrossPolicyImpactsAsync(
        string path, SimulateRequest req, ResolvedSet proposed,
        int affectedUsers, int affectedDevices, CancellationToken ct)
    {
        try
        {
            if (path == "/conditional-access")
            {
                if (!ProposedRequiresCompliantDevice(req) || ProposedStateDisabled(req)) return new();

                // Did the live policy already require a compliant device? If it did and the
                // scope didn't widen, nothing new is gated. One extra GET on the CA path only.
                var liveRequires = false;
                if (!string.IsNullOrWhiteSpace(req.ObjectId))
                {
                    try
                    {
                        var live = await new ConditionalAccessPolicyService(_g).GetPolicyAsync(req.ObjectId, ct);
                        liveRequires = live is not null && RequiresCompliantDevice(live);
                    }
                    catch { /* treat as not-yet-required */ }
                }

                var comp = (await new CompliancePolicyService(_g).ListCompliancePoliciesAsync(ct))
                    .Select(cp => (cp.Id ?? "", cp.DisplayName ?? "(unnamed compliance policy)"))
                    .ToList();

                return BuildCaLockoutImpacts(
                    proposedRequiresCompliant: true, proposedDisabled: false, liveRequires,
                    proposed.AllUsers, proposed.EffectiveUsers().Count, affectedUsers, comp);
            }

            if (path == "/compliance-policies")
            {
                var ca = (await new ConditionalAccessPolicyService(_g).ListPoliciesAsync(ct))
                    .Where(RequiresCompliantDevice)
                    .Where(p => p.State == ConditionalAccessPolicyState.Enabled
                             || p.State == ConditionalAccessPolicyState.EnabledForReportingButNotEnforced)
                    .Select(p => (p.Id ?? "", p.DisplayName ?? "(unnamed CA policy)",
                                  ReportOnly: p.State == ConditionalAccessPolicyState.EnabledForReportingButNotEnforced))
                    .ToList();

                return BuildComplianceFlipImpacts(proposed.AllDevices, affectedDevices, ca);
            }
        }
        catch { /* best-effort: the cross-policy radius is advisory, never fail the primary sim */ }
        return new();
    }

    // Direction A (Example A) — a CA policy that (newly) requires a compliant device makes the
    // in-scope users' sign-in depend on device compliance; name the policies that decide it.
    // Pure: the requirement "newly bites" if the scope widened (affectedUsers>0) or the grant
    // was newly added to a policy that already targets a non-empty population.
    internal static List<CrossPolicyImpactDto> BuildCaLockoutImpacts(
        bool proposedRequiresCompliant, bool proposedDisabled, bool liveRequiresCompliant,
        bool proposedAllUsers, int proposedUserTargetCount, int affectedUsers,
        IReadOnlyList<(string Id, string Name)> compliancePolicies)
    {
        var impacts = new List<CrossPolicyImpactDto>();
        if (!proposedRequiresCompliant || proposedDisabled) return impacts;

        var newlyAddedGrant = !liveRequiresCompliant && (proposedAllUsers || proposedUserTargetCount > 0);
        if (affectedUsers == 0 && !newlyAddedGrant) return impacts;

        var scope = proposedAllUsers ? "All Users"
                  : affectedUsers > 0 ? $"{affectedUsers} newly-in-scope user(s)"
                  : "the in-scope users";
        if (compliancePolicies.Count == 0)
        {
            impacts.Add(new CrossPolicyImpactDto("", "(no device compliance policies)",
                $"This CA policy requires a compliant device for {scope}, but no device compliance policies exist — in-scope users with unmanaged or non-compliant devices will be blocked at next sign-in."));
        }
        else
        {
            foreach (var (id, name) in compliancePolicies.Take(MaxCrossPolicy))
                impacts.Add(new CrossPolicyImpactDto(id, name,
                    $"Sign-in for {scope} now requires a compliant device; any of them whose device fails this compliance policy will be blocked at next sign-in."));
        }
        return impacts;
    }

    // Direction B (Example B) — a compliance change that governs devices makes those devices'
    // owners fail any enabled CA policy that requires a compliant device. `caPolicies` are
    // already filtered to compliant-device + enabled/report-only by the caller. Pure.
    internal static List<CrossPolicyImpactDto> BuildComplianceFlipImpacts(
        bool proposedAllDevices, int affectedDevices,
        IReadOnlyList<(string Id, string Name, bool ReportOnly)> caPolicies)
    {
        var impacts = new List<CrossPolicyImpactDto>();
        if (affectedDevices == 0 && !proposedAllDevices) return impacts;

        var scope = proposedAllDevices ? "All Devices" : $"{affectedDevices} newly-targeted device(s)";
        foreach (var (id, name, reportOnly) in caPolicies.Take(MaxCrossPolicy))
        {
            var effect = reportOnly
                ? "would block their owners' sign-in once this policy is enforced"
                : "will block their owners' sign-in if they report non-compliant";
            impacts.Add(new CrossPolicyImpactDto(id, name + (reportOnly ? " (report-only)" : ""),
                $"This CA policy requires a compliant device; the {scope} governed by this compliance policy {effect} at next sign-in."));
        }
        return impacts;
    }

    // The proposed CA body requires a compliant device (grantControls.builtInControls ∋ compliantDevice).
    private static bool ProposedRequiresCompliantDevice(SimulateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.BodyJson)) return false;
        JsonNode? root;
        try { root = JsonNode.Parse(req.BodyJson); } catch { return false; }
        if (root?["grantControls"]?["builtInControls"] is not JsonArray arr) return false;
        foreach (var v in arr)
            if (string.Equals(TryStr(v), "compliantDevice", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // The proposed CA policy is disabled → the grant control never applies, no lockout.
    private static bool ProposedStateDisabled(SimulateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.BodyJson)) return false;
        try { return string.Equals(TryStr(JsonNode.Parse(req.BodyJson)?["state"]), "disabled", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    // BuiltInControls is a List<ConditionalAccessGrantControl?> (enum, not string) — compare
    // against the enum value, mirroring ControlGrantBlock.cs's .Contains(...) usage.
    private static bool RequiresCompliantDevice(ConditionalAccessPolicy ca)
        => ca.GrantControls?.BuiltInControls?.Contains(ConditionalAccessGrantControl.CompliantDevice) ?? false;

    private static string Score(int affected, int conflictCount, string path)
    {
        // Conditional Access tightening is inherently higher-stakes (lockout risk).
        var caBump = path == "/conditional-access" && affected > 0;
        var sev = affected >= 1000 ? "critical"
                : affected >= 100 ? "high"
                : affected >= 10 ? "medium"
                : affected >= 1 ? "low"
                : "info";
        if (caBump && sev is "low" or "medium") sev = "high";
        if (conflictCount > 0 && sev == "info") sev = "low";
        return sev;
    }

    private static string Summarize(string path, string verb, int users, int devices, int noLonger,
        int conflicts, int redundancies, bool allUsers, bool allDevices)
    {
        var surface = path.TrimStart('/');
        var scope = allUsers && allDevices ? "All Users + All Devices"
                  : allUsers ? "All Users" : allDevices ? "All Devices" : "the targeted groups";
        var parts = new List<string>
        {
            $"This {verb} on {surface} ({scope}) newly affects {users} user(s) and {devices} device(s)."
        };
        if (noLonger > 0) parts.Add($"{noLonger} principal(s) are no longer targeted.");
        if (conflicts > 0) parts.Add($"{conflicts} conflict(s) detected.");
        if (redundancies > 0) parts.Add($"{redundancies} redundancy(ies) detected.");
        if (users + devices == 0 && noLonger == 0 && conflicts == 0)
            parts.Add("No change to the target population.");
        return string.Join(" ", parts);
    }
}
