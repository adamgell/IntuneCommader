# M14 — Hands: live device & remote action

> You can edit every policy in the tenant but can't touch a single device. M14 adds the remote-action verb surface (sync/restart/wipe/retire/…), a device detail pane, and confirm-gated bulk — the action *is* the record.

---

## Why now / what it builds on

The management spine (M2–M13) is config-CRUD: every assignable surface round-trips through
`service/Api/Endpoints/*Endpoints.cs` with full create/update/delete. **Devices are the
exception.** `IManagedDeviceService` (`service/Core/Services/IManagedDeviceService.cs`) exposes
exactly one method:

```csharp
Task<List<ManagedDevice>> ListManagedDevicesAsync(CancellationToken cancellationToken = default);
```

`DeviceService.cs` adds search + single-device GET (`SearchDevicesAsync`, `GetDeviceAsync`,
`ListAllDevicesAsync`) but those are also read-only — `DeviceService.DeviceSelect` pulls only
nine fields (`id, deviceName, operatingSystem, osVersion, lastSyncDateTime, managementState,
model, manufacturer, complianceState`). The `managed-devices` registry row is wired read-only:
`list("managed-devices", "Managed Devices", Devices, "/managed-devices", false)` (`features.rs`).
So the operator sees devices and can't act on one. Every Graph `managedDevice` action verb
(`syncDevice`, `wipe`, `rebootNow`, …) is unbound.

M14 closes that. It also folds in `IMacCustomAttributeService` — which is already **full-CRUD**
(`ListMacCustomAttributesAsync` / `Get` / `Create` / `Update` / `Delete`) but has **no registry
row** in `features.rs`, so a finished Core surface is invisible. One-line fix.

This is **Pattern F** (new): POST a Graph action verb against a device-set, gated by the M6
confirm rail, recorded as an `AuditEventRecord`. There is no config diff — the action is its own
record. Destructive verbs get extra rails (typed-confirm, scope-check, org allowlist, bulk caps).

---

## Core changes

New methods on **`IManagedDeviceService`** (all **net-new Core**; wrap the Graph beta
`managedDevices/{id}/microsoft.graph.{verb}` action endpoints via `GraphServiceClient`, mirroring
how `DeviceService` already calls `_graphClient.DeviceManagement.ManagedDevices[id]`):

| New Core method | Graph beta action verb | Reversible? | Destructive? |
|---|---|---|---|
| `SyncDeviceAsync(id)` | `syncDevice` | n/a (idempotent) | no |
| `RebootNowAsync(id)` | `rebootNow` | n/a | no |
| `RemoteLockAsync(id)` | `remoteLock` | yes (user unlocks) | no |
| `LocateDeviceAsync(id)` | `locateDevice` | n/a | no |
| `RenameDeviceAsync(id, name)` | `setDeviceName` | yes (rename back) | no |
| `RotateBitLockerKeysAsync(id)` | `rotateBitLockerKeys` | n/a | no |
| `RotateFileVaultKeyAsync(id)` | `rotateFileVaultKey` | n/a | no |
| `CollectDiagnosticsAsync(id)` | `createDeviceLogCollectionRequest` | n/a | no |
| `DefenderScanAsync(id, quick)` | `windowsDefenderScan` | n/a | no |
| `FreshStartAsync(id, keepUserData)` | `cleanWindowsDevice` | **no** | **yes** |
| `AutopilotResetAsync(id)` | `wipe` (`keepEnrollmentData=true`, `keepUserData=false`) | **no** | **yes** |
| `RetireAsync(id)` | `retire` | **no** | **yes** |
| `WipeAsync(id, keepEnrollmentData, keepUserData)` | `wipe` | **no** | **yes** |

Plus two read methods feeding the detail pane / history:

| New Core method | Source | Notes |
|---|---|---|
| `GetDeviceDetailAsync(id)` | expand `detectedApps`, `hardwareInformation`, `deviceActionResults` on `managedDevices/{id}` | richer `$select` than `DeviceService.DeviceSelect`; one Graph GET |
| `GetDeviceActionResultsAsync(id)` | `managedDevices/{id}` → `deviceActionResults[]` | maps Graph `deviceActionResult` (actionName/state/startDateTime) → action-history rows |

