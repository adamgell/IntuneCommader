# M15 — Source: Policy-as-Code / GitOps

> The time-machine is already a content-hashed, append-only version history of every
> object. Stop treating it as an audit log and start treating it as a working tree:
> expose it as Git, and tenant config becomes a versioned, PR-reviewed, CI-validatable
> artifact — infrastructure-as-code for Intune, built ~80% from engines we already ship.

---

## Why this is the keystone

Every prior milestone has been edging toward this without naming it. M5 reads the tenant.
M6 gates writes behind a visible diff and snapshots them. The store
(`service/Store/SnapshotStore.cs`) already dedups snapshots by SHA-256 of the body
(`AppendSnapshotIfChangedAsync` → `Sha256(snapshot.BodyJson)`, skip-if-`prevHash == hash`)
and full-text-indexes them in Lucene. That is, structurally, what `git` does: a
content-addressed object store with an append-only history and dedup. M15 is the reframe
that says so out loud and wires the last 20%.

Three engines we *already own* make this wiring, not building:

1. **`ExportService` (`service/Core/Services/ExportService.cs`, `IExportService.cs`)** —
   serializes the live tenant to a file tree, one file per object, across ~35 surfaces
   (`ExportDeviceConfigurationAsync`, `ExportCompliancePolicyAsync`,
   `ExportSettingsCatalogPolicyAsync`, … `ExportConditionalAccessPolicyWithResolvedGuidsAsync`).
   Each export call also records a `MigrationEntry` into a `MigrationTable`. This is `pull`.

2. **`ImportService` (`service/Core/Services/ImportService.cs`, `IImportService.cs`)** —
   reads that file tree back (`Read*FromFolderAsync`) and applies desired state
   (`Import<T>Async(export, migrationTable, ct)`), rewriting source-tenant IDs to
   destination IDs through the same `MigrationTable` (`migrationTable.AddOrUpdate(new
   MigrationEntry { OriginalId = …, NewId = created.Id })`, ImportService.cs ~L213). This
   is `push`.

3. **`JsonDrift.Diff` (`service/Api/JsonDrift.cs`)** — the *exact same* structural differ
   that powers `GET /drift`, `GET /objects`, and the M6 `POST /preview-diff` gate
   (`Program.cs` L205/L222/L239). It walks two JSON bodies and emits `Added` / `Removed` /
   `Modified` `DriftChangeDto`s with JSON-Pointer paths. Run it between a repo file and the
   live tenant body and you get a Terraform-style change set **before any write**. This is
   `plan`.

