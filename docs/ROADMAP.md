# cmProjectX — end-to-end plan: full CRUD against every surface

Unify **IntuneCommander** (management) + **cmtraceopen** (diagnostics) into one app
(Rust + WinUI/Reactor client → .NET sidecar). End state: a shipping app where **every
Intune surface supports list → view → create → edit → clone → delete → assign**, backed
by the append-only time-machine (audit/drift/snapshot/search) and the full cmtrace
diagnostics suite, with first-class safety rails on every write.

## Definition of done ("full app")

1. **Every management surface is full-CRUD**: ~50 Intune/Entra resource types — list, detail,
   create, edit, clone, delete, and (where applicable) assign — plus resource actions
   (script deploy/run-state, sync, refresh).
2. **Auth/profile lifecycle**: add/edit/delete tenant profiles; interactive / device-code /
   client-secret sign-in; multi-cloud (Commercial/GCC/GCCHigh/DoD); permission check + scope gating.
3. **Time-machine**: audit timeline, config-snapshot history, drift, full-text search, and
   **point-in-time restore** (re-apply any past snapshot) — the differentiator.
4. **Bulk & lifecycle**: backup/export, restore/import (cross-tenant, ID remap), bulk assign,
   clone-across-tenant, baseline (OIB/CIS) compare, drift-driven remediation, CA→PPTX docs.
5. **Diagnostics**: cmtrace parser workspaces (logs/IME/dsregcmd/deployment/DNS/error-DB/registry)
   + native (EVTX/Sysmon/event-log, live-tail, timeline correlation, collector, Secure Boot, Graph-WAM).
6. **Shippable**: caching/offline, uniform error model, optimistic concurrency, telemetry,
   MSIX ARM64 packaging, settings/themes, automated tests + CI.

---

## Architecture — how full CRUD scales to ~50 surfaces

You cannot hand-build 50 typed editors (each Intune type is a deep, distinct schema). The keystone
is a **universal JSON-object CRUD** path that works for every resource, with typed niceties layered on.

### 1. The CRUD slice (per surface — extends the M2 read slice)

Endpoints (all typed, OpenAPI source of truth):

| Verb | Path | Core call | Body |
|---|---|---|---|
| GET | `/{res}` | `List…Async` | — → `ListItem[]` (done M2–M4) |
| GET | `/{res}/{id}` | `Get…Async` | — → full object JSON (+ assignments) |
| POST | `/{res}` | `Create…Async` | normalized JSON → created object |
| PATCH | `/{res}/{id}` | `Update…Async` | JSON (full or merge) |
| POST | `/{res}/{id}/clone` | get→strip ids→create | optional name override |
| DELETE | `/{res}/{id}` | `Delete…Async` | — |
| POST | `/{res}/{id}/assign` | `…AssignmentsAsync` | assignment targets |

The sidecar deserializes the JSON body to the right Graph model via `@odata.type` — **reuse
`ImportService`'s existing deserialize+POST logic** (it already does "create from JSON" for ~35 types)
and `ExportService`/`ExportNormalizer` for the GET side. So the universal write path is largely
*wiring existing Core capability*, parallelizable per surface via the proven subagent batch.

### 2. The generic object editor (client) — covers ALL surfaces

The detail pane gains **View / Edit / Clone / Delete**. Edit opens the normalized object JSON in a
multiline editor (native WinUI text editor; pretty-printed, monospace, basic validation). Save:
- **Diff preview** vs the current/last snapshot (reuse `JsonDrift.Diff` — the same engine drift uses),
- **Confirm** dialog summarizing the change set,
- PATCH/POST, then **capture a fresh snapshot** (append-only) so the change is itself time-travelable.

This single component delivers create/edit/delete for every surface the day it lands. Typed forms
(below) are progressive enhancement, not a prerequisite.

### 3. Shared Assignments editor — uniform across Intune types

