# M12.2 — Relationship store: groups, membership & assignment edges (implementation plan)

Implements **layer two** of [CACHE.md](./CACHE.md): a normalized, *mutable* SQLite relationship store
(`cache_groups`, `cache_memberships`, `cache_objects`, `cache_assignments`, `cache_state`) populated by
a new `GraphCacheSync` pass, plus the reverse/effective-assignment read endpoints it unlocks.
Server-side change; the client gets one new status field now and two screens later. Grounded by a
10-agent workflow + adversarial review; follows the house style of [CACHE-M12.1.md](./CACHE-M12.1.md).
Every `file:line` was verified in-repo.

> **Locked decisions (do not relitigate):**
> 1. **Direct edges only + read-time `WITH RECURSIVE` CTE** — store `group→user`, `group→device`,
>    `group-in-group` edges; expand the closure on read. Do **not** materialize the closure at sync
>    time. **Static membership only**; dynamic-group membership is a deliberate non-edge the closure
>    cannot synthesize (filled from a live/cached `TransitiveMemberOf` lookup at read).
> 2. **Lazy name resolution** via a `CachedNameResolver` wrapping `DirectoryObjectResolver` + a small
>    bounded resolved-name cache. **No** full tenant principal sync (deferred to M12.4/M12.5).
> 3. **Trigger = extend `POST /sync`** to run `GraphCacheSync` as a third pass inside
>    `GraphDeltaSync.RunAsync`. **Freshness = one nullable `lastRelationshipSyncUtc` on `GET /health`.**
>    No dedicated `/cache/refresh` or `/cache/status`.

> **Corrections applied from adversarial review (verdict: build-ready with fixes):**
> - **Groups `$delta` is real** — `graphClient.Groups.Delta.GetAsDeltaGetResponseAsync()` exists in
>   the Beta SDK (`members@delta`, `@removed` tombstones, GA in national clouds). The plan keeps the
>   **full re-scan as the mandatory baseline** and treats delta as a gated optimization — no invented API.
> - **Fix 1 — exclusion subtraction must be in SQL** (was hand-waved as "in-memory drop"). See §6.
> - **Fix 2 — one unified principal-group set** (static closure ∪ dynamic `TransitiveMemberOf`) used
>   for **both** inclusion **and** exclusion; otherwise an exclusion that fires via a *dynamic* group is missed.
> - **Fix 3 — tombstoning a group must also delete its `cache_memberships` edges** (both `group_id`
>   and `member_id` roles), or the CTE keeps walking a deleted group (it joins `cache_memberships`, not `cache_groups`).
> - **Fix 4 — Pass-B per-object `/assignments` fan-out needs a concurrency budget** (it's N Graph calls).

## 1. Objective & Definition of Done

- **DoD #1 — reverse-assignments instant & offline.** `GET /groups/{id}/assignments` returns everything
  assigned to group G (object + intent/exclusion/filter) from `cache_assignments` as one indexed read —
  no live full-tenant scan; works while Graph is unreachable (sidecar up, signed in, after one sync).
- **DoD #2 — effective set for a principal.** `GET /effective-assignments?principalId=…&type=user|device`
  returns what a user/device effectively receives: static-group closure ∪ `allUsers`/`allDevices`,
  **minus exclusions**, with filters annotated, **unioned with `TransitiveMemberOf` for dynamic-group coverage**.
- **DoD #3 — explorer re-backed.** `GET /assignment-explorer` reads from `cache_assignments` (instant,
  uncapped) instead of the live `MaxRows=500` scan (`AssignmentExplorerEndpoints.cs:24,43,62`), with a
  cold-store fallback to the live scan before the first sync.
- **DoD #4 — membership delta reflected.** After a sync, a static-membership add/remove is reflected;
  a deleted group is tombstoned (`deleted=1`, edges cleared) and surfaces as an orphaned assignment.
- **DoD #5 — freshness visible.** `lastRelationshipSyncUtc` advances on `/health` after the pass
  succeeds; never regresses on an out-of-order slow pass.
- **Non-goals (later):** what-if client workspace + orphan/empty screens (M12.4); full principal sync
  + pickers (M12.4/M12.5); at-rest encryption of `relationships.db` (M12.5); richer `/cache/status`.
  The append-only `service/Store/` is **untouched**.