The fourth ingredient is `ExportNormalizer` (`service/Core/Services/ExportNormalizer.cs`).
It strips volatile server-assigned fields (`id`, `createdDateTime`, `lastModifiedDateTime`,
`version`, plus Kiota `backingStore` / `additionalData`), sorts object keys (`OrderBy(p =>
p.Key, Ordinal)`) and sorts arrays by serialized value. That determinism is what makes
content-hashing meaningful and what makes `git diff` show *real* drift instead of
serialization jitter. Snapshots in the store are stored already-normalized (see the comment
atop `JsonDrift.cs`: "Snapshots are stored already-normalized … so this is a structural
compare"), so the repo tree and the snapshot bodies are the same shape — the local mirror is
free.

Net: `pull` = `ExportService`, `push` = `ImportService`, `plan` = `JsonDrift.Diff`, history
= `SnapshotStore`. M15 is the glue, the on-disk layout, and the Git remote.

---

## The repo model

A pulled tenant is a directory tree: one folder per surface (matching the subfolder names
`ExportService` already writes — `DeviceConfigurations`, `CompliancePolicies`, etc.), one
JSON file per object, named by a slug of the GUID-resolved `displayName` (from
`GetUniqueFilePath(folderPath, config.DisplayName ?? config.Id, config.Id)` in
ExportService.cs). A `migration-table.json` at the root maps original-tenant IDs to names —
the only place raw GUIDs are allowed to leak. A `manifest.json` records the pull provenance.

```
tenant-contoso/
├── manifest.json
├── migration-table.json
├── DeviceConfigurations/
│   ├── windows-baseline-bitlocker.json
│   └── ios-wifi-corp.json
├── CompliancePolicies/
│   ├── windows-min-compliance.json
│   └── macos-firewall-required.json
├── SettingsCatalog/
│   └── defender-asr-rules.json
├── Applications/
│   └── company-portal-win32.json
├── ConditionalAccess/
│   └── require-mfa-all-users.json   # GUIDs resolved to names via WithResolvedGuids
└── Scripts/
    └── remediation-disk-cleanup.json
```

A normalized exported object (`DeviceConfigurations/windows-baseline-bitlocker.json`). Note
the *absence* of `id` / `version` / `createdDateTime` — `ExportNormalizer` stripped them;
keys are sorted; assignments target a **group name**, not a GUID:

```json
{
  "@odata.type": "#microsoft.graph.windows10EndpointProtectionConfiguration",
  "assignments": [
    { "target": { "groupName": "All Corp Windows Devices" } }
  ],
  "bitLockerEnableStorageCardEncryptionOnMobile": true,
  "bitLockerEncryptDevice": true,
  "description": "Baseline disk encryption for managed Windows endpoints.",
  "displayName": "Windows Baseline - BitLocker",
  "firewallProfileDomain": { "firewallEnabled": "allowed" },
  "platform": "windows10AndLater"
}
```

`manifest.json` — provenance for the pull, so `plan` knows what it is diffing against and
`push` can warn on cross-tenant drift:

```json
{
  "schemaVersion": 1,
  "tenantId": "11111111-2222-3333-4444-555555555555",
  "tenantDomain": "contoso.onmicrosoft.com",
  "pulledUtc": "2026-06-24T14:02:11Z",
  "pulledBy": "acgell995@gmail.com",
  "cmpxVersion": "0.15.0",
  "surfaces": {
    "DeviceConfigurations": 2,
    "CompliancePolicies": 2,
    "SettingsCatalog": 1,
    "Applications": 1,
    "ConditionalAccess": 1,
    "Scripts": 1
  },
  "objectCount": 8,
  "contentHash": "sha256:9f2c…a17b"
}
```

`contentHash` is the Merkle-ish roll-up of every file's normalized SHA-256 — the same hash
function `SnapshotStore.Sha256` uses — so a manifest equality check is a cheap "is this tree
identical to that snapshot generation" test.

---

## Commands / endpoints

`cmpx` is a thin CLI (it can live in the Rust client or a standalone bin) that calls the
sidecar; the sidecar does the Graph + diff work. Every verb is also a plain HTTP endpoint so
the WinUI app and MCP (M13) can drive the same flow.

| Verb | Endpoint | Body | Returns |
|---|---|---|---|
| `cmpx pull` | `POST /gitops/pull` | `{ "outputPath": "…", "surfaces": ["*"] }` | `manifest.json` + writes the tree to disk; per-surface object counts |
| `cmpx plan` | `POST /gitops/plan` | `{ "repoPath": "…", "surfaces": ["*"] }` | `GitOpsPlan` — list of per-object `DriftChangeDto[]` (added/removed/modified), summary counts |
| `cmpx push` | `POST /gitops/apply` | `{ "repoPath": "…", "planId": "…", "confirm": true }` | `GitOpsApplyResult` — per-object `applied` / `skipped` / `conflict`, updated migration table |
| `cmpx status` | `GET /gitops/status?repoPath=…` | — | manifest vs live drift summary (is the tenant ahead of / behind `main`?) |

`plan` is read-only and unauthenticated-write-wise safe: it fetches live bodies through the
existing M12.1 cached reader, normalizes the repo files through `ExportNormalizer.NormalizeJson`
(so both sides are apples-to-apples), and feeds each `(liveBody, repoBody)` pair to
`JsonDrift.Diff`. Objects present in the repo but not the tenant → whole-object `Added`;
present in the tenant but not the repo → `Removed`; both → field-level `Modified` list.

`apply` requires `confirm: true` and a `planId` that pins the plan it was reviewed against —
if the live tenant moved since `plan` (re-diff hash mismatch), apply rejects with `409
Conflict` rather than blindly overwriting. This is the M6 safe-write rail, lifted to
tree scope.

---

## Sample data

**`POST /gitops/plan` response** — a Terraform-style change set. Each entry reuses the
contract's `DriftChange` shape verbatim (`crates/api-types/src/lib.rs` L110-122:
`{ path, kind, before, after }`, `kind ∈ Added|Removed|Modified`), grouped per object:

