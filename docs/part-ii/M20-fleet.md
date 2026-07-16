# M20 — Fleet: MSP-scale multi-tenant

> Every endpoint already keys cache + store by `{tenantId}` — M20 lifts that latent key into first-class fan-out: tenant groups, a fleet view spanning N tenants, golden-tenant templates with per-tenant overrides, and cross-tenant campaigns where one broadcast = N gated M13 replays.

---

## What it builds on

The single-tenant primitives M20 fans out over already exist; the lift is wiring, not engine.

- **Tenant-keyed cache + store are already there.** `CachedReader.ListAsync` / `GetAsync`
  (`service/Api/Endpoints/CachedReader.cs:22,40`) read `auth.ActiveProfile?.TenantId` and key
  every LIST/DETAIL through `ICacheService` as `{tenantId}|{dataType}`. `SnapshotStore` keys its
  append-only snapshots + audit rows by tenant the same way. The data model is *already*
  tenant-partitioned; M20 just iterates the partition set instead of reading the one bound to
  `ActiveProfile`.
- **Tenant profiles are a list, not a singleton.** `ProfileService.Profiles`
  (`service/Core/Services/ProfileService.cs:51`) returns `IReadOnlyList<TenantProfile>`;
  `AuthSession.Profiles` (`AuthSession.cs:67`) re-exposes it. Today exactly one is *active*
  (`GetActiveProfile()`), and `Activate()` (`AuthSession.cs:73`) force-`SignOut()`s on switch
  precisely because a token issued for tenant A is invalid for B and the per-tenant cache must
  never be cross-written. Fleet flips the assumption: hold N live sessions concurrently rather
  than one-at-a-time.
- **Export/Import is the broadcast engine.** `IExportService` / `IImportService`
  (`service/Core/Services/IExportService.cs`, `IImportService.cs`) already serialize every
  assignable surface (device config, compliance, settings-catalog, apps, CA, …) to portable JSON
  and re-`Import…Async` it into a *different* tenant, remapping cross-tenant references through a
  **`MigrationTable`** (`service/Core/Models/MigrationTable.cs`, `MigrationEntry.cs`:
  `objectType`/`originalId`/`newId`/`name`). That `(Export golden → Import per target + remap)`
  loop *is* a campaign step. M20 reuses it verbatim; it does not add a second serializer.
- **M13 replay is the gated write.** Per `docs/PLUGINS-MCP.md`, a governed write is a *deferred,
  gated replay* of the same `(verb, path, body)` the WinUI client would have sent, through M6's
  `/preview-diff` → snapshot-on-write → audit rails. A fleet write to N tenants is **N of those
  same gated replays**, one per target, each producing its own diff/snapshot/audit row in that
  tenant's store.

---

## What's net-new

This is the **biggest net-new lift in Part II** — be honest about it. Everything above is reuse;
the fan-out plane underneath is new code.

- **Multi-tenant session.** Today `AuthSession` holds one `Graph`, one `Credential`, one
  `_state`, gated by a single `_generation` (`AuthSession.cs:30,39,46`). Fleet needs a
  **`FleetSession`** holding a `Dictionary<tenantId, AuthSession>` (or an N-slot session map) so
  N `GraphServiceClient`s are live at once, each with its own generation/cancellation. The
  single-active-profile invariant in `Activate()` (`AuthSession.cs:83` → `SignOut()` on switch)
  is *relaxed for fleet reads* but kept for the interactive "current tenant" UX.
- **Tenant groups.** A named, persisted set of `tenantId`s (a saved fan-out target). New
  persisted model alongside `profiles.json`; no Graph involvement.
- **Fleet view.** A LIST surface (`/fleet/list/{surface}`) that runs the *same* `(verb, path)`
  read across a group and merges results **tenant-tagged** into one `ListItem`-shaped stream.
- **Golden templates + inheritance.** A desired-state document (one tenant or a Git repo, per
  M15) plus a per-tenant override layer and a 3-way resolver. New.
