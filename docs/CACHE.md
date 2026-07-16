# cmProjectX — Caching & the relationship graph

**Goal.** Make the whole Intune/Entra surface **instant and offline-capable**, and turn the
assignment/membership data into a **queryable relationship graph** — groups, group members,
dynamic membership rules, assignable objects, and the **assignment edges (with intent / exclusion /
filter)** between them. The headline payoff is the *reverse* and *effective* questions that today
require a live full-tenant scan: *"everything assigned to this group, with intent"*, *"what does
this device/user effectively receive"*, *"orphaned assignments to deleted groups"*, *"groups with
no assignments / no members"*.

This expands the **M12** "Cache/offline" line in [ROADMAP.md](./ROADMAP.md) from a single
read-through accelerator into a **two-layer cache**: a raw-object blob cache *and* a normalized
relationship store.

---

## Why cmProjectX is already most of the way there

Three things already shipped make this far less than a from-scratch build:

1. **The fetch engine exists.** `AssignmentCheckerService.PrefetchAllToCacheAsync()`
   (`service/Core/Services/AssignmentCheckerService.cs`) already enumerates **31 entity types**
   tenant-wide — dynamic + assigned groups (carrying `membershipRule` /
   `membershipRuleProcessingState`), all assignable policy/app types, filters — batching in groups
   of 5 to dodge throttling. `GroupService` already does member enumeration + counts +
   transitive-member resolution; `AssignmentCheckerService` already flattens each object's targets
   to a `FlatAssignment(GroupId, IsExclusion, IsAllUsers, IsAllDevices)`. **We don't fetch anything
   new — we persist what these already compute and throw away.**

2. **A blob cache is already built — just unwired.** `CacheService`
   (`service/Core/Services/CacheService.cs`) is a production-ready LiteDB cache: generic
   `Get<T>/Set<T>`, 24h TTL, AES+DPAPI encryption, 8 MB chunking, tenant-scoped keys
   (`{tenantId}|{dataType}`), polymorphic `@odata.type` round-trip, `NullCacheService` fallback if
   the DB is locked. It is **not wired into a single endpoint** today — only touched internally when
   `PrefetchAllToCacheAsync()` is explicitly called. Wiring it is layer one.

3. **The background-sync pattern exists.** `POST /sync` already kicks `GraphDeltaSync.RunAsync`
   off as a fire-and-forget `Task.Run`, advances `AuthSession.LastSyncUtc`, and the client polls
   `/health` for the new watermark. `GraphDeltaSync` already establishes the delta playbook —
   per-tenant watermarks in `sync_state`, content-hash dedup, "most Intune config types don't
   support `$delta`". The cache refresh job is a **sibling of that engine**, triggered the same way.

The sidecar (127.0.0.1:5099) is the right home — it already owns auth, the Graph client, the Core
services, and the existing stores.

---

## Decisions locked (2026-06-11)

| Fork | Choice | Consequence |
|---|---|---|
| **Storage shape** | **Hybrid.** Keep the LiteDB blob cache for raw list/detail acceleration (wire it read-through), **and** add a normalized SQLite **relationship store** for the graph. | Two cooperating layers: blobs answer "show me the list/object"; the relational layer answers "show me the relationships". Reverse/effective queries become local SQL, not live scans. |
| **Freshness** | **On-demand + delta where supported.** Groups & membership via Graph **`$delta`** (per-tenant `deltaLink` tokens, tombstones). Assignable objects + their assignment edges via **periodic full re-scan** (no `$delta` for most Intune types). | Repeat syncs are cheap exactly where churn is highest (membership). Triggered like `/sync` today; background/scheduled refresh is an opt-in polish step, not the default. |
| **Where it lives** | A **new mutable store**, *not* in `service/Store/`. | `service/Store/` is **append-only by design** (a non-negotiable project rule). The relationship cache upserts and deletes (tombstones), so it gets its own home — proposed `service/Cache/`. |
| **Contract first** | New DTOs + paths land in `contract/openapi.yaml` before any Rust/C# DTO. | Same rule as every other surface; the relationship endpoints are not an exception. |

---

## Architecture