Assignments are a uniform Graph concept (include/exclude group targets + assignment filters + intent).
One reusable component: load current assignments, add/remove group targets (group picker over
`GroupService`), pick filters (`AssignmentFilterService`), set intent, save via the resource's assign
method. Wired per-surface by its `…AssignmentsAsync` method (subagent reports which have it).

### 4. Typed quick-edit forms — high-value/simple resources only

For shallow resources (Scope Tags, Device Categories, Named Locations, Assignment Filters, Notification
Templates) build proper typed forms (a few fields). Everything else uses the JSON editor until/unless
a typed form is worth it.

### 5. Safety rails (every write)

- **Diff preview** (what changes) before any PATCH/POST — non-negotiable.
- **Dry-run** for bulk/import (mirror CLI `DryRunConfigurationProfileService`).
- **Optimistic concurrency**: carry the Graph `@odata.etag`; PATCH with `If-Match`; on 412 re-fetch + re-diff.
- **Undo via time-machine**: every write snapshots the new body; "restore" re-PATCHes a chosen past
  snapshot's body. Time-machine + write = point-in-time rollback (a headline feature).
- **Scope gating**: grey out writes the signed-in app registration lacks permission for (`PermissionCheckService`).

### 6. Validation

Settings Catalog has embedded definitions → client-side validation possible (later). Default path:
submit and surface Graph's 400 ProblemDetails clearly in the editor. Normalize on read so diffs are noise-free.

---

## Surface inventory (CRUD coverage) — measured from the forked Core

The `crud-coverage-inventory` batch read all ~40 Core services. Result:

**Full CRUD now (~27)** — list+get+create+update+delete already in Core; the universal JSON editor
serves every one: Device Configurations, Compliance Policies, Settings Catalog\*, Administrative
Templates\*, Endpoint Security, Health/Compliance/Platform/Shell Scripts, Device Categories,
Feature/Quality/Driver Updates, App Protection Policies, Managed-Device & Targeted App Configs,
Enrollment Configurations, Autopilot, Named Locations, Auth Strengths, Auth Contexts, Terms of Use,
Scope Tags, Role Definitions, Intune Branding, Azure Branding\*, Terms & Conditions, Reusable Policy
Settings, Notification Templates. **ADMX Files** = create+delete, **no update** (immutable; replace).

**Read-only (~10)** — list/get only → view + actions, no create/delete: **Conditional Access**
(read-only *by design* — never expose write), **Applications** (read + assign; create = content-upload
flow, out of scope), VPP Tokens, Apple DEP, Cloud PC Provisioning, Assignment Filters, Policy Sets,
Cloud PC User Settings, **Managed Devices** (list-only; device actions sync/wipe/retire not in Core yet).

**Assignments:** _set_ method exists for 7 (Compliance, Settings Catalog, Admin Templates, Endpoint
Security, Platform Scripts, Shell Scripts, Applications); _read-only_ for 3 (Device Configs, Health
Scripts, Role Defs); **missing entirely** (Graph supports, Core lacks) for ~8 (Feature/Quality/Driver
Updates, Autopilot, Cloud PC Provisioning, T&C, Cloud PC User Settings) → **Core gap to fill in M7**.

**Cross-cutting enablers (power M6–M8, already in Core):**
- `ImportService` — "create object from model/file" for ~30 types (`Import<T>Async(model, migrationTable)`).
  Some take the raw Graph model, some a `*Export` wrapper. **This is the universal-create engine.**
- `ExportService` — tenant→file serializer for ~35 types (+ CA-with-resolved-GUIDs). The backup engine.
- `DriftDetectionService.CompareAsync(baseline, current)` → `DriftReport` (folder diff, severity-classified).
- `AssignmentCheckerService` — large read-only assignment-reporting engine (by user/group/device,
  unassigned, failed, compare-groups, prefetch-to-cache) → **Assignment Explorer (M6) for free**.
- `BaselineService` — embedded OIB/CIS baselines + `CompareSettingsCatalog` (sync, no Graph).
- `PermissionCheckService` — JWT `roles`/`scp` vs `RequiredPermissions` → write-gating + the perms screen.
- `GroupService`/`UserService` — read-only (groups, members, users) → assignment-editor pickers (M7).