- **GDAP.** Granular Delegated Admin Privileges — MSP delegated access into customer tenants via
  partner relationships, so one operator credential reaches many tenants without a stored secret
  per tenant. Net-new auth path (deferred — see below).

---

## Pattern G — keyed fan-out

> The same `(verb, path, body)` replayed across a tenant **set**; reads merged tenant-tagged, each
> write a gated M13 replay. A fleet write to N tenants is N gated replays — never one bulk call.

```
                              ┌─ tenant A session ─▶ Graph A ─▶ store A (cache+snapshot+audit)
 (verb, path, body)  ──fan──▶ ├─ tenant B session ─▶ Graph B ─▶ store B
   over group {A,B,C}         └─ tenant C session ─▶ Graph C ─▶ store C
                                          │
                              merge, tag each row/result with tenantId
                                          ▼
                              one tenant-tagged response
```

- **Reads** (`GET /fleet/list/{surface}`, `/fleet/drift`, `/fleet/posture`): for each `tenantId`
  in the group, run the existing single-tenant handler against that tenant's session, then
  concatenate results, stamping every row with its `tenantId` + `tenantName`. Each per-tenant read
  still flows through `CachedReader` against `{tenantId}|{surface}` — so fleet reads are warm if
  the per-tenant cache is warm, and partial failures degrade per tenant (tenant B throttled ≠
  whole fleet fails).