```
                          POST /sync  (or /cache/refresh)  — fire-and-forget, like today
                                 │
   ┌───────────────────────────── cmProjectX sidecar (5099) ─────────────────────────────┐
   │                                                                                      │
   │  GraphCacheSync  (new — sibling of GraphDeltaSync)                                    │
   │    • groups + membership ── Graph $delta ──► upsert  ┐                                │
   │    • dynamic rules (on the group row) ───────────────┤                                │
   │    • objects + their /assignments ── full re-scan ───┤ reuses PrefetchAllToCache +    │
   │      flatten targets (intent/exclusion/filter) ──────┘ GroupService + AssignmentCkr   │
   │                         │                         │                                  │
   │                         ▼                         ▼                                  │
   │  ┌─ Layer 1: blob cache ─┐   ┌──── Layer 2: relationship store (NEW, SQLite) ──────┐ │
   │  │ LiteDB CacheService   │   │ cache_groups        (id, rule, counts, tombstone)   │ │
   │  │ raw object lists/      │   │ cache_memberships   (group→member, direct|transitive)│ │
   │  │ details, 24h TTL,      │   │ cache_objects       (assignable left-hand side)     │ │
   │  │ encrypted, per-type    │   │ cache_assignments   (EDGES: target+intent+filter)   │ │
   │  │ (already built)        │   │ cache_state         (per-tenant delta tokens)       │ │
   │  └───────────┬───────────┘   └───────────────────────┬─────────────────────────────┘ │
   │              │ read-through                          │ direct SQL                     │
   │              ▼                                        ▼                                │
   │   list/detail endpoints                  /groups/{id}/assignments  (reverse)          │
   │   (/device-configs, /apps, …)            /effective-assignments    (what-if)          │
   │                                          /assignment-explorer       (re-backed)        │
   └──────────────────────────────────────────────────────────────────────────────────────┘
                                 ▲ GET /health (+ /cache/status): per-entity counts, last-synced
```

### 1. Layer one — blob read-through (the existing LiteDB cache, finally wired)
Put `CacheService` in front of the list/detail endpoints: on `GET /{surface}`, return the cached
list if warm, else fetch from Graph and populate. Warm the whole set via
`PrefetchAllToCacheAsync()` on sign-in and on refresh. This alone delivers **offline browse of
last-known state** and removes the per-request Graph round-trip for every list. It is mostly
*wiring* — the cache, the prefetch, the TTL, the encryption all exist. Register `ICacheService` in
DI (today only Core constructs it internally) and pass `(cacheService, tenantId)` into the Core
services that accept them (`AssignmentCheckerService` already has the optional ctor params; the
`/assignment-explorer` endpoint comment explicitly notes it omits them today).

### 2. Layer two — the relationship store (new)
A normalized SQLite store, sibling to the append-only time-machine, holding the **graph** the blob
cache can't express. Mutable: rows are upserted on delta, tombstoned on `@removed`. Mirrors
`SnapshotStore`'s connection/`SemaphoreSlim`-lock/WAL pattern, but is explicitly **not** in
`service/Store/` (which must stay append-only). Proposed home: `service/Cache/RelationshipCache.cs`
+ `service/Cache/GraphCacheSync.cs`, alongside `service/Store/` and `service/Sync/`. Lives at
`%LocalAppData%\cmProjectX\relationships.db`.

**Schema (every row tenant-scoped — profiles are multi-tenant, mirroring the LiteDB key):**

| Table | Columns (sketch) | Purpose |
|---|---|---|
| `cache_groups` | `tenant_id, id, display_name, group_type, security_enabled, mail_enabled, mail, membership_rule, membership_rule_processing_state, created_utc, user_count, device_count, nested_count, etag, synced_utc, deleted` | One row per group. **The dynamic rule is a column here.** |
| `cache_memberships` | `tenant_id, group_id, member_id, member_type(user\|device\|group), transitive(bool)` · PK `(group_id, member_id, transitive)` | Group→member edges. `transitive=false` = direct; closure materialized or computed on read (see Open decisions). Reverse index on `member_id` answers "which groups is this principal in". |
| `cache_objects` | `tenant_id, id, surface, display_name, odata_type, platform, etag, synced_utc, deleted` | The assignable left-hand side. `surface` mirrors `Surfaces.cs` keys (`device-configs`, `apps`, …). |
| `cache_assignments` | `tenant_id, id, object_id, object_surface, target_kind(group\|exclusionGroup\|allUsers\|allDevices), group_id?, intent?, filter_id?, filter_mode?, synced_utc` | **The relationship + intent table.** One row per assignment target. Shape mirrors the endpoint-side `AssignmentDto` (Kind/GroupId/Intent/FilterId/FilterMode) — richer than `FlatAssignment`, which drops intent + filter. |
| `cache_state` | `tenant_id, key, value` | Per-tenant delta tokens / last-full timestamps (e.g. `groups_delta`, `objects_full:{surface}`). Same idea as `sync_state`, kept in the mutable store to separate concerns. |

