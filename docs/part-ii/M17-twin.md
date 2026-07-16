# M17 — Twin: tenant digital twin + graph analytics

> Every node and edge already flows through the read services into the store/cache — but nothing **assembles** them. M17 builds one local graph of the tenant and runs the cross-cutting analytics no per-screen view can: orphans, conflicts, cycles, CA escape paths, coverage gaps, drift hotspots — queryable, offline, and the substrate the M18 AI reasons over.

---

## What it builds on

Today the engine reads the tenant **one slice at a time**. Every read service projects a Graph
type down to a normalized DTO and lands it in the cache, the snapshot store, or both — but each
read answers exactly one screen's question and then forgets the rest of the tenant existed:

- **Groups + membership edges** — `IGroupService.ListGroupMembersAsync` (`service/Core/Services/IGroupService.cs:26`)
  returns `GroupMemberInfo` rows (`MemberType` ∈ User|Device|Group, `Id`, `Status`), and
  `GetMemberCountsAsync` returns typed counts. Nested groups come back as members with
  `MemberType == "Group"` — the raw material for `memberOf` edges, but no one walks them transitively.
- **Assignment edges** — `IAssignmentCheckerService` (`service/Core/Services/IAssignmentCheckerService.cs`)
  already computes nearly every analytic *as a flat report*: `GetUnassignedPoliciesAsync` (:71),
  `GetEmptyGroupAssignmentsAsync` (:79), `CompareGroupAssignmentsAsync` (:88), plus the
  `All Users` / `All Devices` rollups. Each returns `List<AssignmentReportRow>`
  (`service/Core/Models/AssignmentReportRow.cs`) — a denormalized row with `PolicyId`, `GroupId`,
  `AssignmentReason` ("Excluded" / "Group Assignment" / "All Users"). These rows *are* edges; they
  are just never indexed into a structure you can traverse.
- **Name resolution** — `IDirectoryObjectResolver.ResolveAsync` (`service/Core/Services/IDirectoryObjectResolver.cs:17`)
  batch-resolves up to 1000 object GUIDs to display names. The twin uses it once to label nodes,
  not per-screen.
- **Normalized assignment shape** — `crates/api-types/src/lib.rs:377` `Assignment { kind, groupId,
  filterId, filterMode, intent }` is the wire form of an edge target; `GroupDetail` (:410) +
  `GroupMember` (:399) are the wire form of a Group node and its membership edges.
- **Offline sources** — `ICacheService` (`service/Core/Services/ICacheService.cs:19`) holds the
  last-fetched list per `{tenantId}|{dataType}` in LiteDB (`Get<T>` / `GetSingle<T>`), and
  `SnapshotStore` (`service/Store/SnapshotStore.cs`) holds the append-only, content-hashed history
  of every config body (`GetSnapshottedObjectsAsync` :394 returns one row per object with the two
  newest bodies). Between them, **every node and edge the twin needs already lives on disk** — no
  Graph call is required to assemble the graph.

The gap is purely **assembly**. There is no type that says "Group G includes Policy P, excludes
Group H, and G has 0 members." M17 builds exactly that and nothing more.

---

## The graph model

A directed, typed property graph. Nodes carry the normalized DTO they were projected from; edges
carry their kind and any qualifier (filter, intent, exclude).

| Node type | Source | Key fields |
|---|---|---|
| `Device` | managed-device cache | `id`, `name`, `os`, `compliance` |
| `User`   | user cache | `id`, `upn`, `displayName` |
| `Group`  | `GroupDetail` / `ListGroupMembersAsync` | `id`, `name`, `groupType` (assigned\|dynamic), `memberCount` |
| `Policy` | config / compliance / app caches | `id`, `name`, `policyType`, `platform` |
| `CAPolicy` | conditional-access cache | `id`, `name`, `state`, `grantControls` |
| `Filter` | assignment-filter cache | `id`, `name`, `rule` |

| Edge type | From → To | Qualifier | Source |
|---|---|---|---|
| `memberOf`   | User/Device/Group → Group | — | `GroupMemberInfo` (`MemberType`) |
| `includes`   | Policy/CAPolicy → Group/AllUsers/AllDevices | `filterId?`, `intent?` | `AssignmentReportRow` (reason ≠ Excluded) |
| `excludes`   | Policy/CAPolicy → Group | — | `AssignmentReportRow` (reason = "Excluded") |
| `assignedTo` | Policy → User/Device (effective) | derived | transitive `includes` ∘ `memberOf` |
| `targetedBy` | Group → Policy (reverse of includes) | — | inverted edge, materialized for neighborhood queries |

