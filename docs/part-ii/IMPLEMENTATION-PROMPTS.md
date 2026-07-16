# Part II — local implementation prompts (M14–M22)

Copy-paste prompts for driving each Part-II milestone with a **local** Claude Code session
(on a Windows/.NET box with a sandbox tenant), where the full toolchain and a live tenant are
available. Each milestone is designed in its sibling doc (`docs/part-ii/M*.md`); these prompts
turn a design into a verified slice following the project's established patterns.

**How to use:** paste the **shared preamble** once at the top of a session, then the task block
for the milestone you want. Build/test/smoke steps are baked in.

**Dependency order:** `M15 → M16 → M17 → M18` is the spine (M18 needs M16's `/simulate` + the
M13 inbox). M16/M17 stand alone; M19/M20/M21 are independent; M22 is last and is a stretch.

---

## Shared preamble (prepend to every milestone prompt)

```
Context: cmProjectX — Rust+WinUI client ↔ .NET 10 sidecar (127.0.0.1:5099) over a uniform
REST contract; sidecar wraps a hard-forked Graph engine in service/Core/Services/.

Rules (non-negotiable):
- Read docs/part-ii/<MILESTONE>.md (the design) and AGENTS.md before touching code.
- contract/openapi.yaml is the source of truth — change it FIRST; DTOs mirror it in
  crates/api-types/src/lib.rs (Rust) AND service/Api/Contracts.cs (.NET), camelCase, byte-compatible.
- Wire existing Core engines; do NOT reimplement Graph in Rust. Net-new Core only where the doc says so.
- Adding a surface = one Surfaces.cs row + one features.rs row + one EndpointInventory.cs row, in lockstep.
- Locked guarantees hold: propose→human-approve→apply (no auto-apply); Conditional Access stays
  read-only; append-only store; simulate before automate.
- Develop on branch claude/roadmap-generation-hl6r2u. Don't push without telling me.

Verify before opening a DRAFT PR:
  dotnet build service/CmProjectX.slnx -c Release   &&   cargo build --workspace
  ./scripts/test.ps1 -Tier 1
  then a live smoke against a SANDBOX tenant on throwaway zzz-cmpx-test-* objects (clean up in finally).
Report build/test/smoke results; flag anything you couldn't verify.
```

---

## M15 — GitOps (the keystone; do this first)

```
Implement M15 (docs/part-ii/M15-gitops.md): Policy-as-Code over the existing engines.
- Core/sidecar: POST /gitops/pull (ExportService → normalized GUID-resolved file tree + manifest),
  POST /gitops/plan (JsonDrift.Diff between repo tree and live tenant → DriftChange[] change set),
  POST /gitops/apply (ImportService + MigrationTable, gated). Reuse ExportNormalizer for deterministic diffs.
- Pattern H: plan → confirm (M6 gate) → apply → snapshot-on-write → verify re-pull shows zero diff.
- DoD: pull→plan→push round-trips a sandbox object byte-identical to the JSON-editor path; plan against
  a freshly-pulled tree is empty. Start with a LOCAL repo dir (no Git remote yet); keep secrets out.
```

## M16 — Blast-radius simulator (gates M18)

```
Implement M16 (docs/part-ii/M16-simulator.md): simulate-before-write.
- Wire AssignmentCheckerService (GetUserAssignmentsAsync / GetGroupAssignmentsAsync /
  GetDeviceAssignmentsAsync / CompareGroupAssignmentsAsync) + GroupService.GetMemberCountsAsync.
- POST /simulate: body = a proposed (verb,path,body); return BlastRadius { affectedUsers, affectedDevices,
  sampleAffected[], conflicts[] (overlapping include/exclude), redundancies[], severity, summary }.
- Mirror the BlastRadius DTO in api-types + Contracts.cs. Have the M13 propose_* path attach a simulation
  to the pending-change diff. DoD: simulated affected counts match post-apply reality on a sandbox object.
```

## M17 — Tenant digital twin

```
Implement M17 (docs/part-ii/M17-twin.md): assemble a local graph from data already on disk.
- Project nodes/edges from ICacheService + SnapshotStore + GroupService (membership) + AssignmentChecker
  (assignment edges). Projection over the existing store first — no embedded graph engine.
- GET /twin/analytics/{query} for: orphaned-policies, conflicting-assignments, assignment-cycles,
  coverage-gaps, drift-hotspots; GET /twin/node/{id} (neighborhood). Must answer OFFLINE (cache/store only).
- DoD: orphaned-policies + conflicting-assignments return correctly; an injected orphan/conflict shows up.
```