`collectDiagnostics` results are surfaced to the **M9/M10 diagnostics workspaces** (`diag_*`):
the log-collection bundle URL returned by `createDeviceLogCollectionRequest` is handed to the
existing `collector` / Log Explorer screens rather than parsed in Core.

**MacCustomAttribute fold-in** (no Core change — already complete):
add `list("mac-custom-attributes", "Custom Attributes (macOS)", Devices, "/mac-custom-attributes", true)`
to `features.rs` and a standard CRUD block in a new `MacCustomAttributeEndpoints.MapMacCustomAttributes()`
(copy the `shell-scripts` block — same `DeviceCustomAttributeShellScript` Graph type family,
projected through `ListItemDto` + `CrudJson`).

---

## Contract additions

`contract/openapi.yaml` first, then mirror into `crates/api-types/src/lib.rs` + `Contracts.cs`.

| Verb | Path | Body | Returns |
|---|---|---|---|
| `GET` | `/managed-devices/{id}/detail` | — | `DeviceDetail` |
| `GET` | `/managed-devices/{id}/actions` | — | `[DeviceActionRecord]` (history) |
| `POST` | `/managed-devices/{id}/actions/{action}` | `DeviceActionRequest` | `DeviceActionResult` |
| `POST` | `/managed-devices/actions/{action}` | `BulkDeviceActionRequest` | `BulkActionResult` |
| `GET` | `/mac-custom-attributes` (+ `/{id}`, POST/PATCH/DELETE) | CRUD JSON | `ListItem` / object |

`{action}` is a closed enum mirroring the Core methods: `sync | reboot | remoteLock | locate |
rename | rotateBitLocker | rotateFileVault | collectDiagnostics | defenderScan | freshStart |
autopilotReset | retire | wipe`. Single-device and bulk share the verb space; the bulk route
fans the same Core call across `deviceIds[]`. New camelCase DTOs (mirror the
`AuditEvent`/`ListItem` conventions in `api-types`): `DeviceDetail`, `DeviceActionRequest`,
`DeviceActionResult`, `DeviceActionRecord`, `BulkDeviceActionRequest`, `BulkActionResult`.

Each `POST` handler follows the `DevicesEndpoints` shape: `var g = auth.Graph; if (g is null)
return Results.Conflict();`, run the gate, call `new ManagedDeviceService(g).{Verb}Async(...)`,
then `store.AppendAuditEventAsync(...)`. No `CacheInvalidation.OnWrite` — actions don't mutate a
cached config blob (sync/reboot change device *state*, not a stored object); a follow-up
`/detail` GET re-reads live.

---

## Sample data

**Single device-action request** — `POST /managed-devices/2f8b9c10-.../actions/sync`:

```json
{
  "deviceId": "2f8b9c10-4d3e-4a71-9b2c-7e1f0a6d5c44",
  "reason": "stale check-in, forcing policy refresh"
}
```

**Bulk action request** — `POST /managed-devices/actions/sync`:

```json
{
  "action": "sync",
  "deviceIds": [
    "2f8b9c10-4d3e-4a71-9b2c-7e1f0a6d5c44",
    "a17d6e22-9f4b-4c08-8e3a-3b9c1d2e4f56",
    "c93f1a55-2b6d-47e9-a1c4-8d0e5f7a9b21"
  ],
  "reason": "monthly compliance sweep — zzz-cmpx-test ring"
}
```

**Device-detail response** — `GET /managed-devices/2f8b9c10-.../detail`:

