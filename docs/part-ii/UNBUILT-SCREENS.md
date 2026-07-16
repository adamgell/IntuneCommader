# Unbuilt screens — build-out plan

Every screen in the app is one row in `app/src/features.rs`. This document inventories the
**7 stubbed** screens (`Screen::Stub` — nav-present, "coming soon" placeholder rendered at
`app/src/main.rs:1518`) plus **1 partially-built** screen (`managed-devices` — M14 backend landed,
client UI pending), with a grounded build-out spec for each: what Core already exists, what each
layer needs, the closest template to copy, effort, dependencies, an ordered checklist, risks, and a
sandbox smoke test.

Specs were produced by a per-screen investigation pass over the codebase; every claim cites a real
`file:line`. Use this to knock the screens out in one ultracode session.

## Summary

| # | Screen | Tag | Section | Status | Effort | Net-new Core? | Engine already exists |
|---|---|---|---|---|---|---|---|
| 1 | CA → PowerPoint | `ca-pptx` | Identity | stub | **S** | No | `ConditionalAccessPptExportService.ExportAsync` (fully built) |
| 2 | Role Assignments | `role-assignments` | Tenant Admin | stub | **S** | tiny (1 GET-by-id) | `RoleDefinitionService.GetRoleAssignmentsAsync` |
| 3 | Application Assignments | `app-assignments` | Apps | stub | **S** | No | `/apps/{id}/assignments` + `assignment_editor` |
| 4 | Cache Sync | `cache-dev` | Drift & Compare | stub | **S** | No | `ICacheService` (GetMetadata/Invalidate/Cleanup) + warm lambda |
| 5 | Managed Devices — detail + actions | `managed-devices` | Devices | partial | **M** | No (M14 done) | full M14 backend; pure client work |
| 6 | Policy Comparison | `policy-comparison` | Drift & Compare | stub | **M** | endpoint+DTOs | `BaselineService.CompareSettingsCatalog` |
| 7 | Bulk App Assignment | `bulk-assign` | Apps | stub | **M** | endpoint+DTOs | `ApplicationService.AssignApplicationAsync` (loop it) |
| 8 | Detection & Remediation | `detection-remediation` | Drift & Compare | partial | **M** | endpoints+DTOs | `DeviceHealthScriptService` run-state + on-demand |

**None require net-new Graph engine code** — every screen is wiring an existing Core capability
through the contract → sidecar → DTO → client layers.

## Recommended ultracode session order

Cheapest, lowest-risk wins first; client-heavy ones after.

1. **`ca-pptx`** — engine done, 1:1 `/export` template. ~30 lines endpoint + small screen.
2. **`role-assignments`** — add one Core GET-by-id, then a read-only `list(...)` flip.
3. **`app-assignments`** — backend ships today; a small dedicated screen reusing `assignment_editor`.
4. **`cache-dev`** — all `ICacheService` primitives exist; one read endpoint + 2 actions + a screen.
5. **`managed-devices`** — pure client; finishes M14 (no backend/contract/DTO work).
6. **`policy-comparison`** — engine exists; new compare endpoint + DTOs + two-picker UI.
7. **`bulk-assign`** — thin loop over an existing per-app write + a multi-select client.
8. **`detection-remediation`** — Core done; new projections + a bespoke 3-pane screen.

Batches 1–4 are "S" and can be done back-to-back; 5–8 are "M" (each needs a bespoke client screen).

## Cross-cutting notes (read once)