> **Delta uncertainty (gated, not blocking):** no `.Delta` call exists anywhere today; `GroupService`
> does full-list + `OdataNextLink` only (`GroupService.cs:21-51,190-263`), and `GraphDeltaSync.cs:13-20`
> documents config types lacking `$delta`. `groups/delta` is SDK-real but unused here. §3 specifies
> **`$delta`-if-available-else-full**, with the full re-scan as the baseline that satisfies every DoD.
> The delta path is an optimization gated on a one-time SDK-surface spike — do not block M12.2 on it.

## 2. New store — `service/Cache/RelationshipCache.cs`

A new **mutable** SQLite store in a new `service/Cache/Cache.csproj` (`RootNamespace` `CmProjectX.Cache`,
`net10.0`), sibling to `service/Store/` and `service/Sync/`, **explicitly not** in append-only
`service/Store/`. Mirrors `SnapshotStore`'s connection/lock/WAL discipline, but mutable (upsert + tombstone):

- **Connection + WAL:** `new SqliteConnection(...); _db.Open(); Execute("PRAGMA journal_mode=WAL;")` —
  exactly `SnapshotStore.cs:127-129`.
- **Lock:** `private readonly SemaphoreSlim _dbLock = new(1, 1);` (`SnapshotStore.cs:102`); every read
  and write goes through `await _dbLock.WaitAsync(ct)` / `finally _dbLock.Release()`. Its **own**
  `SqliteConnection`, never touching the Lucene index → self-contained commits.
- **Path:** `%LocalAppData%\cmProjectX\relationships.db` (the `SnapshotStore.cs:110-117` pattern, new file).
- **Init:** `InitializeAsync` with the `if (_initialized) return;` guard, `CREATE TABLE/INDEX IF NOT EXISTS`.
- **Upserts:** `INSERT … ON CONFLICT(<pk>) DO UPDATE SET …` (the `SetSyncStateAsync` pattern,
  `SnapshotStore.cs:458-464`) — **never** `INSERT OR REPLACE`.
- **Every row `tenant_id`-scoped**; profile removal = `DELETE WHERE tenant_id=…`.

### Schema

```sql
CREATE TABLE IF NOT EXISTS cache_groups (
    tenant_id TEXT NOT NULL, id TEXT NOT NULL, display_name TEXT,
    group_type TEXT,                                   -- 'assigned' | 'dynamic'
    security_enabled INTEGER, mail_enabled INTEGER, mail TEXT,
    membership_rule TEXT,                              -- dynamic only; the rule lives HERE
    membership_rule_processing_state TEXT, created_utc TEXT,
    user_count INTEGER, device_count INTEGER, nested_count INTEGER,
    etag TEXT, synced_utc TEXT NOT NULL, deleted INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (tenant_id, id));

CREATE TABLE IF NOT EXISTS cache_memberships (        -- DIRECT static edges only
    tenant_id TEXT NOT NULL, group_id TEXT NOT NULL, member_id TEXT NOT NULL,
    member_type TEXT NOT NULL,                         -- 'user' | 'device' | 'group'
    synced_utc TEXT NOT NULL,
    PRIMARY KEY (tenant_id, group_id, member_id));
CREATE INDEX IF NOT EXISTS ix_mem_member ON cache_memberships(tenant_id, member_id);
CREATE INDEX IF NOT EXISTS ix_mem_group  ON cache_memberships(tenant_id, group_id);

CREATE TABLE IF NOT EXISTS cache_objects (            -- assignable left-hand side
    tenant_id TEXT NOT NULL, id TEXT NOT NULL, surface TEXT NOT NULL,  -- Surfaces.cs key
    display_name TEXT, odata_type TEXT, platform TEXT,
    etag TEXT, synced_utc TEXT NOT NULL, deleted INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (tenant_id, id));

CREATE TABLE IF NOT EXISTS cache_assignments (        -- THE EDGE + INTENT table (mirrors AssignmentDto)
    tenant_id TEXT NOT NULL, id TEXT NOT NULL,         -- synthetic: object_id|target_kind|group_id
    object_id TEXT NOT NULL, object_surface TEXT NOT NULL,
    target_kind TEXT NOT NULL,                         -- group|exclusionGroup|allUsers|allDevices
    group_id TEXT, intent TEXT, filter_id TEXT, filter_mode TEXT,
    synced_utc TEXT NOT NULL, PRIMARY KEY (tenant_id, id));
CREATE INDEX IF NOT EXISTS ix_asg_group  ON cache_assignments(tenant_id, group_id);
CREATE INDEX IF NOT EXISTS ix_asg_object ON cache_assignments(tenant_id, object_id);
CREATE INDEX IF NOT EXISTS ix_asg_kind   ON cache_assignments(tenant_id, target_kind);

CREATE TABLE IF NOT EXISTS cache_state (              -- per-tenant delta tokens / last-full stamps
    tenant_id TEXT NOT NULL, key TEXT NOT NULL, value TEXT NOT NULL,
    PRIMARY KEY (tenant_id, key));
```