- **Writes** (`POST /fleet/campaign`): the broadcast `(verb, path, body)` is **not** sent once.
  It is enqueued as one **gated M13 replay per target tenant** (`POST /pending-changes`, bound to
  that target's `tenantId`). Each replay independently resolves the effective golden, produces a
  field-level diff, and — *when approved in that tenant's inbox* — resolves cross-tenant refs via
  that tenant's `MigrationTable`, snapshots-on-write, and writes an audit row in *that tenant's*
  store. Because apply is deferred to the gate, the campaign's **immediate** fan-out reports
  `pending` for each gated target (plus `skip` for the golden source / already-in-sync / missing
  object, `error` for a per-tenant throttle or unreachable session, and `success` only under
  `dryRun` where the diff is computed but nothing is enqueued). The **resolved** outcomes
  (`success` / `skip` / `conflict`) are what each target settles to *after* the operator approves
  its gate: `success` once the replay applies (snapshot + audit land then), and `conflict` when the
  target's live object changed since the golden read so the gated replay no longer applies cleanly
  (an etag/state mismatch — surfaced at **approve time**, in the pending inbox, not at campaign
  fan-out time). The campaign aggregates N outcomes — it never collapses them into one.
- **Concurrency + isolation.** Fan-out runs with a bounded degree-of-parallelism and per-tenant
  generation gating (same pattern as `AuthSession._generation`, `AuthSession.cs:30`), so a tenant
  switch / sign-out of one session can't clobber another's in-flight result. One tenant's 429
  (Graph throttle) is caught and reported as that tenant's outcome, not a fleet abort.

---

## Golden templates + inheritance

A **golden** is the desired state for a surface, defined once and inherited by every target. Two
sources, same shape:

1. **Golden tenant** — pick one live tenant as canonical; its surface (read via the existing
   handler) is the baseline. Internally an `Export…Async` of that surface.
2. **Golden repo** — a Git-backed desired-state tree (reuse the M15 GitOps repo model), so the
   baseline is version-controlled and reviewable, not a live tenant.

**Inheritance is a layered merge:** `effective = golden ⊕ groupOverride ⊕ tenantOverride`. The
override layers are sparse JSON-merge-patch documents keyed by `{surface, objectName}` — e.g.
golden says *"Compliance baseline: passcode 6 digits"*, but tenant `contoso` overrides
`minimumLength: 8`. Resolution is field-level (it reuses the same `JsonDrift.Diff` machinery M6
uses for `/preview-diff`), so an override touches only the fields it names; everything else
inherits. Cross-tenant references (groups, scope tags, filters) never inherit raw — they route
through each target's `MigrationTable` so `originalId`→`newId` remap is per-tenant
(`MigrationEntry.OriginalId`/`NewId`).

Drift, then, is just *effective-golden vs. live-tenant* run through the same field diff — a tenant
"diverged" when its live surface differs from its resolved effective state.

---

## Contract additions

All under `/fleet/*`, all camelCase, all mirrored in `crates/api-types/src/lib.rs` +
`service/Api/Contracts.cs`, added to `contract/openapi.yaml` first.

| Method | Path | Purpose |
|---|---|---|
| `GET`  | `/fleet/tenants` | All known tenant profiles + live session state (which are signed in). |
| `GET`  | `/fleet/groups` | List saved tenant groups. |
| `POST` | `/fleet/groups` | Create/update a tenant group (named set of `tenantId`s). |
| `DELETE` | `/fleet/groups/{id}` | Delete a group. |
| `GET`  | `/fleet/list/{surface}?group={id}` | Fan-out LIST across a group; rows tenant-tagged. |
| `POST` | `/fleet/campaign` | Broadcast a `(verb, path, body)` baseline to a tenant set; `dryRun` gates. |
| `GET`  | `/fleet/campaign/{id}` | Poll a campaign's per-tenant outcomes. |
| `GET`  | `/fleet/drift?group={id}&golden={ref}` | Per-tenant divergence from the resolved golden. |
| `GET`  | `/fleet/posture?group={id}` | Fleet-wide posture: per-tenant score + fleet roll-up. |
| `GET`  | `/fleet/templates` / `POST` `/fleet/templates` | Manage golden templates + override layers. |

`{surface}` is the same surface key the single-tenant `features.rs` registry already uses
(`managed-devices`, `compliance-policies`, `settings-catalog`, …) — fleet adds no new surface
vocabulary.

---

## Sample data

### Tenant-group definition (`POST /fleet/groups`)

```json
{
  "id": "grp-northwest-msp",
  "name": "Northwest MSP — production",
  "tenantIds": [
    "9f1c3a52-7b44-4e2d-8f10-2c6b9d5a1e80",
    "3d8e7f04-1a92-4c6b-9e55-77b0c2f4a3d1",
    "a14b6c98-2f0d-4781-bb3e-5e9c0a7d2f44"
  ],
  "goldenTenantId": "9f1c3a52-7b44-4e2d-8f10-2c6b9d5a1e80",
  "createdUtc": "2026-06-24T14:02:11Z"
}
```

### Fleet-list response (`GET /fleet/list/compliance-policies?group=grp-northwest-msp`)

One surface listed across 3 tenants; every row carries `tenantId` + `tenantName`. Tenant C
degraded (throttled) — reported per tenant, fleet still returns.

```json
{
  "surface": "compliance-policies",
  "group": "grp-northwest-msp",
  "tenants": [
    { "tenantId": "9f1c3a52-7b44-4e2d-8f10-2c6b9d5a1e80", "tenantName": "Fabrikam HQ",  "status": "ok" },
    { "tenantId": "3d8e7f04-1a92-4c6b-9e55-77b0c2f4a3d1", "tenantName": "Contoso Retail","status": "ok" },
    { "tenantId": "a14b6c98-2f0d-4781-bb3e-5e9c0a7d2f44", "tenantName": "Tailwind Labs", "status": "throttled" }
  ],
  "items": [
    { "id": "b2c1...001", "title": "Win10 Baseline", "subtitle": "2 assignments",
      "platform": "Windows", "modified": "2026-05-30T09:11:00Z",
      "tenantId": "9f1c3a52-7b44-4e2d-8f10-2c6b9d5a1e80", "tenantName": "Fabrikam HQ" },
    { "id": "b2c1...044", "title": "iOS Compliance", "subtitle": "1 assignment",
      "platform": "iOS", "modified": "2026-06-02T16:40:00Z",
      "tenantId": "9f1c3a52-7b44-4e2d-8f10-2c6b9d5a1e80", "tenantName": "Fabrikam HQ" },
    { "id": "c7d2...210", "title": "Win10 Baseline", "subtitle": "3 assignments",
      "platform": "Windows", "modified": "2026-04-18T11:05:00Z",
      "tenantId": "3d8e7f04-1a92-4c6b-9e55-77b0c2f4a3d1", "tenantName": "Contoso Retail" }
  ],
  "errors": [
    { "tenantId": "a14b6c98-2f0d-4781-bb3e-5e9c0a7d2f44", "code": "throttled",
      "message": "Graph 429 — retry after 38s; tenant rows omitted" }
  ]
}
```

### Campaign request (`POST /fleet/campaign`) — broadcast a baseline, dry-run + gated

```json
{
  "campaignId": "camp-2026-0624-compliance-rollout",
  "group": "grp-northwest-msp",
  "golden": { "kind": "goldenTenant", "tenantId": "9f1c3a52-7b44-4e2d-8f10-2c6b9d5a1e80" },
  "surface": "compliance-policies",
  "objectName": "Win10 Baseline",
  "verb": "PATCH",
  "dryRun": true,
  "gate": "perTenantConfirm",
  "overrides": {
    "3d8e7f04-1a92-4c6b-9e55-77b0c2f4a3d1": { "passwordMinimumLength": 8 }
  }
}
```

`dryRun: true` resolves `effective = golden ⊕ override` per target and returns a `/preview-diff`
for each, applying nothing. `gate: "perTenantConfirm"` means each target's diff is a separate
approval in the app's pending-changes inbox — N gates, not one.

### Campaign result (`GET /fleet/campaign/{id}`) — per-tenant outcomes

Each target's row carries `pendingId` (the gated M13 replay awaiting approval), `newId` (the
matched live object), `appliedFields`, and the field-level `diff` — the same `DriftChange` shape
`/preview-diff` emits. `outcome` is one of `skip` / `pending` / `error` at fan-out time, settling to
`success` / `conflict` as each gate is approved (see the Writes note in Pattern G). There are **no**
`snapshotId` / `auditId` / `conflictFields` fields — the durable snapshot + audit rows land in the
target tenant's append-only store when its gate applies, and the campaign links to them only through
`pendingId`.