- **Contract debt — assignment endpoints are undocumented.** `/apps/{id}/assignments` (and the
  per-resource assignment routes) and the `Assignment`/`AssignmentDto` shape are hand-wired and
  **not** in `contract/openapi.yaml`, violating the source-of-truth rule. `app-assignments` and
  `bulk-assign` should formalize the `Assignment` schema while they're in there. Mirror
  `crates/api-types/src/lib.rs:400` (Rust) and `Assignments.cs:11` (C#) byte-for-byte.
- **Reactor re-render gotcha (CLAUDE.md).** State-driven children under `NavigationView` won't
  repaint on their own state — lift action/working/save state to the dispatched parent workspace's
  hook context (this is why `list_workspace` keeps the assignment editor + save mutation in the
  parent). The `assignment_editor` is a render helper, **not** a nested `component()`. Key bespoke
  detail panes with `.with_key(...)` so switching selection fully remounts.
- **No modal/`ContentDialog` primitive in the reactor fork.** Confirm gates must be **inline
  panels** modeled on the M6 review step (`main.rs:1071-1143`), not popups.
- **Device-action env gates.** Destructive verbs are server-gated behind
  `CMPROJECTX_DEVICE_ACTIONS_DESTRUCTIVE=1` (plus `_DISABLED` / `_BULK_CAP`); the client can't enable
  them — surface the server's 403 rather than hiding the button.
- **Volatile reports bypass the cache.** Run-state / device-state / compare results must go straight
  to Graph (follow `AssignmentExplorerEndpoints.cs`), not through the M12.1 `CachedReader`.

---

## 1. `ca-pptx` — CA → PowerPoint  (S)

**Status:** stub (`features.rs:206`). **Effort: S** — the entire `.pptx` engine, templates, GUID
resolution, and Syncfusion dep already exist; ~30 lines of endpoint + a small screen.

**Already exists:**
- `ConditionalAccessPptExportService.ExportAsync(outputPath, tenantName, ct)` — fully implemented:
  lists CA policies, batch-resolves directory/location/auth-strength/auth-context/terms-of-use
  GUIDs, clones one slide per policy from embedded templates, writes the `.pptx`.
- `service/Core/Assets/PolicyTemplate.pptx` + Syncfusion.Presentation.Net.Core 32.2.5 (`Core.csproj:31`).
- `BulkEndpoints.cs:/export` — the temp-file + `Results.File(bytes, contentType, fileName)`
  binary-download pattern to copy. All ctor deps take a single `GraphServiceClient` (`auth.Graph`).

**Layers:**
- **Contract:** add `GET /conditional-access/pptx` (copy `/export` at `openapi.yaml:233-243`; 200
  content type `application/vnd.openxmlformats-officedocument.presentationml.presentation`, 409).
- **Sidecar:** one `MapGet("/conditional-access/pptx")` in `IdentityEndpoints.cs` — guard
  `auth.Graph` (409), construct `ConditionalAccessPptExportService` inline with the 7 `g`-based
  services, write to a temp `.pptx`, `await ExportAsync(tmp, auth.ActiveProfile?.Name ?? "tenant", ct)`,
  return `Results.File(bytes, pptx-mime, dated-filename)` in a `try/finally` that deletes the temp.
- **DTOs:** none (binary).
- **Client:** flip `features.rs:206` to `bulk("ca-pptx", "CA → PowerPoint", Identity, "ca-pptx")`;
  add a `ca_pptx_workspace` (copy `bulk.rs::export_workspace`) + `api_client.export_ca_pptx()` (copy
  `export_backup`); add the `"ca-pptx"` arm to the `Screen::Bulk` match (`main.rs:144`).

**Risks:** Syncfusion may need a license key at runtime (verify the `.pptx` opens); large tenants
make export slow (bump the client http timeout); temp-file cleanup must be in `finally`;
`tenantName` must be non-empty (`ExportAsync` throws on whitespace).

**Smoke:** `curl -s -o ca.pptx -w '%{http_code} %{content_type}' .../conditional-access/pptx` → 200
+ presentationml; `unzip -l ca.pptx` shows `ppt/slides/slide1..N`. Signed-out → 409. In-app: Identity
→ CA → PowerPoint → Export → `.pptx` opens with one cover + one slide per policy.

---

## 2. `role-assignments` — Role Assignments  (S)

**Status:** stub (`features.rs:210`). **Effort: S** — Core LIST exists; net-new is one ~5-line Core
GET-by-id + two copy-paste handlers + a one-line `features.rs` flip. Ship **read-only** (`rw=false`).

**Already exists:**
- `RoleDefinitionService.GetRoleAssignmentsAsync()` (line 77) — lists all tenant role assignments
  with paging → `List<DeviceAndAppManagementRoleAssignment>`.
- `TenantAdminEndpoints.cs:65-104` — the role-definitions GET/GET{id} module to copy.
- `ListItemDto` + `list_workspace` — generic master/detail, no per-screen client code needed.

**Missing (small):** `IRoleDefinitionService` has **no** `GetRoleAssignmentAsync(string id)` — the
detail pane needs it. Add it mirroring `GetRoleDefinitionAsync` (line 46) via
`_graphClient.DeviceManagement.RoleAssignments[id].GetAsync`.

**Layers:**
- **Contract:** add read-only `/role-assignments` (list) + `/role-assignments/{id}` (detail),
  copying `/role-definitions` (`openapi.yaml:1098-1124`), dropping post/patch/delete.
- **Sidecar:** two `MapGet` handlers in `TenantAdminEndpoints.cs` after the role-definitions block;
  cache dataType key `"RoleAssignments"` (must be distinct from `"RoleDefinitions"`).
- **DTOs:** none — reuse `ListItemDto` + raw object JSON.
- **Client:** flip `features.rs:210` → `list("role-assignments", "Role Assignments", TenantAdmin, "/role-assignments", false)`.

**Risks:** `DeviceAndAppManagementRoleAssignment` exposes member/scope **GUIDs**, not names — list
subtitle/detail show raw GUIDs (acceptable for read-only v1); ensure the by-id return type matches
the list type so projections stay consistent.

**Smoke:** `curl .../role-assignments` → `ListItemDto[]`; `curl .../role-assignments/{id}` → object
JSON (not 404); UI: Tenant Admin → Role Assignments lists rows, detail pane renders JSON. (Tenant
needs ≥1 Intune RBAC role assignment.)

---

## 3. `app-assignments` — Application Assignments  (S)

**Status:** stub (`features.rs:190`). **Effort: S** — backend, DTOs, editor, and api_client methods
all already ship via the `/apps` screen. The work is client + closing a contract gap.

**Already exists:**
- `IApplicationService.GetAssignmentsAsync` / `AssignApplicationAsync` (replace-all write).
- `AppsEndpoints.cs:182/190` — GET/POST `/apps/{id}/assignments` (live, with group-name resolution).
- `assignments.rs::assignment_editor` (app variant, `with_intent=true`) + the "assign" block at
  `main.rs:1172-1196`; `api_client.rs:305/316` `get_assignments`/`set_assignments`.

**Layers:**
- **Contract:** close the gap — document `GET`/`POST /apps/{id}/assignments` + the `Assignment`
  schema (mirror `lib.rs:400` / `Assignments.cs:11`). (Shared with `bulk-assign`.)
- **Sidecar / DTOs:** none net-new.
- **Client:** convert the stub to a dedicated **assignments-first** screen — a small
  `app_assignments_workspace` in `main.rs` that lists apps (`get_list("/apps")`) and on-select opens
  `assignment_editor("/apps", id, true, ..)` directly (no object-detail/edit chrome), copying the
  seed/save wiring from `list_workspace` (`main.rs:744-806`). Give it its own tag so `.with_key(tab)`
  remounts independently from the Applications screen.

**Risks:** **redundancy** — single-app assignment editing already exists on the Applications screen;
define the value-add (assignment-first UX / cross-app overview) or it's nav noise. **Replace-all**
semantics — `AssignApplicationAsync` overwrites the whole set; keep the "Save replaces all" warning.
Raw-GUID group/filter entry (no resolver). Reactor: keep working/save state in the parent.

**Smoke:** Apps → Application Assignments → pick app → assignments load (names resolved) → add
`+Required` group → Save → POST 204, list reloads with the new row; re-list `/apps` shows the app
badged `assigned`.

---

## 4. `cache-dev` — Cache Sync  (S)

**Status:** stub (`features.rs:230`). **Effort: S** — every backend op exists; work is one read
endpoint + 2 action endpoints + ~4 small DTOs + a screen. The only friction: hoist Program.cs's
local `warmCache` Func so the warm endpoint can call it.

**Already exists:**
- `ICacheService`: `GetMetadata(tenantId, dataType)` → `(CachedAt, ItemCount)` without
  deserializing; `Invalidate(tenantId, dataType?)`; `CleanupExpired()`; `IsAvailable` (all impl'd
  over LiteDB in `CacheService.cs:197-242`).
- `AssignmentCheckerService.PrefetchAllToCacheAsync` (the warm engine) + the **existing**
  `warmCache` lambda at `Program.cs:54-79` (reuse, don't rebuild).
- `Surfaces.All` with per-surface `CacheKey` — the authoritative key list to iterate;
  `/health` already returns `lastWarmedUtc`.

**Layers:**
- **Contract:** `GET /cache` (→ `CacheEntryStatus[]` {key, displayName, cachedAtUtc?, itemCount,
  warmAhead}), `POST /cache/warm?force=`, `POST /cache/evict?key=`; + a `CacheSummary` header schema.
- **Sidecar:** new `CacheEndpoints.cs` (`MapCache()`, wired after `Program.cs:279`) iterating
  `Surfaces.All.Where(CacheKey!=null)` × `GetMetadata`; warm reuses the hoisted lambda (202);
  evict → `Invalidate` (204).
- **DTOs:** `CacheEntryStatus` + `CacheSummary` in both `Contracts.cs` and `lib.rs`. (Could reuse
  `ListItem` to avoid new DTOs.)
- **Client:** new `Screen::Cache` + `screen_cache.rs` (copy `screen_tiles.rs` for the inspect grid +
  `bulk.rs` for Warm/Evict buttons); 3 `api_client` methods.

**Risks:** read `tid` from `auth.ActiveProfile.TenantId` and 409 when signed out (never touch another
tenant's keys); hoisting `warmCache` out of its closure is the one non-mechanical step (keep
`authSession.CacheWarm` wiring intact); DETAIL keys are lazy-only and **not** enumerable — grid shows
LIST-key metadata only; render a banner when `IsAvailable==false` (NullCacheService).

**Smoke:** `curl .../cache` → ~30 entries; in-app Drift & Compare → Cache Sync shows ages + item
counts matching `/health.lastWarmedUtc`; Evict-all clears, Warm repopulates and advances
`lastWarmedUtc`; signed-out → 409 empty state.

---

## 5. `managed-devices` — detail pane + action UI  (M)

**Status:** partial — **M14 backend fully landed**; the client device-detail pane + action UI is the
remaining work. **Effort: M** — pure Rust/WinUI; no backend/contract/DTO work.

**Already exists (all backend):** `GET /managed-devices/{id}` (detail), `GET /managed-devices/actions`
(11-verb catalog), `POST /managed-devices/{id}/actions/{action}` (single, gated + typed-confirm +
audited), `POST /managed-devices/actions/{action}` (bulk); DTOs `DeviceActionInfo` /
`BulkDeviceActionResult` already mirrored in `Contracts.cs` + `lib.rs`; schemas in `openapi.yaml`.

**Layers — client only:**
- `api_client.rs`: `list_device_actions()`, `run_device_action(id, action, confirm, params)`,
  `run_bulk_device_action(action, ids, confirm)` (model on `post_text`/`preview_diff`).
- Add `Screen::Devices` + `screen_devices.rs` (copy `screen_ca.rs` as the dedicated-screen template);
  flip `features.rs:187` to it. Detail pane reuses `config_view::render_config` (inventory read).
- Action-button row from the catalog: non-destructive verbs = one-click `use_mutation`; destructive
  (retire/wipe/cleanWindowsDevice) open an **inline typed-confirm panel** (model on the M6 review
  step `main.rs:1071-1143`) — Confirm enabled only when input == device name, passes
  `confirm=device_name`. Surface server 400/403 verbatim. Mutation lifecycle like `status_bar`.
- (Optional tail) Bulk multi-select + confirm token `"{action} {count}"` + per-device results table.

**Risks:** no modal primitive — confirm must be inline; nested-component re-render trap (lift state);
two confirm contracts (single = device **name**, case-insensitive; bulk = `"{action} {count}"`,
case-**sensitive**); destructive params override server `DefaultBody` (prefer sending none); bulk
multi-select is genuinely new UI (descope to follow-up if needed); explicit `list_height` for scroll.

**Smoke:** Devices → Managed Devices → select device → inventory renders; **Sync** → "Syncing…" →
204 → a `Device action: syncDevice` row appears in the Audit Timeline; **Wipe** → inline confirm
panel, Confirm disabled until exact name typed; with destructive disabled, firing → 403 in error_box
(no wipe). `curl .../managed-devices/actions` matches the client's button list.

---

## 6. `policy-comparison` — Policy Comparison  (M)

**Status:** stub (`features.rs:225`). **Effort: M** — engine + baseline catalog + tenant-settings
fetch all exist, but it needs a **new** endpoint + **new** DTOs through all 4 layers + a two-picker
master/detail UI.

**Already exists:**
- `BaselineService.CompareSettingsCatalog(baseline, tenantSettings, policyId, name)` (line 84) →
  `BaselineComparisonResult` {Matching/Missing/Drifted/Extra}.
- `GetBaselinesByType(SettingsCatalog)` (embedded OIB/CIS catalog); `SettingsCatalogService`
  `GetPolicySettingsAsync(id)` (the exact 2nd arg) — exposed at
  `DevicesEndpoints.cs:170`; `/baselines` already projects the baseline picker (`BulkEndpoints.cs:99`).

**Missing:** no endpoint wires them; no DTO for `BaselineComparisonResult`.

**Layers:**
- **Contract:** a compare path (e.g. `POST /baselines/{baselineId}/compare/{policyId}`) + a
  `BaselineComparison` / `BaselineSettingComparison` schema (model after `/baselines` + `DriftRecord`).
- **Sidecar:** new handler (extend `BulkEndpoints.cs` or a `CompareEndpoints.cs`): resolve baseline
  by **FileName** from `GetBaselinesByType(SettingsCatalog)`, fetch tenant settings, call
  `CompareSettingsCatalog`, project to DTO; 409 signed-out.
- **DTOs:** `BaselineComparison` + `BaselineSettingComparison` in both sides;
  `api_client.compare_baseline(baseline_id, policy_id)` (mirror `drift()`).
- **Client:** `Screen::Compare` + a `compare_workspace` (copy `drift_workspace` `main.rs:521`) — but
  **two** pickers (baseline filtered to SettingsCatalog + tenant policy); right pane groups
  Matching/Missing/Drifted/Extra reusing `drift_row` (`main.rs:705`).

**Risks:** **wrong-engine trap** — `DriftDetectionService.CompareAsync` compares two on-disk backup
dirs, **not** baseline-vs-tenant; use `BaselineService.CompareSettingsCatalog`. Only SettingsCatalog
baselines are comparable (`BaselineService.cs:83` TODO) — filter ES/Compliance out of the picker.
Resolve baselines by **FileName** not Name (`/baselines` projects `id=FileName`). Group/collection
settings may show false drift (raw-JSON fallback).

**Smoke:** Drift & Compare → Policy Comparison → pick a SettingsCatalog baseline + a tenant policy →
right pane shows counts + per-setting rows; cross-check the compare route JSON against the schema;
signed-out → 409.

---

## 7. `bulk-assign` — Bulk App Assignment  (M)

**Status:** stub (`features.rs:194`). **Effort: M** — backend is a thin loop over an existing per-app
write, but the client needs a new multi-select picker + the intent-aware editor in a new workspace,
plus contract/DTO additions.

**Already exists:** `ApplicationService.AssignApplicationAsync(appId, List<MobileAppAssignment>)`
(the exact per-app write to loop); `AppsEndpoints.cs:190` (single-app translation to copy into the
loop body); `Assignments.BuildTarget`/`ParseIntent`; `CacheInvalidation.OnWrite`.

**Layers:**
- **Contract:** `POST /apps/assign` + `BulkAssignRequest {appIds[], assignments[], dryRun?}` +
  `BulkAssignResult {results:[{appId, ok, error?}]}`; formalize the `Assignment` schema here too
  (shared contract debt with `app-assignments`).
- **Sidecar:** new handler in `BulkEndpoints.cs` — build typed assignments once, loop `appIds`
  calling `AssignApplicationAsync` in per-app `try/catch`, aggregate results, evict cache once, honor
  `dryRun`. Wire in `Program.cs`.
- **DTOs:** `BulkAssignRequest`/`BulkAssignResult` both sides (reuse `Assignment`/`AssignmentDto`).
  `api_client.bulk_assign(...)`.
- **Client:** `bulk_assign_workspace` in `bulk.rs` (copy `export_workspace` shell) — multi-select app
  picker + the `assignment_editor` builder (`with_intent=true`) + per-app results panel.

**Risks:** writes hit live tenant, **replace-all** per app → **default to `dryRun`** (mirror
`/import`); partial failure → per-app try/catch, never abort the batch; Graph 429 throttling on many
sequential POSTs (sequential + backoff); contract debt (formalize `Assignment`); Reactor state in
parent.

**Smoke:** Apps → Bulk App Assignment → multi-select 2 apps → add `+Available` group → **dry-run**
first (result panel lists "would assign", no tenant change) → live apply on ONE throwaway app →
`GET /apps/{id}/assignments` confirms the write; signed-out → 409.

---

## 8. `detection-remediation` — Detection & Remediation  (M)

**Status:** partial (`features.rs:231`). **Effort: M** — Core engine is 100% built; this is
contract+sidecar projection + a bespoke 3-pane client screen.

**Already exists:**
- `DeviceHealthScriptService.GetRunSummaryAsync` (line 85) — run-summary counts;
  `GetDeviceRunStatesAsync` (line 91) — per-device detection/remediation state (`$expand=managedDevice`);
  `InitiateOnDemandRemediationAsync` (line 122) — the "run now".
- `/remediation-scripts` list/get/create/patch/delete already LIVE (`DeviceScriptsEndpoints.cs:22-58`)
  — the deploy/CRUD half is built.

**Missing:** no DTO/mapper for `RunSummary` / `DeviceRunState`; no endpoints expose the three methods;
remediation-scripts has no assignment **write** in Core (only read) — net-new if group-targeting is
in scope.

**Layers:**
- **Contract:** add `GET /remediation-scripts/{id}/run-summary` (→ tile array),
  `GET /remediation-scripts/{id}/device-states` (→ new `DeviceRunState[]`),
  `POST /remediation-scripts/{id}/run/{deviceId}` (→ 204, mirror the M14 action gating block).
- **Sidecar:** extend `DeviceScriptsEndpoints.cs` (already names these Graph types) — project
  run-summary → `ListItemDto` tiles, device-states → `DeviceRunStateDto[]`, run → on-demand call
  behind the **same env kill-switch** as device actions. Go direct to Graph (volatile; no cache).
- **DTOs:** `DeviceRunStateDto` both sides (run-summary reuses `ListItem`).
- **Client:** new `Screen::DetectionRemediation` + `screen_detection.rs` — script list (left) +
  run-summary tiles + device-state table + "Run now" device picker (copy `list_workspace` skeleton +
  `screen_tiles` for tiles).

**Risks:** large fleets → cap/paginate device-states (`MaxRows=500` like assignment-explorer);
on-demand remediation is an **irreversible device action** → gate like M14 destructive
(confirm + env switch), not a bare button; volatile reports must bypass the cache; generic
`Screen::List` can't host the multi-panel layout (needs a bespoke screen); **nav overlap** —
reconcile with the existing `remediation-scripts` row (this screen = reporting/run; deploy/CRUD
stays on the list).

**Smoke:** `curl .../remediation-scripts/{id}/run-summary` (tile counts) + `.../device-states`
(per-device rows); `POST .../run/{deviceId}` → 204 + on-demand run appears in Intune; in-app →
Detection & Remediation → select script → tiles + table populate, "Run now" gated/confirmed. (Needs a
tenant with a deployed proactive-remediation script.)

---

## Per-screen effort recap

| Screen | Contract | Sidecar | DTOs | Client | Net Core |
|---|---|---|---|---|---|
| `ca-pptx` | 1 path | 1 handler | none | small screen + 1 api method | none |
| `role-assignments` | 2 read paths | 2 handlers | none | `features.rs` flip | 1 GET-by-id |
| `app-assignments` | formalize Assignment | none | none | small screen | none |
| `cache-dev` | 3 paths + 2 schemas | new module | 2 DTOs | new screen + 3 api | none (hoist warm) |
| `managed-devices` | none | none | none | **new screen + 3 api + confirm UI** | none |
| `policy-comparison` | 1 path + 2 schemas | 1 handler | 2 DTOs | two-picker screen | none |
| `bulk-assign` | 1 path + schemas | 1 handler | 2 DTOs | multi-select screen | none |
| `detection-remediation` | 3 paths + 1 schema | 3 handlers | 1 DTO | 3-pane screen | none |

After these 8, the only remaining "not built out" work is **typed quick-edit forms** (progressive
enhancement over the JSON editor, M15-era) — not stubbed screens.