```json
{
  "planId": "p_3f9a1c",
  "repoPath": "C:\\src\\tenant-contoso",
  "generatedUtc": "2026-06-24T14:30:02Z",
  "summary": { "add": 1, "change": 1, "destroy": 1, "noop": 5 },
  "objects": [
    {
      "objectType": "DeviceConfiguration",
      "objectName": "Windows Baseline - BitLocker",
      "verdict": "Modified",
      "changes": [
        {
          "path": "/firewallProfileDomain/firewallEnabled",
          "kind": "Modified",
          "before": "notConfigured",
          "after": "allowed"
        },
        {
          "path": "/assignments/0/target/groupName",
          "kind": "Modified",
          "before": "Pilot Windows Devices",
          "after": "All Corp Windows Devices"
        }
      ]
    },
    {
      "objectType": "CompliancePolicy",
      "objectName": "macOS Firewall Required",
      "verdict": "Added",
      "changes": [ { "path": "/", "kind": "Added", "before": null, "after": { "displayName": "macOS Firewall Required" } } ]
    },
    {
      "objectType": "DeviceConfiguration",
      "objectName": "Legacy WiFi Profile",
      "verdict": "Removed",
      "changes": [ { "path": "/", "kind": "Removed", "before": { "displayName": "Legacy WiFi Profile" }, "after": null } ]
    }
  ]
}
```

(`before`/`after` here are exactly what `JsonDrift.Diff` emits: for whole-object add/remove
the path is `"/"` via `PathOrRoot`, with the body on the populated side.)

**`POST /gitops/apply` request + result.** The request pins the reviewed `planId`; the
result reports per-object outcome and returns the freshly merged `migration-table.json`:

```json
// request
{ "repoPath": "C:\\src\\tenant-contoso", "planId": "p_3f9a1c", "confirm": true }
```

```json
// result
{
  "planId": "p_3f9a1c",
  "appliedUtc": "2026-06-24T14:31:40Z",
  "objects": [
    { "objectName": "Windows Baseline - BitLocker", "outcome": "applied", "snapshotId": "a1b2c3d4", "newId": "8f00…01" },
    { "objectName": "macOS Firewall Required",      "outcome": "applied", "snapshotId": "e5f6a7b8", "newId": "8f00…02" },
    { "objectName": "Legacy WiFi Profile",          "outcome": "skipped",  "reason": "destroy not enabled (--allow-destroy)" },
    { "objectName": "iOS WiFi Corp",                "outcome": "conflict", "reason": "live body changed since plan (hash mismatch)" }
  ],
  "summary": { "applied": 2, "skipped": 1, "conflict": 1 }
}
```

Every `applied` object carries a `snapshotId` because `ImportService` writes go through the
M6 write pipeline, which calls `POST /snapshots` → `AppendSnapshotIfChangedAsync`. History is
free; the apply *is* a snapshot generation.

**`migration-table.json` snippet** — cross-env ID remap, the `MigrationTable` /
`MigrationEntry` model (`service/Core/Models/`). `originalId` is the source tenant's GUID;
`newId` is filled in by `ImportService` on create (`NewId = created.Id`). On a fresh
destination tenant `newId` starts null and is populated by the first `push`:

```json
{
  "entries": [
    {
      "objectType": "DeviceConfiguration",
      "originalId": "7a11…src",
      "newId": "8f00…01",
      "name": "Windows Baseline - BitLocker",
      "exportedAt": "2026-06-24T14:02:11Z"
    },
    {
      "objectType": "CompliancePolicy",
      "originalId": "9c22…src",
      "newId": null,
      "name": "macOS Firewall Required",
      "exportedAt": "2026-06-24T14:02:11Z"
    }
  ]
}
```

---

## Pattern H — declarative loop

GitOps is **Pattern H**: `plan(JsonDrift) → gate → apply(ImportService) → snapshot → verify-converged`.
It is the M6 single-object safe-write rail (`preview-diff → confirm → write → snapshot`) lifted
to *tree* scope — N objects instead of one — reusing the same three primitives:

```
   repo tree (desired)              live tenant (actual)
        │                                  │
        └────────► JsonDrift.Diff ◄────────┘        1. PLAN  (read-only)
                        │
                   GitOpsPlan (per-object DriftChange[])
                        │
                   ┌────▼─────┐
                   │   GATE   │  ← M6 confirm panel / M13 Pending-changes inbox
                   └────┬─────┘     (operator approves the exact change set)
                        │ confirm:true + planId
                   ImportService.Import<T>Async                4. APPLY
                        │   (per object; re-check live hash → 409 on conflict)
                        ▼
                   POST /snapshots → AppendSnapshotIfChangedAsync   5. SNAPSHOT
                        │
                   re-PLAN against fresh pull → expect summary all-noop   6. VERIFY
```