```json
{
  "id": "2f8b9c10-4d3e-4a71-9b2c-7e1f0a6d5c44",
  "deviceName": "zzz-cmpx-test-01",
  "operatingSystem": "Windows",
  "osVersion": "10.0.26100.4061",
  "complianceState": "compliant",
  "managementState": "managed",
  "ownership": "company",
  "enrolledDateTime": "2025-11-03T14:22:08Z",
  "lastSyncDateTime": "2026-06-24T07:41:55Z",
  "hardware": {
    "manufacturer": "Microsoft Corporation",
    "model": "Surface Pro 11",
    "serialNumber": "0F4A21X-9931",
    "totalStorageBytes": 511220809728,
    "freeStorageBytes": 308912123904,
    "physicalMemoryBytes": 17179869184,
    "wifiMac": "8C-AE-4C-1D-77-90"
  },
  "installedApps": [
    { "id": "f01a...d2", "displayName": "Microsoft 365 Apps", "version": "16.0.18526.20168" },
    { "id": "9b22...4e", "displayName": "Company Portal", "version": "5.0.6296.0" }
  ],
  "actionHistory": [
    {
      "id": "act-7c1e",
      "action": "sync",
      "state": "done",
      "requestedUtc": "2026-06-21T09:12:00Z",
      "completedUtc": "2026-06-21T09:12:44Z",
      "actor": "acgell995@gmail.com"
    }
  ]
}
```

**AuditEvent emitted** (mirrors `AuditEventRecord` in `SnapshotStore.cs` → `AuditEvent` DTO;
`objectType = "managedDevice"`, `action = "device.sync"`, `objectId` = the device id):

```json
{
  "id": "8d2a1f0e-6c34-4b9d-a7e2-1f5b3c8e9a01",
  "timestamp": "2026-06-24T07:42:10Z",
  "actor": "acgell995@gmail.com",
  "action": "device.sync",
  "objectType": "managedDevice",
  "objectId": "2f8b9c10-4d3e-4a71-9b2c-7e1f0a6d5c44",
  "objectName": "zzz-cmpx-test-01"
}
```

**Destructive typed-confirmation request** — `POST /managed-devices/2f8b9c10-.../actions/wipe`.
The `confirmText` must equal the live `deviceName`; the server re-checks it, not just the client:

```json
{
  "deviceId": "2f8b9c10-4d3e-4a71-9b2c-7e1f0a6d5c44",
  "confirmText": "zzz-cmpx-test-01",
  "keepEnrollmentData": false,
  "keepUserData": false,
  "reason": "device returned by departing employee"
}
```

---

## Safety / gating

Five rails, layered. A reversible action passes through rail 1 only; a destructive single
action hits 1–4; bulk destructive hits all five.