**\*Per-service write quirks the editor/sidecar must handle** (documented in coverage notes):
Settings Catalog update is **split** (metadata PATCH + settings delete-then-POST with rollback);
Named Location update **rebuilds a subtype-aware patch**; Azure Branding resolves the **org id** + edits
localizations; Admin Templates manage only the **container** (not GPO definition values); apps have **no
create**; many PATCHes use `GraphPatchHelper.PatchWithGetFallbackAsync` (re-GET on null body).

### What this means for the write layer
1. **One new sidecar primitive** unlocks create/update for all ~27 full-CRUD surfaces: deserialize the
   JSON body → the **typed Graph model** (driven by `@odata.type`, via the Graph SDK parse-node), then
   call that service's typed `Create…/Update…`. Equivalently, route create through `ImportService`'s
   per-type `Import<T>Async` (already does model-from-JSON). Delete is just `Delete…Async(id)`.
2. **Read-only surfaces** skip write endpoints entirely (esp. Conditional Access).
3. **M7 adds ~8 missing `Assign…Async` methods** to Core (small, mirror the 7 that exist) + the shared
   assignments editor; the assignment *reporting* (Explorer) needs no new Core (AssignmentChecker exists).
4. **M8 bulk = wiring existing engines**: Backup→`ExportService`, Restore→`ImportService` (+MigrationTable
   group remap), Drift-folder→`DriftDetectionService`, Baseline→`BaselineService`, Explorer→`AssignmentChecker`.

---

## Flight plan (layered — read → view → write → bulk → diagnostics → ship)

Done: **M1–M5** unified shell + read/view across ~39 surfaces LIVE · **M6** write core LIVE
(generic JSON CRUD **+ diff-preview/confirm gate, Clone, snapshot-on-write, restore/undo**) ·
**M7** assignments editor (group/filter pickers pending) · **M8** Backup/Export + Import dry-run +
Security Baselines wired (cross-tenant clone/full restore pending) · **M9** 7 local parsers LIVE ·
**M10** native Event Log/Sysmon/Secure Boot + Timeline Correlation + Diagnostics Collector LIVE ·
**M11** profile add/edit/delete + multi-cloud selector + device-code/secret sign-in LIVE.