- **Gate.** A `GitOpsPlan` is just a bag of `DriftChange`s, so it renders in the **existing
  M6 Review-changes diff panel** with zero new UI. In the M13 path the whole plan enqueues as
  one `PendingChange` (`{ verb: POST, path: /gitops/apply, bodyJson, diff }`,
  PLUGINS-MCP.md L93) and shows in the **Pending AI changes inbox** — `Approve` →
  `POST /pending-changes/{id}/approve` replays the apply through the same pipeline. An
  AI-proposed `push` is governed identically to a human one.
- **Verify-converged.** After apply, `cmpx pull` a fresh tree and `cmpx plan` again; a
  converged tenant returns `summary: { add:0, change:0, destroy:0 }`. Non-empty means a field
  is server-computed or read-only — feed it back into `ExportNormalizer.StrippedFields`.

---

## Git integration

The local mirror and a real remote are two layers; only the first is mandatory for M15.

- **Local mirror = the snapshot store.** The store is already content-addressed and
  append-only. `cmpx pull` writing a tree, then `git init && git commit`, makes that history
  navigable with standard tooling — `git blame DeviceConfigurations/windows-baseline-bitlocker.json`
  tells you who changed `firewallEnabled` and when, because every M6 write already
  snapshotted. We don't reimplement Git; we shell to it (or libgit2 via `git2` in the Rust
  client) over the pulled tree.
- **Real remote = PR-reviewed tenant changes.** Push the tree to GitHub/Azure Repos. A
  change to the tenant becomes a pull request; CI runs `cmpx plan --repo . --tenant $SANDBOX`
  and posts the change set as a PR comment; merge to `main` triggers a gated `cmpx push`.
  Full blame, review, and rollback (`git revert` → `cmpx push`) for free.
- **Secrets stay OUT.** Config is non-secret by construction — `ExportNormalizer` already
  drops volatile and transport fields, and assignment targets are resolved to **group
  names**, not GUIDs or member lists. Any genuine secret (a Win32 app's content, a cert
  payload) is stored as a **reference** in the repo (`{ "secretRef": "kv://contoso/…" }`),
  resolved at *apply* time from the operator's vault, never serialized into a committed file.
  The repo is safe to make a normal source repo; the secret never enters Git history.
- **CI-gated PR flow.** `plan` is the CI primitive: deterministic (normalized both sides),
  read-only, exits non-zero if the change set is non-empty against the target tenant. So
  "does this PR drift `main` from the tenant?" is a check, and "apply `main` to the tenant" is
  a gated deploy job — desired-state convergence with a human gate at the merge button.

---

## Definition of done

- **Byte-identical round-trip.** `cmpx pull` a sandbox object → `cmpx push` it to a throwaway
  tenant → `cmpx pull` again: the second pulled file is byte-identical to the first after
  `ExportNormalizer.NormalizeJson`. (IDs differ in the live tenant but are stripped; the
  migration table carries the remap.)
- **Zero-diff on fresh pull.** Immediately after a pull, `cmpx plan` against the same tenant
  returns `summary: { add:0, change:0, destroy:0, noop:N }`. A non-zero result is a
  normalizer bug (a volatile field leaked into the diff), tracked as a `StrippedFields` gap.
- **PR-gated apply converges.** A PR that edits one field, merged to `main`, drives a gated
  `cmpx push`; the post-apply `cmpx plan` is all-noop; the change is visible in the snapshot
  history and audit log with the committing user as actor.
- **Conflict safety.** If the live tenant changes between `plan` and `apply`, `apply` reports
  that object as `conflict` (hash mismatch) and applies nothing for it — never a blind
  overwrite.

---

## Open questions

- **Surface coverage parity.** `ExportService` covers ~35 surfaces but not 100% of what M5
  can read (e.g. enrollment-status pages, some Entra-side objects). Does `pull` export only
  the export-supported surfaces, or do we backfill `Export*`/`Import*` for the gaps as part of
  M15? Proposed: ship with the existing ~35, manifest declares `surfaces`, plan ignores
  surfaces not in the manifest.
- **Assignments-as-config.** Group *names* are stable across tenants but group *membership*
  is not. Do we treat assignment targets as desired state (and fail apply if the named group
  is missing in the destination), or as advisory? Leaning: fail loud — a missing target group
  is a `conflict`, not a silent skip.