```json
{
  "campaignId": "camp-2026-0624-compliance-rollout",
  "surface": "compliance-policies",
  "objectName": "Win10 Baseline",
  "dryRun": false,
  "results": [
    { "tenantId": "9f1c3a52-7b44-4e2d-8f10-2c6b9d5a1e80", "tenantName": "Fabrikam HQ",
      "outcome": "skip", "reason": "goldenSource — no write to canonical tenant",
      "pendingId": null, "newId": null, "appliedFields": [], "diff": [] },
    { "tenantId": "3d8e7f04-1a92-4c6b-9e55-77b0c2f4a3d1", "tenantName": "Contoso Retail",
      "outcome": "pending", "reason": "gated through the pending-changes inbox",
      "pendingId": "pc-3d8e-0091", "newId": "c7d2...210",
      "appliedFields": ["passwordMinimumLength"],
      "diff": [ { "path": "/passwordMinimumLength", "kind": "Modified", "before": 6, "after": 8 } ] },
    { "tenantId": "a14b6c98-2f0d-4781-bb3e-5e9c0a7d2f44", "tenantName": "Tailwind Labs",
      "outcome": "conflict",
      "reason": "live object modified since golden read; operator must re-resolve",
      "pendingId": null, "newId": null, "appliedFields": [], "diff": [] }
  ],
  "summary": { "success": 0, "skip": 1, "conflict": 1, "error": 0, "pending": 1, "total": 3 }
}
```

> **On the `conflict` outcome.** The campaign handler at fan-out time emits `skip` / `pending` /
> `error` (and `success` only for a `dryRun` preview). It never emits `conflict` directly, because
> it does not apply — every live target is enqueued as a gated replay. `conflict` is an
> **approve-time** outcome: when the operator approves a gate whose live object changed since the
> golden read, the replay no longer applies cleanly (etag/state mismatch) and that target settles to
> `conflict`. `CampaignSummary.conflict` therefore tallies the resolved (post-gate) view, and is `0`
> on the immediate fan-out result. The DoD's "run for real → per-tenant `success` / `skip` /
> `conflict`" describes that resolved state, reached one gate at a time.