Indexes that make the value queries cheap: `cache_assignments(group_id)`, `cache_assignments(object_id)`,
`cache_memberships(member_id)`, plus `(tenant_id)` everywhere.

### 3. The queries this unlocks (the whole point)
- **What's assigned to group G (with intent/exclusion/filter):** `cache_assignments WHERE group_id=G`
  → join `cache_objects`. This is the "reverse-assignment scan" that `GroupsEndpoints` today calls
  out as *"a separate, slower scan and intentionally not bundled here"* — now a single indexed read.
- **What principal P (user/device) effectively gets:** P's groups (direct + transitive from
  `cache_memberships WHERE member_id=P`, via a `WITH RECURSIVE` closure over group-in-group edges) ∪
  `allUsers`/`allDevices` → `cache_assignments` targeting those groups, **minus** exclusion targets,
  with filters annotated. The effective-assignment / *what-if* engine — SQL plus a small in-memory
  exclusion/filter pass.
- **Empty groups / orphaned assignments:** groups with no `cache_assignments` and/or no
  `cache_memberships`; assignment rows whose `group_id` is absent from `cache_groups` (deleted group).
- **Dynamic-rule audit:** query `membership_rule` across all groups in one scan.

### 4. The refresh engine — `GraphCacheSync` (sibling of `GraphDeltaSync`)
A new singleton (`GraphServiceClient` + tenant + `RelationshipCache` + logger), triggered exactly
like the existing sync — extend `POST /sync` to also run it, or add `POST /cache/refresh`; both
fire-and-forget via `Task.Run`, client polls `/health`/`/cache/status`. Three passes:

- **Groups + membership — `$delta`.** `GET /groups/delta` returns adds/updates/deletes and
  `members@delta`; persist the `deltaLink` token in `cache_state` per tenant (exactly the audit
  watermark pattern). First run = full; later runs = incremental. `@removed` tombstones →
  `deleted=1` / membership-row deletes. Dynamic rules ride along on the group object. This is the
  big delta win — membership churn is precisely what `$delta` is for.
- **Assignable objects + their assignments — full re-scan.** Reuse `PrefetchAllToCacheAsync` to list
  objects (this also warms layer one), then fetch each object's `/assignments`, flatten targets
  through the existing `Assignments.ReadTarget` (which already yields Kind/GroupId/Intent/Filter),
  and upsert `cache_objects` + `cache_assignments`. Etag/content-hash to skip unchanged objects.
  Heavier than delta, but assignments change far less often than membership.