- **Destroy semantics.** `JsonDrift` emits `Removed` for repo-absent objects, but auto-delete
  on the tenant is dangerous. Gate behind an explicit `--allow-destroy` / `confirmDestroy:
  true` flag (default off → `skipped`), as in the sample apply result.
- **Plan staleness window.** `planId` pins a hash of the live tenant at plan time; how long is
  a plan valid before apply forces a re-plan? Tie it to the M12.1 cache TTL so a stale read
  can't silently widen the window.
- **Multi-tenant migration table identity.** `MigrationEntry` keys on `(objectType,
  originalId)`. Promoting dev→prod through two hops needs a chain of tables — do we compose
  them, or make the repo tree itself the canonical identity (filename = stable key) and demote
  the migration table to a per-apply remap cache?
- **Big binaries.** Win32 app content, ADMX uploads — these don't belong in Git. Reference +
  vault, or Git-LFS? Reference-and-resolve keeps the repo diffable; LFS keeps it
  self-contained. Leaning reference.

---

## Implementation status (v1)

What shipped, and the deliberate deviations from the text above (sidecar:
`service/Api/Endpoints/GitOps.cs` + `GitOpsEndpoints.cs`; contract + DTO mirrors;
client `api_client::gitops_*` + a `features.rs` nav scaffold).

- **Surfaces.** Writable: `device-configs`, `compliance-policies`, `settings-catalog`.
  Read/plan-only: `conditional-access` (CA stays read-only — locked guarantee). The
  `GitOpsSurfaces` registry makes the remaining ~30 surfaces a one-row addition each.

- **Serialization & byte-identity.** The repo tree is written by `ExportService` and
  normalized by `ExportNormalizer` — i.e. **System.Text.Json runtime-type form**
  (`odataType`, integer enums), which is the *exact same* canonical shape
  `GraphDeltaSync` stores in the snapshot time-machine. So:
  - The **pull → apply → re-pull round-trip is byte-identical** (verified), and the
    tree matches the snapshot store, so `git blame` on a file lines up with snapshot
    history. *This* is the DoD's byte-identity.
  - It is intentionally **not** byte-identical to the live **JSON-editor** wire form,
    which is Kiota (`@odata.type`, string enums). The repo mirrors the *time-machine*,
    not the live-edit wire form. (Switching the repo to the Kiota form was considered
    and rejected — it would diverge the tree from the snapshot store.)

- **The apply-write model.**
  - `Added` → `ImportService.Import<T>Async` + `MigrationTable` (the doc's "push").
  - `Modified` → Core update with a **minimal patch** (only the changed top-level
    fields). `ImportService` is create-only (no Import-update), so updates can't route
    through it; and a full-object PATCH re-sends null-on-read / non-nullable fields
    Graph rejects (`supportsScopeTags`). Minimal-patch is the correct update seam.
    It covers scalar edits (the DoD's "edit one field"); enum/nested edits await the
    broader System.Text.Json ↔ Kiota reconciliation.
  - `Removed` → Core delete, gated behind `confirmDestroy` (default off → `skipped`).

- **GUID resolution.** Conditional Access is exported via
  `ExportConditionalAccessPolicyWithResolvedGuidsAsync` with a name map batched from
  `DirectoryObjectResolver` (users/groups/roles/apps) + named-location names, so the
  repo carries names, not raw GUIDs. **Assignments-as-config** (resolving assignment
  targets to group names, per Open question #2) is **not** yet implemented — exports
  are object-metadata only.

- **A coupled fix.** Polymorphic CRUD **create** (and PATCH) were dropping the
  `@odata.type` discriminator: a model deserialized through the Graph backing-store
  parse factory is "initialized, no changes", so `Post`/`PatchAsync`'s changed-only
  serialization proxy emitted an empty body → *"Cannot create an abstract class"*.
  `CrudJson.MakeWriteReady` resets the backing store (the inbound mirror of
  `CrudJson.ToJson`'s flip) so the full object is sent — fixing all CRUD writes and
  the GitOps apply path.

- **Verified live** (Ivy24 sandbox): pull (real configs), zero-diff fresh plan, pull
  determinism (byte-identical), and a full `create → plan → apply (minimal-patch) →
  verify → converge → re-pull` round-trip on a throwaway `zzz-cmpx-test-*` object,
  cleaned up.