## M18 — Autonomy loop (needs M16 + the M13 inbox)

```
Implement M18 (docs/part-ii/M18-autonomy.md): closed loop that TERMINATES at the human-gated inbox.
- Flesh out service/Sync/ into a scheduled background watcher (delta-sync watermarks in sync_state).
- watch → DriftDetectionService.CompareAsync → plan remediation → M16 simulate → enqueue a PendingChange
  (state=pending) into the EXISTING /pending-changes inbox WITH diff + blast-radius → operator approves →
  existing replay-apply → verify re-converged → audit.
- GET/PUT /autonomy/policy (which signals may auto-ENQUEUE), GET /autonomy/runs.
- LOCKED: nothing auto-applies — autonomy ends at the inbox. DoD: injected drift surfaces as a pending
  change carrying diff+blast-radius; approving re-converges; verify shows zero residual drift; fully audited.
```

## M19 — Continuous posture (extends an existing DTO)

```
Implement M19 (docs/part-ii/M19-posture.md): extend, don't reinvent.
- The SecurityPosture/ScoreCategory/SecurityGap/PostureStats DTOs and GET /security-posture/summary
  ALREADY exist (crates/api-types/src/lib.rs:206; SecurityPostureEndpoints.cs). Build on them.
- Add: benchmark mapping (CIS/OIB/Essential8/NIST control IDs per category, via BaselineService),
  GET /posture/trend (score over time from SnapshotStore), POST /posture/evidence-pack (generalize
  ConditionalAccessPptExportService + AssignmentReportExporter to all-surface evidence), GET /posture/poam.
- DoD: score + a point-in-time evidence pack regenerate after a change and show the delta.
```

## M20 — MSP fleet (biggest net-new lift)

```
Implement M20 (docs/part-ii/M20-fleet.md): first-class multi-tenant fan-out (Pattern G).
- Today AuthSession is single active tenant; ProfileService holds N profiles, cache/store key by {tenantId}.
- Add tenant groups, GET /fleet/list/{surface} (fan-out, rows tenant-tagged), POST /fleet/campaign
  (broadcast a baseline via Export→Import+MigrationTable per target, each a gated M13 replay), /fleet/drift,
  /fleet/posture. Golden-template inheritance: golden ⊕ groupOverride ⊕ tenantOverride.
- Lean: stored-profile fan-out first (no new consent); GDAP later. DoD: a campaign to ≥2 sandbox tenants
  reports per-tenant success/skip/conflict, each write gated.
```

## M21 — Ecosystem (mostly wiring M15 + M13.3)

```
Implement M21 (docs/part-ii/M21-ecosystem.md): make config + capability shareable.
- Packs: versioned M15 desired-state repos + a pack.json manifest; POST /packs/{id}/adopt → M15 plan→gate→apply.
- Playbooks: parameterized ordered propose_* sets; POST /playbooks/{id}/run → lands each step in the M13 inbox.
- Marketplace: a discovery layer over the existing McpPlugins.cs / plugins.json aggregation (curated allowlist).
- Everything flows through the existing approval inbox + audit + scope-gating. DoD: an externally-authored
  pack AND a downstream plugin each drive a gated write end-to-end.
```

## M22 — Cross-MDM (stretch / vision — confirm before building)

```
Read docs/part-ii/M22-cross-mdm.md. This is a STRETCH — do NOT build the full thing; first produce a
short feasibility spike: define the IMdmProvider seam (list/get/create/update/delete/assign projecting into
the shared ListItem/Assignment DTOs), show where it plugs into Program.cs DI keyed off the tenant profile,
and implement ONE read-only surface for a second provider (a stub/mock provider is fine) that renders in the
existing client unchanged via the uniform contract. Report whether the abstraction holds before going further.
```

---

## Reference: the M14 verification prompt (already implemented)

M14 (macOS custom attributes + device-action layer) is implemented on
`claude/roadmap-generation-hl6r2u` and CI-green, but not yet locally built or tenant-tested.
To verify it on a Windows/.NET box, see the smoke steps in `.smoke/m14_devices_smoke.ps1`
(if added) or run: list/create/get/patch/delete on `/mac-custom-attributes` against a
`zzz-cmpx-test-*` object, a reversible `POST /managed-devices/{id}/actions/syncDevice` (expect
204 + an audit event), and the safety rails (`wipe` with destructive opt-in off → 403; bogus
verb → 400). Do **not** send a matching destructive `confirm`.
```