**Public surface:** `InitializeAsync`; `UpsertGroupsAsync`; **`TombstoneGroupAsync(gid)` — sets
`cache_groups.deleted=1` AND `DELETE FROM cache_memberships WHERE group_id=gid OR member_id=gid`
(critique Fix 3: the CTE joins `cache_memberships`, so a tombstoned group's edges must physically go
or the closure keeps walking it)**; `ReplaceMembershipsForGroupAsync(gid, edges)` (delete-then-insert
the group's edge set under one lock, so a removed static member disappears) / `DeleteMembershipAsync`
(delta `members@removed`); `UpsertObjectsAsync` / `TombstoneObjectAsync`;
`ReplaceAssignmentsForObjectAsync(objectId, rows)`; `GetState`/`SetState`; and the §6 read queries.
All mutators run under `_dbLock`.

**Companion `service/Cache/CachedNameResolver.cs`** (Decision 2): wraps `DirectoryObjectResolver`
(`DirectoryObjectResolver.cs:32-107`). Order: sentinel short-circuit (`:18-25` "All"/"None"/zero-GUID)
→ check a small `resolved_names(tenant_id, id, display_name, resolved_utc)` table (its own mutable
table, also not in `service/Store/`) → batch only misses into `getByIds` (1000/call) → write back.
TTL-bound (mirror the LiteDB 24 h backstop) or invalidate on `/sync`. Keep `IDirectoryObjectResolver`
as the drop-in seam.

**Project wiring:** `Cache.csproj` references `Store.csproj` + `Core.csproj` (mirroring `Sync.csproj:11`);
`Api.csproj` adds `<ProjectReference Include="..\Cache\Cache.csproj" />` (next to `Api.csproj:11-12`).

## 3. `service/Cache/GraphCacheSync.cs` — the refresh engine

A new singleton (`GraphServiceClient` passed per-run like `GraphDeltaSync.RunAsync`; ctor takes
`RelationshipCache` + `CachedNameResolver` + `ILogger`), invoked as a **third pass inside
`GraphDeltaSync.RunAsync`** (§5), wrapped in its own `try/catch … LogWarning("…(continuing)")` (the
`GraphDeltaSync.cs:95-100` convention) so a failure can't abort audit/snapshot work. Watermarks live in
`cache_state` keyed `groups_delta` / `objects_full:{surface}` — **never** colliding with
`audit_watermark:{tenantId}` (`GraphDeltaSync.cs:52`).

**Pass A — groups + static membership (`$delta`-if-available-else-full):**
- **Groups (full baseline):** `FetchDynamicGroupsAsync` (`AssignmentCheckerService.cs:1938-1963`) +
  `FetchAssignedGroupsAsync` (`:1965-1991`), or `GroupService.List{Dynamic,Assigned}GroupsAsync`
  (`GroupService.cs:17-101`) — both already select `membershipRule`/`membershipRuleProcessingState`
  (the dynamic rule column). Upsert into `cache_groups`; `group_type` from `groupTypes` ∋ `DynamicMembership`;
  counts via `GetMemberCountsAsync` (`:103-144`). **Groups not returned this run → `TombstoneGroupAsync`.**
- **Static edges:** `GroupService.ListGroupMembersAsync(gid)` (`:190-263`) → one `cache_memberships`
  row per member by `OdataType`. `ReplaceMembershipsForGroupAsync` (delete-then-insert under lock).
  **Dynamic groups: store the group row, write NO edges** (Decision 1 — `ListGroupMembersAsync` sees
  only static members). The `group_type='dynamic'` flag tells the read path the closure is incomplete.
- **`$delta` optimization (gated):** if the spike confirms `Microsoft.Graph.Beta` v5.130.0-preview
  exposes `Groups.Delta` + `members@delta`, persist `deltaLink` as `cache_state[groups_delta]`,
  first-run-full then incremental, `@removed` → tombstone/`DeleteMembership`. Needs a new `IGroupService`
  delta method (`IGroupService.cs` has none). Follow-up only; baseline ships regardless.

**Pass B — objects + assignment edges (full re-scan, always):**
- Reuse `PrefetchAllToCacheAsync` (`AssignmentCheckerService.cs:1183-1230`) to enumerate assignable
  surfaces (also warms M12.1's blob cache). Upsert into `cache_objects` (`surface` = `Surfaces.cs` key).
- For each object, fetch `/assignments` via the per-surface `GetAssignmentsAsync` (nine Core services,
  each a distinct wrapper carrying `DeviceAndAppManagementAssignmentTarget`), flatten **through
  `Assignments.ReadTarget`** (`Assignments.cs:45-63`) — which keeps `Kind/GroupId/FilterId/FilterMode`
  and (apps-only) `Intent`. **Use `ReadTarget`, not `FlatAssignment`** (`:826-830`, which drops intent+filter).
  `ReplaceAssignmentsForObjectAsync` per object; tombstone objects no longer returned.
- **Fan-out budget (critique Fix 4):** this is one Graph call per object (potentially thousands). Cap
  concurrency with a `SemaphoreSlim` (reuse the prefetch's batch-of-5 discipline, `:1245`), and prefer
  **Graph `$batch` (20 requests/call)** where the wrapper allows it to cut round-trips. `log()` the
  object count + any surface skipped on permission error (best-effort, like Pass A). This pass dominates
  sync wall-clock — keep it off the request path (it already runs in `/sync`'s background `Task.Run`).

**Pass C — principals: none (lazy).** Names resolve on read via `CachedNameResolver`.

**Atomicity/ordering:** stamp `lastRelationshipSyncUtc` only after `SyncAsync` returns without throwing,
`Max(existing, now)` (mirror `AuthSession.MarkWarmed`, `:45`). The store's own WAL + `_dbLock` keep its
commit self-contained despite `RunAsync` lacking cross-pass transactions.

## 4. New endpoints (contract-first)

Order: **`openapi.yaml` → `crates/api-types` + `Contracts.cs` → handlers.** New
`service/Api/Endpoints/RelationshipEndpoints.cs`, mapped via `app.MapRelationships()` near
`app.MapGroups()`. Signed-out `409` guard stays above every cache read.

1. **`GET /groups/{id}/assignments` (reverse, DoD #1).** Reuses the contract `Assignment` schema
   (Rust `Assignment` `lib.rs:372-381`, C# `AssignmentDto` `Assignments.cs:11-17`) wrapped in
   `ReverseAssignmentRow` adding `objectId/objectSurface/objectName`. Names via `CachedNameResolver`.
2. **`GET /effective-assignments?principalId=…&type=user|device` (DoD #2).** New `EffectiveAssignment`
   schema (`objectId, objectSurface, objectName, intent?, filterId?, filterMode?, viaGroupId?, source:
   static|dynamic|allUsers|allDevices`). See §6 for the corrected query.
3. **Re-back `GET /assignment-explorer` (DoD #3).** Read `cache_assignments ⨝ cache_objects`, uncapped;
   keep the response schema unchanged; **fall back to the live `MaxRows=500` scan when the store is cold**.

## 5. Registration in `Program.cs`

1. **DI:** `AddSingleton<RelationshipCache>()`, `AddSingleton<GraphCacheSync>()`, `CachedNameResolver`
   next to `AddSingleton<GraphDeltaSync>()` (`Program.cs:36`).
2. **Init barrier:** `await GetRequiredService<RelationshipCache>().InitializeAsync();` right after the
   `ISnapshotStore … InitializeAsync()` (`Program.cs:69`).
3. **Trigger:** fold `GraphCacheSync.SyncAsync` into `GraphDeltaSync.RunAsync` as the third pass, so it
   shares the existing `/sync` `Task.Run` (`Program.cs:142-160`), one tenant resolution from
   `ActiveProfile` at run start (profile-switch clears the session — M12.1 §9 gotcha 1), one error scope
   (its own `try/catch…(continuing)`).
4. **Freshness:** `AuthSession.LastRelationshipSyncUtc` + `MarkRelationshipSynced` (mirror
   `LastWarmedUtc`/`MarkWarmed`); surface in `/health` `SyncStatusDto`. Contract-first: nullable
   `lastRelationshipSyncUtc` on `SyncStatus` → `Contracts.cs` → `last_relationship_sync_utc:
   Option<String>` with `#[serde(default)]` (the `last_warmed_utc` pattern, `lib.rs:16-18`).
5. **Map:** `app.MapRelationships();`.

## 6. The queries (corrected)

**Reverse — what's assigned to group G (DoD #1):**
```sql
SELECT a.object_id, a.object_surface, o.display_name, a.target_kind,
       a.intent, a.filter_id, a.filter_mode
FROM   cache_assignments a
LEFT JOIN cache_objects o ON o.tenant_id=a.tenant_id AND o.id=a.object_id AND o.deleted=0
WHERE  a.tenant_id=$tid AND a.group_id=$gid;            -- ix_asg_group
```

**Effective — principal P (DoD #2).** Build `$pGroups` in code = **static closure** (CTE below) **∪
dynamic groups from `TransitiveMemberOf`** (`AssignmentCheckerService.cs:1143-1181`; those ids are
*not* in `cache_memberships`). Use the **single unified `$pGroups`** for both inclusion and exclusion
(critique Fix 2). Static closure:
```sql
WITH RECURSIVE groups_of(gid, depth) AS (
    SELECT group_id, 0 FROM cache_memberships
    WHERE tenant_id=$tid AND member_id=$pid                 -- ix_mem_member
  UNION                                                     -- UNION = visited-set dedup (cycle-safe)
    SELECT m.group_id, g.depth+1 FROM cache_memberships m
    JOIN groups_of g ON m.member_id=g.gid
    WHERE m.tenant_id=$tid AND m.member_type='group' AND g.depth < 16   -- depth cap
)
SELECT gid FROM groups_of;   -- ∪ (in code) the TransitiveMemberOf dynamic-group ids  ⇒  $pGroups
```
Then the effective set, **with exclusion expressed in SQL** (critique Fix 1):
```sql
SELECT a.object_id, a.object_surface, a.intent, a.filter_id, a.filter_mode, a.group_id
FROM   cache_assignments a
WHERE  a.tenant_id=$tid
  AND ( (a.target_kind='group' AND a.group_id IN ($pGroups))
        OR a.target_kind = CASE WHEN $type='user' THEN 'allUsers' ELSE 'allDevices' END )
  AND NOT EXISTS (                                          -- drop objects excluded via ANY of P's groups
        SELECT 1 FROM cache_assignments x
        WHERE x.tenant_id=a.tenant_id AND x.object_id=a.object_id
          AND x.target_kind='exclusionGroup' AND x.group_id IN ($pGroups) );
```
Tag each row `source` (static via CTE / dynamic via TransitiveMemberOf / allUsers / allDevices) so the
dynamic contribution is explicit, not silently merged.

**Orphaned assignments / empty groups / dynamic-rule audit:**
```sql
-- orphans: assignment whose group is gone (tombstoned or never present)
SELECT a.* FROM cache_assignments a
WHERE a.tenant_id=$tid AND a.group_id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM cache_groups g
                  WHERE g.tenant_id=a.tenant_id AND g.id=a.group_id AND g.deleted=0);
-- empty groups: no members AND no assignments
SELECT id, display_name FROM cache_groups g WHERE g.tenant_id=$tid AND g.deleted=0
  AND NOT EXISTS (SELECT 1 FROM cache_memberships m WHERE m.tenant_id=g.tenant_id AND m.group_id=g.id)
  AND NOT EXISTS (SELECT 1 FROM cache_assignments a WHERE a.tenant_id=g.tenant_id AND a.group_id=g.id);
-- dynamic-rule audit: one indexed scan
SELECT id, display_name, membership_rule FROM cache_groups
WHERE tenant_id=$tid AND group_type='dynamic' AND deleted=0;
```

## 7. Test plan

- **Schema/init:** `InitializeAsync` idempotent; five tables + indexes; file at
  `%LocalAppData%\cmProjectX\relationships.db`, **not** under `service/Store/`.
- **Upsert + tombstone (DoD #4, deterministic — no live Graph):** upsert group+members → reverse query
  returns them; re-run `ReplaceMembershipsForGroupAsync` with a member removed → it's gone;
  **`TombstoneGroupAsync` → its `cache_memberships` rows are physically deleted and the CTE no longer
  walks it** (Fix 3); the orphaned-assignment query surfaces its still-present assignment.
- **CTE closure (DoD #2):** `A∈G1`, `G1∈G2`, assign→`G2` ⇒ effective(A) includes it; a **cyclic** edge
  set terminates (visited-set `UNION` + depth cap).
- **Exclusion (Fix 1/2):** assign→`allUsers` with an `exclusionGroup`→`Gx`; `A∈Gx` (static) ⇒ object
  excluded; `A` in a **dynamic** `Gx` (from TransitiveMemberOf, not in `cache_memberships`) ⇒ **still
  excluded** (proves the unified `$pGroups` covers dynamic exclusions).
- **Dynamic gap explicit:** dynamic group + rule + zero `cache_memberships` ⇒ static CTE returns
  nothing; the TransitiveMemberOf union supplies it, tagged `source='dynamic'`.
- **Reverse vs live agreement (DoD #1):** cache reverse == live `AssignmentCheckerService` scan (set
  equality on object+intent+filter). Live-tenant `SidecarFixture` (won't run offline — M12.1 caveat).
- **Explorer parity (DoD #3) + cold-store fallback.**
- **Freshness (DoD #5):** `/health.lastRelationshipSyncUtc` advances after `/sync`; `Max` guard prevents
  regression.
- **Name resolution:** sentinel without a Graph call; second resolve hits the table (no second
  `getByIds`); unresolved id → raw GUID.

## 8. Risks & invariants

- **Append-only `service/Store/` untouched** — all new code in `service/Cache/` (mutable) + Api/contract/
  api-types; the store has its **own** `SqliteConnection` + `_dbLock`, never the Lucene writer.
- **Contract-first**; reuse the `Assignment` schema where the shape matches `/apps/{id}/assignments` to
  prevent drift.
- **Edition-2024 / client:** only the one `lastRelationshipSyncUtc` render line in `main.rs` for the
  M12.2 server DoD; reverse/what-if screens are M12.4. No Reactor change.
- **Single-instance / ARM64:** 5099 mutex relied-on, not changed; only new dep is `Microsoft.Data.Sqlite`
  (already used) → **ARM64 clean**. `relationships.db` at-rest encryption deferred to M12.5 (flag UPN/
  device-name PII now).
- **No concurrent `dotnet build`**; namespaced `cache_state` watermarks; every row `tenant_id`-scoped;
  tenant read at run start from `ActiveProfile`; `ON CONFLICT DO UPDATE` not `INSERT OR REPLACE`; all
  writes + CTE reads under `_dbLock`.
- **Correctness ceilings (state in UI later):** static spine can't see dynamic membership (covered by the
  TransitiveMemberOf union); a renamed principal shows a stale name until the resolver TTL / `/sync`.

## 9. Sequencing (subagent parallelization)

- **Serializable spine (in order):** (1) `Cache.csproj` + refs; (2) `openapi.yaml`
  (`lastRelationshipSyncUtc` + `ReverseAssignmentRow` + `EffectiveAssignment`); (3) DTO mirrors
  (`lib.rs` `#[serde(default)]`, `Contracts.cs`); (4) `RelationshipCache.cs` (schema + mutators +
  query methods) — the keystone.
- **Parallel batch (one agent each, after the spine):**
  - **A** — `GraphCacheSync.cs` Pass A+B (full-rescan baseline; delta stubbed behind the spike flag; Fan-out budget).
  - **B** — `CachedNameResolver.cs` + `resolved_names` table.
  - **C** — `RelationshipEndpoints.cs` (`/groups/{id}/assignments` + `/effective-assignments`).
  - **D** — re-back `AssignmentExplorerEndpoints.cs` (+ cold-store fallback).
  - **E** — `AuthSession.LastRelationshipSyncUtc`/`MarkRelationshipSynced` + `/health` field + `main.rs` line.
- **Orchestrator (central, after batch):** `Program.cs` DI + Init barrier + fold the third pass into
  `RunAsync` + `app.MapRelationships()`. Then §7 tests.
- **One-time spike (off critical path):** verify `Microsoft.Graph.Beta` v5.130.0-preview `groups/delta`
  + `members@delta`; gate the delta optimization on it. Baseline ships regardless.
- **Build gate:** one `dotnet build service/CmProjectX.slnx --configuration Release` then `cargo build
  --workspace` — sequential.