- **Principals (names) — lazy.** Resolve user/device display names on read via the existing
  `DirectoryObjectResolver` (batch `getByIds`, 1000/call) with a small resolved-name cache; a full
  up-front principal sync is optional and deferred (a tenant's full user/device list can be huge).

---

## New surface area (small, mostly wiring)

- **Service:**
  - `service/Cache/RelationshipCache.cs` (new SQLite store) + `service/Cache/GraphCacheSync.cs`
    (refresh engine). Both registered as singletons in `Program.cs`; `RelationshipCache.InitializeAsync()`
    next to the existing `ISnapshotStore.InitializeAsync()`.
  - DI-register `ICacheService` (today Core-internal only) so endpoints can read-through.
  - Extend `POST /sync` (or add `POST /cache/refresh`) to run `GraphCacheSync`; surface counts +
    last-synced in `/health` (or a new `GET /cache/status`).
  - New read endpoints: `GET /groups/{id}/assignments` (reverse), `GET /effective-assignments?principalId=…&type=user|device`
    (what-if), and re-back `GET /assignment-explorer` from `cache_assignments` (instant + uncapped
    vs. today's live `MaxRows=500` scan).
- **Client (`app/src/`):** extend `screen_groups.rs` detail with a reverse-assignments section
  (now a cache hit); a new "Effective assignments / what-if" workspace; a cache-status / last-synced
  indicator (poll like `/health`). One or two `features.rs` rows; `api_client.rs` methods.
- **Contract:** add the new paths + `AssignmentEdge` / `EffectiveAssignment` / `CacheStatus` schemas
  to `contract/openapi.yaml` **first**, then mirror into `crates/api-types` and `Contracts.cs`.
- **Tests:** delta round-trip (full → incremental → tombstone removes rows); reverse-assignment query
  vs. a live `AssignmentCheckerService` scan agree; effective-assignment excludes exclusion targets;
  cache survives sign-out (offline browse).

---

## Phased plan (M12.x)

- **M12.1 — Blob read-through.** Wire `CacheService` in front of list/detail endpoints; warm via
  `PrefetchAllToCacheAsync` on sign-in/refresh. **DoD:** lists render from cache offline; second
  load does no Graph round-trip. *Ships value alone (offline browse) with almost no new code.*
- **M12.2 — Relationship store: groups + membership + rules.** `RelationshipCache` schema;
  `GraphCacheSync` groups/membership via `$delta`; `GET /groups/{id}/assignments` reverse +
  group detail served from cache. **DoD:** group detail + reverse-assignments instant and offline;
  a membership change reflected after a delta refresh.
- **M12.3 — Assignment edges + intents.** Cache `cache_objects` + `cache_assignments` from the
  full re-scan; re-back `/assignment-explorer` from the store (uncapped, instant); orphan/empty
  queries. **DoD:** explorer loads from cache with no live scan; orphaned/empty reports return.
- **M12.4 — Effective assignments / what-if.** `GET /effective-assignments` (membership closure −
  exclusions + filters) + the client what-if workspace. **DoD:** pick a user/device → see its
  effective policy/app set, exclusions honored.
- **M12.5 — Freshness polish.** Delta-token hardening, tombstone correctness, opt-in
  background/scheduled refresh, `/cache/status` surface, at-rest encryption for `relationships.db`.

---

## Open decisions (surface before the relevant phase)

1. **Transitive membership — materialize vs. compute on read.** Store only direct + group-in-group
   edges and expand with a `WITH RECURSIVE` CTE on read (lighter store, recursive cost per query),
   vs. materialize the full closure on sync (fast reads, heavier/larger sync). *Lean: direct edges +
   on-read closure, cache hot principals.* (M12.2)
2. **Principal sync — lazy name resolution vs. full user/device cache.** Full tenant user+device
   lists can be very large. *Lean: lazy resolve via `DirectoryObjectResolver`; full principal sync
   optional later (would also enable principal search/pickers).* (M12.2/M12.4)
3. **Trigger surface — fold into `/sync` + `/health` vs. dedicated `/cache/*`.** *Lean: reuse
   `/sync` + `/health` to avoid surface sprawl; add `GET /cache/status` only if `/health` gets
   crowded.* (M12.1)
4. **At-rest encryption for `relationships.db`.** It holds membership (UPNs, device names) — mildly
   sensitive. The LiteDB blob cache is already DPAPI-encrypted. *Lean: match that posture (SQLCipher
   or a DPAPI-wrapped key) before this leaves prototype.* (M12.5)
5. **Multi-tenant eviction.** One DB keyed by `tenant_id` (mirrors LiteDB keys) vs. a DB-per-profile.
   *Lean: one DB, `tenant_id` column; `DELETE WHERE tenant_id=…` on profile removal.* (M12.2)
6. **Blob vs. relational overlap.** Group detail can come from either the LiteDB blob *or* the
   relational store. *Lean: relational is source of truth for the graph (groups/members/edges); the
   blob cache serves the non-relational surfaces' raw list/detail. Avoid caching the same fact in
   both as a writable source.* (M12.2)