Sample node + edge (the on-disk twin row form):

```json
{
  "nodes": [
    { "id": "grp-7a3", "type": "Group", "name": "All Sales Laptops",
      "props": { "groupType": "assigned", "memberCount": 0 } },
    { "id": "pol-c19", "type": "Policy", "name": "Win11 BitLocker Baseline",
      "props": { "policyType": "Device Configuration", "platform": "Windows" } }
  ],
  "edges": [
    { "from": "pol-c19", "to": "grp-7a3", "type": "includes",
      "props": { "filterId": null, "intent": null } }
  ]
}
```

Adjacency is stored as two index maps — `out[nodeId] → [edge]` and `in[nodeId] → [edge]` — so both
"what does P target?" and "what targets G?" are O(1) lookups, and effective-assignment expansion is
a bounded BFS over `memberOf` then `includes`/`excludes`.

---

## Storage

Two options; recommend **projection-over-store first**.

**A. Projection over store + cache (recommended).** The twin is a *materialized view*, rebuilt by
reading the cached lists (`ICacheService.Get<T>`) and group memberships, never by calling Graph.
A `TwinBuilder` runs the same assembly the report methods almost do today, but writes the result
into the two adjacency maps held in memory (and serialized to a single `twin.json` blob in
`%LocalAppData%\cmProjectX\` for warm starts). Rebuild is cheap (seconds for a mid-size tenant), so
freshness = "rebuild on demand or after a sync." This reuses everything: no new persistence engine,
no schema migration, fully offline, and the graph is *derived* so it can never drift from the
source DTOs it was projected from. The snapshot store additionally gives the twin a **time axis** —
`GetSnapshotsForObjectAsync` (`service/Store/SnapshotStore.cs:368`) lets a node carry its change
history, which is what makes drift-hotspot analytics possible.

**B. Embedded graph engine (deferred).** Bolt an in-process graph DB (e.g. a Kùzu/SQLite-graph
layer) and persist nodes/edges as first-class rows with a real query language. More power
(arbitrary multi-hop Cypher-style queries), but a new dependency, a new store to keep in lockstep
with the cache, and another thing to invalidate. **Not** for M17 — revisit only if `POST /twin/query`
demand outgrows the canned analytics.

Decision: **A.** The twin is a view, not a system of record. The store (`SnapshotStore`) and the
cache (`ICacheService`) remain the only durable truth; the twin is rebuildable from them at any
time and is therefore allowed to be lossy/stale-and-refreshable. Offline is free because both
sources are already on disk.

---

## Analytics queries

Each is a graph traversal over the materialized adjacency maps. Names are stable query ids for
`GET /twin/analytics/{query}`.

| Query id | Question | How computed |
|---|---|---|
| `orphaned-policies` | Which policies are assigned to nothing, or only to empty/deleted groups? | Policy nodes with no `includes` edge, OR every `includes` target is a Group with `memberCount == 0` (or an unresolved id). Folds `GetUnassignedPoliciesAsync` + `GetEmptyGroupAssignmentsAsync` into one structural test. |
| `redundant-assignments` | Which policies target the same effective set twice (e.g. a group **and** a parent it's nested under)? | For each policy, expand each `includes` group's transitive members; flag pairs where one member-set ⊆ another. |
| `conflicting-assignments` | Where does a policy both include and exclude overlapping populations, or two policies of the same type contend for the same device? | Per policy: intersect `includes` member-sets with `excludes` member-sets (non-empty → contradictory targeting). Cross-policy: same `policyType` + `platform` whose effective device-sets intersect. |
| `assignment-cycles` | Are there nested-group membership cycles that make effective assignment ill-defined? | Tarjan SCC over the `memberOf` subgraph; any SCC of size > 1 is a cycle. |
| `ca-escape-paths` | Which Conditional-Access policies can be bypassed via exclusions or gaps (e.g. an excluded group whose membership an attacker can join)? | Walk `CAPolicy --excludes--> Group`; flag excluded groups that are dynamic/self-serviceable or that grant elevated roles; flag users covered by *no* enforcing CA policy. |
| `coverage-gaps` | Which devices/users are targeted by **no** policy of a required class (e.g. no compliance policy)? | Compute the union of effective `assignedTo` device-sets for `policyType == "Compliance Policy"`; the complement against all `Device` nodes is the gap. |
| `drift-hotspots` | Which objects changed most often over the retained window? | Rank nodes by `SnapshotCount` from `GetSnapshottedObjectsAsync` (`SnapshotStore.cs:394`); join onto twin nodes to attribute drift to assignment reach (a high-drift policy targeting `AllDevices` is hotter than one targeting an empty group). |

All seven run against the in-memory graph; none calls Graph. `coverage-gaps`, `redundant-`, and
`conflicting-assignments` share one helper — `EffectiveMembers(groupId)` — a memoized transitive
`memberOf` expansion.

---

## Contract additions

`contract/openapi.yaml` is the source of truth; these add a `/twin/*` surface group. DTOs are
camelCase and mirrored in `crates/api-types/src/lib.rs` + `service/Api/Contracts.cs`.

| Method | Path | Purpose | Response |
|---|---|---|---|
| `POST` | `/twin/rebuild` | Re-materialize the graph from cache + store (offline). | `TwinStats { nodeCount, edgeCount, builtUtc, stale }` |
| `GET`  | `/twin/stats` | Node/edge counts + freshness without rebuilding. | `TwinStats` |
| `GET`  | `/twin/analytics/{query}` | Run one canned analytic (ids above). | `TwinAnalyticsResult` |
| `GET`  | `/twin/node/{id}` | Neighborhood: a node + its in/out edges + 1-hop neighbors. | `TwinNeighborhood` |
| `POST` | `/twin/query` | Structural query (typed traversal: from-type, edge-path, predicates). Canned-vocabulary, **not** arbitrary Cypher in M17. | `TwinQueryResult { nodes, edges }` |

`GET /twin/analytics/{query}` returns `409`-as-empty when signed out and the cache is cold (same
contract convention the MCP read tools rely on, per `docs/PLUGINS-MCP.md`).

---

## Sample data

**`orphaned-policies`** — `GET /twin/analytics/orphaned-policies`:

```json
{
  "query": "orphaned-policies",
  "builtUtc": "2026-06-24T09:12:04Z",
  "source": "cache",
  "results": [
    { "id": "pol-c19", "name": "Win11 BitLocker Baseline",
      "policyType": "Device Configuration", "platform": "Windows",
      "reason": "emptyGroupOnly",
      "detail": "Only assignment targets group 'All Sales Laptops' (grp-7a3) which has 0 members." },
    { "id": "pol-44b", "name": "Legacy Edge Config",
      "policyType": "Settings Catalog", "platform": "Windows",
      "reason": "noAssignments",
      "detail": "Policy has zero assignment targets." },
    { "id": "pol-901", "name": "iOS Wi-Fi (Pilot)",
      "policyType": "Device Configuration", "platform": "iOS",
      "reason": "deletedGroup",
      "detail": "Assignment targets group id 'a91...e2' that no longer resolves." }
  ]
}
```

**`conflicting-assignments`** — `GET /twin/analytics/conflicting-assignments`:

```json
{
  "query": "conflicting-assignments",
  "builtUtc": "2026-06-24T09:12:04Z",
  "results": [
    { "policyId": "pol-c19", "policyName": "Win11 BitLocker Baseline",
      "kind": "includeExcludeOverlap",
      "includeGroup": { "id": "grp-eng", "name": "All Engineering", "effectiveMembers": 412 },
      "excludeGroup": { "id": "grp-eng-mac", "name": "Engineering macOS", "effectiveMembers": 38 },
      "overlap": 38,
      "detail": "38 devices are both included (via All Engineering) and excluded (Engineering macOS) — net excluded; likely unintended." },
    { "policyId": "pol-77a", "policyName": "Defender ASR Rules",
      "kind": "crossPolicyContention",
      "contendsWith": { "id": "pol-77b", "name": "Defender ASR (Strict)" },
      "policyType": "Settings Catalog", "platform": "Windows",
      "overlap": 156,
      "detail": "Two Settings Catalog policies of the same platform target 156 overlapping devices; last-writer-wins on conflicting settings." }
  ]
}
```

**Node neighborhood** — `GET /twin/node/grp-7a3` (a Group with members + targeting policies):

```json
{
  "node": { "id": "grp-7a3", "type": "Group", "name": "All Sales Laptops",
            "props": { "groupType": "assigned", "memberCount": 0 } },
  "inbound": [
    { "from": "pol-c19", "to": "grp-7a3", "type": "includes",
      "fromNode": { "id": "pol-c19", "type": "Policy", "name": "Win11 BitLocker Baseline" } },
    { "from": "pol-310", "to": "grp-7a3", "type": "excludes",
      "fromNode": { "id": "pol-310", "type": "Policy", "name": "Default Compliance (Win)" } }
  ],
  "outbound": [],
  "members": [],
  "warnings": [ "Group has 0 members; 1 policy targets it exclusively (see orphaned-policies)." ]
}
```

(Members are empty here precisely because this group is the injected orphan case — a populated
group would carry `GroupMember` rows from `ListGroupMembersAsync` under `members`.)

---

## How the AI uses it (M18)

The M18 AI reasons over the **twin**, not the 50 raw endpoints. Today (M13) the MCP read tools
proxy one list endpoint each (`docs/PLUGINS-MCP.md`); to answer "is anything misconfigured?" an AI
would have to call `list_objects` across every surface and reconstruct the graph in its own
context — N round-trips, N× tokens, and it re-derives the same joins every turn.

With the twin, **one query replaces N list calls**. "Which policies are orphaned?" → one
`GET /twin/analytics/orphaned-policies`. "What targets this group and is it safe to delete?" → one
`GET /twin/node/{id}` returning the full neighborhood with warnings. The twin becomes the AI's
working memory of tenant structure: it answers the *structural* questions (reach, overlap, gaps)
deterministically, leaving the model to do judgment ("this overlap looks intentional, that one
doesn't") rather than data assembly. It also slots into the existing HITL rail — an AI that spots a
conflict can `propose_*` the fix, which still lands in the M13.2 approval inbox.

---

## Definition of done

- `POST /twin/rebuild` materializes the graph **offline** — from `ICacheService` + `SnapshotStore`
  only, with the network unplugged and signed-out — and `GET /twin/stats` reports non-zero
  node/edge counts.
- `GET /twin/analytics/orphaned-policies` and `GET /twin/analytics/conflicting-assignments` return
  correct results against cache/store alone (no Graph call in the request path; verified by a test
  that fails if any Core read service is invoked during the query).
- **Injection test:** seed the cache with a policy assigned only to a 0-member group → it appears
  in `orphaned-policies` with `reason: "emptyGroupOnly"`. Seed a policy with an include group whose
  members ⊆ its exclude group → it appears in `conflicting-assignments` with a non-zero `overlap`.
- `GET /twin/node/{id}` returns a group's inbound `includes`/`excludes` edges and its members.
- DTOs (`TwinStats`, `TwinAnalyticsResult`, `TwinNeighborhood`) exist in `contract/openapi.yaml`,
  mirrored byte-for-byte in `crates/api-types` and `Contracts.cs`.

---

## Open questions

1. **Twin storage choice.** Projection-into-memory-plus-`twin.json` (recommended) vs. an embedded
   graph DB. Lean: ship the projection; reconsider B only if `POST /twin/query` needs arbitrary
   multi-hop traversal the canned analytics can't express.
2. **Freshness / staleness.** When is the twin "stale"? Options: rebuild on every analytics call
   (always fresh, slower), rebuild after each sync (`sync_state` watermark in `SnapshotStore`), or
   TTL like the cache. Lean: serve from `twin.json`, stamp `builtUtc`, expose `stale` in
   `TwinStats`, rebuild on demand or post-sync. How does staleness interact with the M13 cache's
   own TTL/eviction (`CacheInvalidation.cs`)?
3. **Graph query language.** Is `POST /twin/query` a fixed typed-traversal vocabulary (from-type +
   edge-path + predicates) or do we expose something Cypher-like later? M17 ships the typed form;
   the open question is whether M18's AI needs more expressiveness than the canned analytics + typed
   query provide.
4. **Edge fidelity for filters/intents.** How precisely must `includes` edges model assignment
   filters (`filterId`/`filterMode`) and app intents when computing effective member-sets? Coarse
   (ignore filters) is simpler but over-reports coverage; exact requires evaluating filter rules
   against device properties.
5. **Effective-membership cost.** Transitive `memberOf` expansion is the hot path for three
   analytics. Memoize per rebuild — but very large nested groups could blow the BFS bound; cap depth
   or precompute closure?