### Fleet-drift summary (`GET /fleet/drift?group=grp-northwest-msp&golden=goldenTenant`)

Which tenants diverged from the resolved golden, field-level.

```json
{
  "group": "grp-northwest-msp",
  "golden": "9f1c3a52-7b44-4e2d-8f10-2c6b9d5a1e80",
  "surface": "compliance-policies",
  "tenants": [
    { "tenantId": "9f1c3a52-7b44-4e2d-8f10-2c6b9d5a1e80", "tenantName": "Fabrikam HQ",
      "status": "golden", "driftCount": 0 },
    { "tenantId": "3d8e7f04-1a92-4c6b-9e55-77b0c2f4a3d1", "tenantName": "Contoso Retail",
      "status": "inSync", "driftCount": 0,
      "note": "passwordMinimumLength=8 is an accepted override, not drift" },
    { "tenantId": "a14b6c98-2f0d-4781-bb3e-5e9c0a7d2f44", "tenantName": "Tailwind Labs",
      "status": "diverged", "driftCount": 2,
      "drifts": [
        { "field": "passwordExpirationDays", "golden": 90, "actual": 365 },
        { "field": "storageRequireEncryption", "golden": true, "actual": false }
      ] }
  ],
  "summary": { "golden": 1, "inSync": 1, "diverged": 1 }
}
```

---

## GDAP vs multi-profile

Lean **stored-profile fan-out first**; GDAP later.

- **Phase 1 — stored-profile fan-out (no new consent).** Reuse the profiles already in
  `profiles.json` (`TenantProfile` per customer, each with its own `clientId`/secret/auth method —
  `TenantProfile.cs`). `FleetSession` holds N of these live at once. Zero new Entra consent: if
  the operator can already sign into each tenant single-tenant today, fleet just signs into all of
  them. This proves the fan-out plane, tenant groups, campaigns, drift, and the per-tenant gate UX
  end-to-end before touching delegated auth.
- **Phase 2 — GDAP (once fleet UX is proven).** Granular Delegated Admin Privileges: one MSP
  partner credential reaches many customer tenants via partner relationships, replacing
  N-stored-secrets with one delegated identity scoped per customer. This is a new auth path in
  `GraphClientFactory` / `AuthSession` and new consent flows — defer until the N-profile model
  has earned it. The fan-out plane is identical either way; only how each per-tenant `Graph` is
  acquired changes.

---

## Definition of done

- A `tenant group` of **≥ 2 sandbox tenants** can be created and persisted.
- `GET /fleet/list/{surface}?group=…` returns tenant-tagged rows merged across the group, with one
  tenant's throttle/error reported per tenant (not a fleet abort).
- A `campaign` broadcasting one baseline to that group, run with `dryRun: true`, returns a
  per-tenant `/preview-diff` and writes nothing.
- The same campaign run for real reports **per-tenant `success` / `skip` / `conflict`**, and each
  `success` produced a snapshot + audit row in *that tenant's* store and was individually gated
  (no single bulk approval).
- `GET /fleet/drift` flags which tenants diverged from the resolved effective golden, field-level,
  with accepted overrides excluded from drift.

---

## Open questions

- **GDAP consent.** What partner-relationship + admin-consent flow does Phase 2 require, and can a
  single `GraphServiceClient` factory path serve both stored-secret and GDAP-delegated tenants
  without forking `AuthSession`?
- **Cross-tenant throttling.** Graph throttles per tenant *and* per app — fanning a campaign to 50
  tenants under one `clientId` may trip app-wide limits. What degree-of-parallelism / backoff /
  retry-after honoring keeps a large fleet from self-throttling, and should fan-out be queued
  rather than parallel past some N?
- **Template conflict resolution.** When a target's live object changed since the golden read
  (the `conflict` outcome above, etag mismatch), what's the resolution UX — auto re-read + re-diff,
  3-way merge against the override layer, or always kick back to the operator? And how do
  group-level vs. tenant-level overrides resolve when both touch the same field?
```