1. **Confirm gate (all actions).** Reuse the M6 safe-write rail (`api_client.rs` §"M6 safe-write
   rails", `config_view.rs` confirm panel). The action surfaces a confirm panel summarizing
   `(verb, device, reason)`; only "Confirm" issues the POST. No diff body — the summary *is* the
   preview, since there's no config delta.
2. **Typed-confirmation (destructive only: `wipe`/`retire`/`freshStart`/`autopilotReset`).**
   Request carries `confirmText`; the endpoint loads the device via
   `ManagedDeviceService.GetDeviceDetailAsync(id)` and rejects with `400` unless
   `confirmText == deviceName` (case-sensitive). Client disables Confirm until the operator
   re-types the name. Server-side check is authoritative.
3. **Scope-check via PermissionCheckService.** Before any write, reuse
   `auth.Credential`/`auth.Scopes` → `new PermissionCheckService(credential, scopes)
   .CheckPermissionsAsync(ct)` (the exact pattern in `PermissionCheckEndpoints.cs`) and require
   the device-action scope (`DeviceManagementManagedDevices.PrivilegedOperations.All` for
   wipe/retire; `…ManagedDevices.ReadWrite.All` otherwise). Missing scope → `403`, surfaced as a
   Permission Check gap, not a Graph 403 stacktrace.
4. **Org allowlist (destructive only).** A tenant-admin setting (`%LocalAppData%\cmProjectX`
   config) gates which destructive verbs are enabled at all. Default: `wipe`/`retire` **off**.
   A disabled verb returns `403` with `{ "error": "wipe disabled by org policy" }` and never
   reaches Graph. This is the kill-switch independent of token scope.
5. **Bulk caps (bulk only).** `POST /managed-devices/actions/{action}` enforces:
   reversible bulk capped at **200** ids (matches `DeviceService` `Top = 200` paging);
   **destructive bulk is opt-in** — off unless the org allowlist explicitly enables
   `bulkDestructive`, and then hard-capped at **25** ids with typed-confirm of a sweep phrase
   (not per-device names). Over cap → `400` before any Graph call. `BulkActionResult` reports
   per-device `{ deviceId, state, error? }` so partial failures are visible; each *successful*
   device emits its own `AuditEventRecord` (the append-only log stays one-row-per-action).

---

## Client surface

**`features.rs` rows:**
- `managed-devices` flips writable: `list("managed-devices", "Managed Devices", Devices, "/managed-devices", true)` — but actions don't fit the generic List edit/delete affordance, so dispatch a dedicated screen instead (below).
- new `list("mac-custom-attributes", "Custom Attributes (macOS)", Devices, "/mac-custom-attributes", true)` (generic CRUD List — no special screen).

**New `Screen::Devices` variant** (alongside `Groups`, `Ca` in the `Screen` enum): a master/detail
device workspace. Left = the existing `/managed-devices` list (search via `DeviceService.SearchDevicesAsync`).
Right = the **device detail pane** rendering `DeviceDetail`: hardware table, compliance/management
badges, installed-apps list, and the **action history** table (`actionHistory[]`). Above the
history, an **action bar**: reversible verbs as plain buttons (Sync/Reboot/Locate/Rotate keys);
destructive verbs (Wipe/Retire/Fresh Start/Autopilot Reset) behind the typed-confirm panel,
disabled entirely if the org allowlist has them off.

**`api_client.rs` methods** (thin blocking `reqwest`, alongside `get_detail`/`delete_item`):
- `get_device_detail(id) -> DeviceDetail`
- `get_device_actions(id) -> Vec<DeviceActionRecord>`
- `post_device_action(id, action, body) -> DeviceActionResult` (reuses the `post_text` plumbing)
- `post_bulk_device_action(action, body) -> BulkActionResult`

Bulk is reached from the device list's multi-select (checkbox column on the master list) → action
bar → confirm/typed-confirm panel → `post_bulk_device_action`. Action history refreshes by
re-calling `get_device_actions` after a successful POST.

---

## Definition of done

- A reversible action round-trips E2E against a **sandbox device** (`zzz-cmpx-test-01`):
  `post_device_action(id, "sync", …)` → Graph `syncDevice` → `200` → an `AuditEventRecord`
  with `action="device.sync"` lands in `audit_events` → `/managed-devices/{id}/actions` shows
  the new row.
- `GET /managed-devices/{id}/detail` returns hardware + compliance + installed apps + action
  history for one Graph GET; the detail pane renders all four.
- **Destructive single action requires typed confirmation:** `wipe` with absent or wrong
  `confirmText` returns `400` and never calls Graph; correct `confirmText` proceeds.
- **Org allowlist enforced server-side:** with `wipe` disabled, the endpoint returns `403`
  regardless of token scope; the client hides/greys the Wipe button.
- **Scope-check enforced:** a token missing the privileged-operations scope returns `403` framed
  as a Permission Check gap (not a raw Graph error).
- **Bulk destructive is opt-in and capped:** reversible bulk rejects >200 ids; destructive bulk
  is off by default, and when enabled rejects >25 ids and requires sweep-phrase typed-confirm.
- `mac-custom-attributes` appears under Devices and round-trips full CRUD (create → list → edit →
  delete) through the generic List screen.

---

## Open questions

- **Action verb naming in audit.** `device.sync` vs `managedDevice.syncDevice`? Pick one
  convention now — it's the search key in the time-machine and can't be retro-renamed (append-only).
- **`collectDiagnostics` handoff.** Does the bundle URL flow through the sidecar (download + cache
  under `%LocalAppData%`) or does the client fetch it directly? Affects whether M9/M10 parse a
  local path or a SAS URL.
- **`locateDevice` is async two-step** (POST triggers, result polled separately). Does the detail
  pane poll for the lat/long, or is locate fire-and-forget with the result only in action history?
- **Bulk audit volume.** One `AuditEventRecord` per device per bulk action could write thousands
  of rows for a large sweep. Keep one-row-per-device (greppable, honest) or add a bulk-batch
  parent record? Leaning one-per-device to preserve the append-only "action is the record" model.
- **Org allowlist storage.** Reuse the tenant-profile config (`AuthSession` profile lifecycle) or
  a separate signed policy file? The kill-switch shouldn't be editable by the same flow that
  performs the wipe.