| Flight | Layer | Scope | Pattern |
|---|---|---|---|
| **M4** | Read breadth | Tenant Admin (11) + Groups LIVE → every plain list LIVE | A list-slice (subagent batch) |
| **M5** | View depth | `GET /{res}/{id}` per surface + detail pane: object JSON view, assignments view, typed summary; clone-source ready | A+ (subagent batch: which Get method) |
| **M6** | **Write core** | Generic `POST/PATCH/DELETE/clone` per surface (reuse Import/Export); generic JSON editor + diff-preview + confirm + eTag + snapshot-on-write + undo. **Full CRUD against all surfaces lands here.** | universal JSON CRUD (subagent batch: which Create/Update/Delete) |
| **M7** | Write depth | Shared **Assignments editor**; typed quick-edit forms for shallow resources; scope/permission gating of write actions | C custom UI |
| **M8** | Bulk & lifecycle | Backup/Export, Restore/Import (cross-tenant, migration table), Bulk Assign, Clone-across-tenant, Baseline (OIB/CIS) compare, Drift-driven remediation, CA→PPTX (Syncfusion) | D bulk/dry-run |
| **M9** | Diagnostics (local) | `parser_workspace(kind)`: Log Explorer, Intune IME, dsregcmd, Deployment, DNS-debug, Error-DB, Registry — pure `cmtraceopen-parser`, no auth | B parser (subagent batch) |
| **M10** | Diagnostics (native) | EVTX/Sysmon/Event-Log + live-tail first; then Timeline correlation, Collector, Secure Boot, Graph-WAM. macOS dropped | E native (sidecar/Rust) |
| **M11** | Identity/cross-cut | Profile add/edit/delete UI; sign-in flows (interactive/device-code/secret); multi-cloud selector; permission-check screen; token-expiry handling | C custom UI |
| **M12** | Ship | Cache/offline (LiteDB read-through), uniform error model, telemetry, MSIX **ARM64** packaging + updater, settings/themes (port cmtrace's 9), test suite + CI | infra |

**Parallelizable tracks** (independent of the management write path): M9 (parser diagnostics) and M11
(auth/profile UI) can run anytime; M10 (native) is self-contained. The management spine is M4→M5→M6→M7→M8.

### Per-flight DoD
Each management flight: `cargo build` + `dotnet build` green → sidecar up, `POST /auth/signin` →
curl new endpoints (read) / round-trip a create-edit-delete on a throwaway object (write) → app
screenshot. Contract (`openapi.yaml`) synced. For writes: a diff-preview + confirm must gate every mutation.

---

## Cross-cutting systems (build alongside, not at the end)

- **Error model**: one envelope — transport (`service_err`), Graph ProblemDetails (surfaced verbatim in
  editor), validation, permission. Client renders by class (already started: timeout vs unreachable vs decode).
- **Caching/offline**: `CacheService` (LiteDB, already in Core) as read-through for lists/details;
  writes invalidate. Lets the app open + browse last-known state before sign-in completes.
- **Concurrency/consistency**: eTags on every editable object; the append-only snapshot store is the
  audit trail of *our own* writes (not just synced state).
- **Multi-cloud + permissions**: `Cloud` flows through `TenantProfile`/`/health`; gate writes on
  `PermissionCheckService`; surface a cloud indicator in the status bar.
- **Packaging**: self-contained `win-arm64` sidecar + WinUI client; MSIX arm64; auto-updater (port
  cmtrace's updater pattern). DataProtection app-name `IntuneManager` stays frozen.
  - **Embed the sidecar — no separate JIT exe.** Ship the sidecar as a NativeAOT (`PublishAot`,
    win-arm64) native exe bundled next to `app.exe`, auto-spawned + auto-killed by the client (no
    machine .NET dependency, no ~6s JIT cold-start, no manual port-5099 management). Risk: AOT-compat
    of Graph SDK + Kiota + Lucene.NET + LiteDB + DataProtection + System.Text.Json (source-gen). If
    AOT-hostile, fall back to self-contained single-file + ReadyToRun. The Api.Tests fixture already
    prototypes the spawn/drain/kill lifecycle.
- **Testing/CI**: contract round-trip tests (OpenAPI ↔ DTOs), sidecar integration tests against a
  recorded Graph, Rust UI smoke (the existing run+screenshot harness), `cargo build`+`dotnet build` gates.

## Open decisions (surface before the relevant flight)

1. **JSON editor depth** — native multiline TextBox now vs. a richer embedded editor later (syntax
   highlight/fold). Start native; revisit in M7.
2. **Write blast-radius default** — dry-run ON by default for bulk/import; single-object edits go
   straight to diff-preview+confirm. Confirm before M6.
3. **Cross-tenant clone** — needs ID remap (migration table exists in ImportService). M8.
4. **Settings-catalog typed editing** — huge; JSON-edit covers it in M6, typed settings UI is a
   stretch goal (own mini-flight) only if demanded.
5. **macOS diagnostics** — dropped; reconfirm.

## Patterns legend
A list-slice · A+ detail-slice · B parser-local · C custom-UI · D bulk/dry-run · E native.

---

# Part II — The platform play (M14–M22): from "manages every surface" to "an autonomous control plane for the fleet"

M1–M13 (+ the RT regression program) met the MVP thesis: **full CRUD across ~50 surfaces, a
time-machine, diagnostics, and an MCP/HITL control plane.** Most tools stop here and spend their
second act adding surfaces. That would waste what we've actually built.

cmProjectX is quietly sitting on **three assets almost nobody combines in one product:**

1. **A uniform CRUD contract over every surface** — `(verb, path, body)` works for all ~50 resources,
   per-surface Graph quirks already absorbed behind it (M6).
2. **An append-only, content-hashed, full-text-indexed time-machine** — every config state and every
   write we make is a deduped, searchable, restorable snapshot (Store).
3. **An MCP/HITL control plane** — `propose → human-approve-the-diff → replay → audit`, already wired
   to a real agent surface (M13).

Those three together are the substrate for something a single-tenant CRUD app is not: a
**declarative, simulated, autonomous, auditable control plane for an entire device fleet.** Part II
cashes that in. The discipline from Part I holds — **every bet below is anchored to an engine the
fork already has**; ambition here is *recombination*, not fantasy. Where a bet needs net-new Core, it
says so.

> **Detailed designs:** each flight below is built out into a standalone design doc — thesis,
> architecture, contract additions, **sample data**, and DoD — under [`docs/part-ii/`](./part-ii/)
> (see the [index](./part-ii/README.md)). This section is the map; those docs are the territory.

## The arc

```
  M14  Hands ........ act on devices, not just configs        (close the last read-only gap)
  M15  Source ....... tenant config becomes Git: plan/apply   (time-machine ⟶ GitOps)
  M16  Foresight .... simulate any change before it lands     (blast-radius engine)
  M17  Twin ......... a queryable graph model of the tenant   (analytics: orphans, conflicts, paths)
  M18  Autonomy ..... closed-loop watch→plan→simulate→gate→apply→verify   (the AI SRE)
  M19  Posture ...... continuous benchmark scoring + audit evidence       (compliance as a product)
  M20  Fleet ........ 100s of tenants, golden templates, campaigns        (MSP scale)
  M21  Ecosystem .... shareable policy packs + remediation playbooks + plugin market
  M22  Moonshot ..... the same contract over Jamf / Workspace ONE         (beyond Intune)
```

**M14 → M15 → M16 → M17 → M18** is the spine — each makes the next possible (you can't safely
automate what you can't simulate; you can't simulate without a twin; the twin is fed by the read
surfaces and stored by the time-machine). **M19/M20** ride alongside; **M21/M22** are the platform
endgame.

## Grounding: measured Core gaps vs. existing engines

A second inventory pass over `service/Core/Services/`:

- **`ManagedDeviceService` is list-only** (`ListManagedDevicesAsync`, nothing else). Every device
  *action* is a Core gap. The last big read-only surface. → **M14** (net-new Core).
- **`SettingsCatalogService` exposes a policy get but no setting-*definition* catalog** → typed
  authoring + human-readable catalog drift need that fetch (deferred M6 decision #4). → folded into M15.
- **`MacCustomAttributeService` has full CRUD but is missing from the Part-I inventory** → one free
  registry row. → M14 housekeeping.
- **`ExportService` (≈35 types) + `ImportService` (migration table) + `JsonDrift.Diff` + the
  content-hashed snapshot store** are *exactly* a serialize / desired-state-diff / apply / version
  triad — they just aren't exposed as one. → **M15 is mostly wiring**, not new Graph.
- **`AssignmentCheckerService`** already resolves assignments **by user / group / device** (incl.
  unassigned/failed/compare-groups). That's the kernel of a blast-radius simulator. → **M16**.
- **All read services + `GroupService`/`UserService`/`DirectoryObjectResolver` + the store** already
  hold every node and edge of a tenant graph; nothing *assembles* them into one model. → **M17**.
- **`DriftDetectionService` + M13 pending-changes inbox + `BaselineService`** exist but only run
  on demand and in isolation. The autonomy loop and posture product are about **connecting** them. →
  **M18 / M19**.
- **`ConditionalAccessPptExportService` + `CaPptExport` + `AssignmentReportExporter`** are shipped,
  single-purpose exporters → generalizable evidence/report engine. → M19.
- **Zero multi-tenant** — `TenantProfile` is one tenant per sign-in. → **M20** (net-new fan-out).

---

## The bets

### M14 — Hands: live device & remote action
**Thesis:** you can edit every *policy* but can't touch a single *device*. Close it.
New Core action layer on `ManagedDeviceService`: sync, restart, wipe, retire, rename, fresh-start,
remote-lock, locate, rotate BitLocker/FileVault, **collect diagnostics** (pull logs straight into the
M9/M10 diagnostics workspaces — Graph → cmtrace, no manual file shuffling), Defender quick-scan,
autopilot-reset. Device detail (hardware, compliance, installed apps, action history). **Confirm-gated
bulk actions.** Fold in `MacCustomAttribute` as full-CRUD.
**Pattern F — device-action:** POST a Graph action verb for a target device-set, gated by the M6
confirm dialog, snapshotted as an audit event (the *action* is the record — no config body to diff).
**Why first:** remote diagnostics collection makes the twin (M17) and the autonomy loop (M18) able to
reason about device *health*, not just config.

### M15 — Source: Policy-as-Code / GitOps (the keystone reframe)
**Thesis:** the time-machine is already a content-hashed, append-only version history. Expose it as
**Git**, and tenant configuration becomes a versioned, reviewable, CI-validatable artifact.
- **`cmpx pull`** — `ExportService` serializes the live tenant into a normalized, GUID-resolved file
  tree (one file per object). **`cmpx push`** — `ImportService` (+ MigrationTable) applies a desired
  state. **`cmpx plan`** — `JsonDrift.Diff` between repo and tenant → a Terraform-style change set
  *before* anything is written.
- The snapshot store becomes the **local mirror** of a Git source of truth; every M6 write already
  snapshots, so history is free. Round-trips through a real Git remote → **PR-reviewed tenant
  changes, CI-gated**, full blame on who changed which setting when.
- **Desired-state convergence:** "tenant should equal `main`" → plan → gated apply → re-converge.
**Why it matters:** this is the single highest-leverage reframe in the doc. It turns an interactive
admin tool into **infrastructure-as-code for Intune** — and it's ~80% wiring of Export/Import/Drift.

### M16 — Foresight: the What-If / blast-radius simulator
**Thesis:** never let an operator (or an AI) apply a change blind. Before *any* write — manual, GitOps
apply, or AI proposal — simulate impact across the assignment graph.
Reuse `AssignmentCheckerService` (by user/group/device) to answer, pre-flight: *"this CA change locks
out 4,200 users,"* *"this compliance policy flips 800 devices to non-compliant,"* *"this assignment
overlaps an existing exclude."* Output is a **blast-radius report** attached to the M6 confirm gate
and the M13 inbox diff. Conflict/redundancy detection between the proposed and live assignment sets.
**Why it gates autonomy:** an automated remediation you can't simulate is an automated outage. M16 is
the safety precondition for M18.

### M17 — Twin: the tenant digital twin + graph analytics
**Thesis:** every node (devices, users, groups, policies, CA, assignments, filters) and edge already
flows through the read services and lands in the store — but nothing *assembles* them into one model.
Build a **local graph** of the tenant and run analytics that no per-screen view can:
orphaned policies (assigned to nothing / to empty groups), **conflicting or redundant assignments**,
assignment **cycles**, CA **privilege/escape paths**, coverage gaps ("no compliance policy targets
these 300 devices"), drift hotspots over time. Queryable, offline (served from the snapshot store +
cache), and the **substrate the AI reasons over** instead of re-listing 50 endpoints per question.

### M18 — Autonomy: the closed-loop AI SRE (the headline)
**Thesis:** wire M13 + M16 + M17 + `DriftDetectionService` into a **continuous, human-gated control
loop** — the differentiator that no Intune tool ships.
`service/Sync/` (today a stub) becomes a **scheduled background watcher**: delta-sync → detect drift,
posture regressions, new advisories → an agent **plans** a remediation → **simulates** it (M16) →
enqueues a `propose_*` into the **M13 inbox** with the diff *and* the blast-radius → operator approves
→ replay-apply → **verify re-converged** → audit the whole chain. Natural-language fleet ops over the
twin ("make all Windows devices CIS L1, show me the blast radius first"). **Locked guarantee holds:
rules/AI propose, a human approves the exact diff, nothing auto-applies** (M13 policy, unchanged). The
loop is autonomous in *watching and proposing*; humans stay the approval authority.

### M19 — Posture: continuous compliance as a product
**Thesis:** `BaselineService` (OIB/CIS embedded) + the report engines already exist — turn one-shot
comparison into a **continuously-scored posture product**. Benchmark scoring against CIS / OIB /
Essential 8 / NIST, trend dashboards over the time-machine, **audit-evidence export** (generalize
`ConditionalAccessPptExportService` + `AssignmentReportExporter` to all-surface, point-in-time
evidence packs for SOC2/ISO/Essential-8 assessors), automated POA&M from open drift. Posture
regressions feed M18 as remediation candidates.

### M20 — Fleet: MSP-scale multi-tenant
**Thesis:** every endpoint already keys cache+store by `{tenantId}`; lift that into **first-class
fan-out**. Tenant groups; a **fleet view** spanning N tenants; **golden-tenant templates** with
inheritance + per-tenant overrides; **cross-tenant campaigns** (broadcast a baseline/change, reuse
Export→Import+MigrationTable per target, each gated); fleet-wide drift and posture. GDAP/delegated
sign-in. **Pattern G — multi-tenant:** the same `(verb, path, body)` replayed across a tenant set,
results merged tenant-tagged, each write a gated M13 replay (a fleet write is N gated replays).

### M21 — Ecosystem: packs, playbooks, and a plugin market
**Thesis:** GitOps (M15) + MCP plugins (M13.3) make config and capability *portable* — so make them
*shareable*. Versioned **policy packs** and **baselines** (a Git repo of desired state others can
adopt), **remediation playbooks** (parameterized propose-sets the autonomy loop can run), and a
**plugin marketplace** over the M13 MCP aggregation (ServiceNow, CMDB, ticketing, PowerShell-remediation
servers) — all flowing through the one governed approval inbox + audit.

### M22 — Moonshot: beyond Intune (cross-MDM)
**Thesis:** M6 already proved a *uniform contract can hide per-surface quirks.* Push that abstraction
one level up — **the same `(verb, path, body)` contract, plan/apply, twin, and autonomy loop over a
second MDM** (Jamf, Workspace ONE). The client, time-machine, simulator, and HITL plane are
provider-agnostic by construction; only a new Core provider behind the contract is new. Turns
"an Intune tool" into "**the** cross-platform device-management control plane." Explicitly a stretch —
listed to set the ceiling, not committed.

---

## Flight summary (Part II)

| Flight | Bet | Anchored in | Net-new Core? |
|---|---|---|---|
| **M14** | Live device & remote action | `ManagedDeviceService` (list-only today) | **Yes** — device-action verbs |
| **M15** | Policy-as-Code / GitOps | Export + Import + JsonDrift + snapshot store | No — wiring |
| **M16** | What-if / blast-radius simulator | `AssignmentCheckerService` | Mostly no |
| **M17** | Tenant digital twin + analytics | all read services + store + DirectoryObjectResolver | No — assembly |
| **M18** | Autonomous watch→plan→simulate→gate→apply→verify | `service/Sync` (stub) + DriftDetection + M13 inbox + M16/M17 | **Yes** — sync + agent |
| **M19** | Continuous posture + audit evidence | `BaselineService` + the 3 export engines | No — wiring |
| **M20** | MSP-scale multi-tenant fleet | tenant-keyed cache/store + Export/Import | **Yes** — fan-out + GDAP |
| **M21** | Packs / playbooks / plugin market | M15 GitOps + M13.3 MCP aggregation | No |
| **M22** | Cross-MDM (Jamf/WS1) | the uniform M6 contract | **Yes** — a provider |

## New patterns
- **F — device-action:** verb POST over a device-set + confirm gate, audited as an event (no diff body).
- **G — multi-tenant:** keyed fan-out of the uniform contract across a tenant set; each write a gated M13 replay.
- **H — declarative:** plan (`JsonDrift`) → gate → apply (`ImportService`) → snapshot → verify-converged. Reused by M15 GitOps, M18 autonomy, and M20 campaigns alike.

## Per-flight DoD (Part II)
Part-I gate (`cargo build` + `dotnet build` green, contract synced, app screenshot) plus:
- **M14:** a sandbox device round-trips a reversible action (sync) E2E; destructive actions
  (wipe/retire) require typed-confirmation + scope-check + audit, never one-click; bulk destructive is
  opt-in per deployment with a size cap and a second confirmation past a threshold.
- **M15:** `pull → plan → push` round-trips a sandbox object with a byte-identical body to the JSON
  path; `plan` shows zero diff against a freshly-pulled tree; a PR-gated apply converges the tenant.
- **M16:** a simulated CA/compliance change reports affected user/device counts *before* apply, and the
  number matches the post-apply reality on a sandbox object.
- **M17:** the twin answers "orphaned policies" and "conflicting assignments" offline (cache/store
  only), and an injected orphan/conflict shows up.
- **M18:** an injected drift surfaces as a pending change carrying both a diff *and* a blast-radius;
  approving it re-converges and the verify step confirms zero residual drift — fully audited.
- **M19:** a posture score + a point-in-time evidence pack regenerate after a change and show the delta.
- **M20:** a campaign to ≥2 sandbox tenants reports per-tenant success/skip/conflict, each write gated.
- **M21:** an externally-authored policy pack and a downstream plugin both drive a gated write E2E.
- **M22:** (stretch) a single read surface served by a non-Intune provider behind the same contract.

## Locked guarantees (hold through Part II — non-negotiable)
- **Propose → human-approve-the-exact-diff → apply.** No auto-apply, ever — autonomy is in *watching,
  planning, and simulating*, never in unattended writes (M13 policy, unchanged).
- **Conditional Access stays read-only by design.** M16 simulates it, M19 documents it, M15 versions
  it — nothing writes it.
- **No Graph logic in the Rust client.** Part II adds Core methods (device actions, a sync watcher, a
  provider) and *assembles/wires* existing engines; it does not move Graph into Rust.
- **Append-only store.** GitOps (M15) mirrors it; it never mutates history in place.
- **Simulate before automate.** No M18 remediation reaches the inbox without an M16 blast-radius.

## Open decisions (surface before the relevant flight)
1. **Destructive-action gate (M14).** Typed-confirmation + scope-check + org-level action allowlist;
   bulk caps + second confirmation. Confirm before M14.
2. **GitOps remote & secrets (M15).** Local Git mirror only, vs. push to a real remote (GitHub/ADO) —
   and how tenant secrets stay out of the repo (config is non-secret; references resolve at apply).
3. **Simulation fidelity (M16).** Group membership can be dynamic/large; cache the assignment graph
   via `ICacheService` and state staleness in the report. How fresh is fresh enough?
4. **Twin storage (M17).** Reuse the SQLite store with a graph projection vs. an embedded graph engine.
   Lean: projection over the existing store first.
5. **Autonomy scope (M18).** Which signals may auto-*enqueue* proposals (drift, posture, advisories)?
   Lean: drift + posture first; advisories later. Auto-*apply* remains out of scope, permanently.
6. **GDAP vs. multi-profile fan-out (M20).** Lean: stored-profile fan-out first (no new consent),
   GDAP once fleet-view UX is proven.
7. **M22 commitment.** Moonshot only — revisit after M18 ships and the contract has proven provider-agnostic.

## Patterns legend (Part II additions)
F device-action · G multi-tenant fan-out · H declarative plan→gate→apply→verify.
