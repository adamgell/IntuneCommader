# M18 — Autonomy: the closed-loop AI SRE

> Wire the M13 HITL inbox + M16 blast-radius simulator + M17 twin + `DriftDetectionService` into a continuous, **human-gated** control loop: watch → detect → plan → simulate → propose → approve → apply → verify → audit. Rules and AI *propose and simulate*; a human approves the exact diff; nothing auto-applies.

This is the headline of Part II (`ROADMAP.md`: "M18 Autonomy … closed-loop watch→plan→simulate→gate→apply→verify — the AI SRE"). It does **not** invent a new write path. It feeds the existing M13 propose→approve→replay machinery (`service/Api/Program.cs` `/pending-changes`, `service/Api/Mcp/McpWriteTools.cs`) from an autonomous watcher instead of from a human typing into an MCP client.

---

## The loop

```
            ┌──────────────────────────────────────────────────────────────────────────┐
            │                       M18 closed loop (per tenant, scheduled)              │
            └──────────────────────────────────────────────────────────────────────────┘

  ① WATCH           ② DETECT                ③ PLAN              ④ SIMULATE
  service/Sync      DriftDetectionService   AutonomyPlanner     M16 simulator
  GraphDeltaSync    .CompareAsync           (net-new agent)     (AssignmentChecker)
  .RunAsync         → DriftReport           propose remediation → blast-radius report
  delta watermarks  + posture regression    over the M17 twin    (affected users/devices,
  (sync_state)      + advisory feed                              new overlaps/conflicts)
       │                  │                       │                     │
       ▼                  ▼                       ▼                     ▼
  audit + snapshot   signal {drift|         (verb, path, body)    embedded simulation
  appended to        posture|advisory}      = the corrective       attached to the
  SnapshotStore                             write to converge      proposal
       │                                          │                     │
       └──────────────────────────────────────────┴─────────────────────┘
                                       │
                                       ▼
  ⑤ PROPOSE  ──────────────►  POST /pending-changes  (the EXISTING M13 enqueue)
             PendingChangeRecord { proposer="autonomy:drift", kind, path, bodyJson,
                                   diffJson = DriftChange[] + embedded blastRadius }
                                       │
                                       ▼   state = "pending"
  ⑥ APPROVE  ◄────────  operator reviews diff + blast-radius in the WinUI inbox
             POST /pending-changes/{id}/approve   ←── HUMAN GATE (unchanged from M13)
                                       │
                                       ▼
  ⑦ APPLY    REPLAY (verb, path, body) through the normal write pipeline (Loopback.Http)
             → snapshot-on-write (AppendSnapshotIfChangedAsync) → audit (AppendAuditEventAsync)
                                       │
                                       ▼
  ⑧ VERIFY   next ① WATCH re-syncs the object; DriftDetectionService.CompareAsync
             re-runs against the new head snapshot → residualDrift == 0 ⇒ converged
                                       │
                                       ▼
  ⑨ AUDIT    the whole chain (watched → drifted → proposed → decided → applied → verified)
             recorded as an AutonomyRun row + the existing append-only audit_events
```

Every numbered step except ③ and ⑤'s wiring already exists. The loop is the new *orchestration*; the writes still go through `/pending-changes/{id}/approve` → `Loopback.Http.SendAsync` (`Program.cs:345`).

---

## The locked guarantee

**Rules and AI propose; a human approves the exact diff; nothing auto-applies.** This is the M13 write policy verbatim (`docs/PLUGINS-MCP.md` Decisions-locked table: *"Read-write, human-in-the-loop — AI proposes; operator approves the exact diff; snapshot + audit always. … No auto-apply in M13"*), and M18 does not relax it.

- **Autonomy ends at the inbox.** The watcher's terminal action is `POST /pending-changes` with `state="pending"` — byte-identical to what an MCP `propose_update_*` tool produces (`McpWriteTools.cs:133`). A proposal sits inert until a human hits **Approve**.
- **The approval path is unchanged.** `/pending-changes/{id}/approve` (`Program.cs:345`) is the *only* code that calls `Loopback.Http.SendAsync` for a proposed write. M18 adds no second apply path. If a human never approves, no write happens — full stop.
- **Autonomy is in watching + detecting + planning + simulating**, never in unattended writes (`ROADMAP.md`: *"the differentiator is autonomy in watching, planning, and simulating, never in unattended writes (M13 policy, unchanged)"*).
- **The `/autonomy/policy` toggles only enqueue-eligibility**, never auto-apply. The most a signal can do, even fully enabled, is *land a richer proposal in the inbox faster*. There is no policy value that applies a change without a human click.

---

## What's net-new vs wiring

| Piece | Status | Where |
|---|---|---|
| `service/Sync` scheduler (background loop, cadence, throttling) | **net-new** | flesh out `service/Sync/` — today only `GraphDeltaSync.cs` (the one-shot engine) exists |
| `AutonomyPlanner` agent (signal → remediation `(verb, path, body)`) | **net-new** | `service/Api/Autonomy/AutonomyPlanner.cs` |
| `AutonomyRun` records + `autonomy_runs` table | **net-new** | `service/Store/SnapshotStore.cs` (a new append-only table beside `pending_changes`) |
| `/autonomy/policy` + `/autonomy/runs` endpoints + DTOs | **net-new** | `Program.cs`, `Contracts.cs`, `crates/api-types`, `contract/openapi.yaml` |
| Delta-sync engine (audit watermark + snapshot dedup) | **wiring** | `GraphDeltaSync.RunAsync` already exists — M18 schedules it, doesn't rewrite it |
| Drift detection | **wiring** | `DriftDetectionService.CompareAsync` → `DriftReport` already exists |
| Blast-radius simulation | **wiring** | M16 simulator already exists — the planner calls it before enqueueing |
| The twin to plan over | **wiring** | M17 twin already assembled — the planner queries it |
| Propose → approve → replay → snapshot → audit | **wiring** | M13's `/pending-changes` pipeline, untouched |

> The honest summary: M18 is **two net-new components (a scheduler and a planner) plus glue**. Everything destructive — diffing, simulating, applying, snapshotting, auditing — is reuse. `ROADMAP.md` flags M18 as the only Part-II milestone where significant new code is "Yes — sync + agent."

### Net-new #1 — `service/Sync` scheduler

`GraphDeltaSync.RunAsync` (`service/Sync/GraphDeltaSync.cs:43`) is a one-shot: sync audit events past the `audit_watermark:{tenantId}` watermark (`sync_state` table, `GetSyncStateAsync`/`SetSyncStateAsync`), snapshot the headline config types (compliance, settings-catalog, apps), dedup on content hash via `AppendSnapshotIfChangedAsync`, one `CommitIndexAsync`. M18 wraps it in a `BackgroundService` (`AutonomyLoop : IHostedService`) that ticks on the configured cadence, holds a `loop_watermark:{tenantId}` so it knows which snapshots are new since last evaluation, and throttles per `/autonomy/policy`. Graph 429 `Retry-After` is already honored by SDK middleware (per the comment at `GraphDeltaSync.cs:21`), so the scheduler only needs an outer inter-tenant delay.

### Net-new #2 — `AutonomyPlanner`

Given a signal (a `DriftReport` change, a posture regression, an advisory), the planner produces the corrective `(verb, path, body)` — the write that re-converges the object to its baseline (drift) or to the recommended posture (regression/advisory). For drift, the "plan" is often just *restore the base snapshot body* (the inverse of the diff `DriftDetectionService` already computed). It then calls M16 to attach a blast-radius and enqueues via `POST /pending-changes`. The planner never applies.

---

## Signals

| Signal | Source | Cadence | → Proposal type |
|---|---|---|---|
| **Config drift** — object body diverged from its baseline snapshot | `GraphDeltaSync` snapshot delta → `DriftDetectionService.CompareAsync` → `DriftReport.Changes` (`DriftChange[]`) | every sync tick (default 15 min) | `propose_update` (restore base body) or `propose_create` (re-add a `deleted`/`Critical` object) |
| **Posture regression** — security score or a category dropped | M17 twin recomputes `SecurityPosture` (`crates/api-types` `SecurityPosture.score`, `gaps`); planner diffs vs. the last run's score | hourly | `propose_update`/`propose_assign` closing the regressed `SecurityGap` |
| **New advisory** — a CIS/Microsoft baseline or CVE-driven hardening lands | advisory feed (open question — see below); matched against the twin | daily | `propose_update`/`propose_create` applying the recommended setting |

Every signal funnels to the **same** terminal action: a `PendingChangeRecord` with `proposer = "autonomy:{signal}"`, the corrective `bodyJson`, and a `diffJson` carrying both the field-level `DriftChange[]` and an embedded blast-radius. Severity gating reuses `DriftSeverity` (`DriftDetectionService` already classifies `added`=Medium, `deleted`=Critical, modified by field).

---

## Contract additions

`contract/openapi.yaml` is the source of truth; these mirror into `crates/api-types/src/lib.rs` and `service/Api/Contracts.cs`.

| Method + path | Purpose |
|---|---|
| `GET /autonomy/policy` | Read which signals may auto-enqueue, cadence, scope, severity floor. |
| `PUT /autonomy/policy` | Operator edits the policy (e.g. enable drift, keep advisories off). |
| `GET /autonomy/runs` | Loop history — each tick: watched / drifted / proposed / decision / verify result. |
| `GET /autonomy/runs/{id}` | One run with its full chain (links to the `pending-changes` id it raised). |

**No new write/apply endpoint.** A generated proposal lands in the **existing** `POST /pending-changes` (`Program.cs:308`) and is approved/rejected through the **existing** `/pending-changes/{id}/approve|reject` (`Program.cs:345`/`411`). The only new fields are: a `proposer` of `autonomy:*` (the request already carries `proposer` — `CreatePendingChangeRequest`, `Contracts.cs:218`) and a blast-radius embedded in `diffJson`. Because `diffJson` is opaque, free-form JSON parsed back into `DriftChange[]` (`Program.cs:298` `ToPendingDto`), M18 piggybacks the blast-radius as a sibling object without changing the `pending_changes` schema. The WinUI inbox renders the `DriftChange[]` with the existing M6 `drift_row` panel and the blast-radius in a new sub-panel.

---

## Sample data

### 1. Autonomy policy (`GET /autonomy/policy`)

```json
{
  "tenantId": "a17f...",
  "enabled": true,
  "cadenceMinutes": 15,
  "scope": {
    "objectTypes": ["DeviceCompliancePolicy", "SettingsCatalogPolicy"],
    "assignmentGroupAllowlist": ["All Windows Corporate Devices"]
  },
  "signals": {
    "drift":             { "enabled": true,  "minSeverity": "Medium" },
    "postureRegression": { "enabled": true,  "minScoreDrop": 3 },
    "advisory":          { "enabled": false, "minSeverity": "High" }
  },
  "throttle": { "maxProposalsPerRun": 5, "maxInflightPending": 20 },
  "note": "Autonomy may only ENQUEUE proposals. No value here auto-applies a write."
}
```

### 2. Generated `PendingChange` enriched with a blast-radius

The wire shape is exactly the M13 `PendingChange` (`crates/api-types/src/lib.rs:360`); `changes` is the parsed `diffJson`. The autonomy planner embeds a `blastRadius` object alongside the `DriftChange[]` so the inbox shows the diff **and** the impact in one card.

```json
{
  "id": "9f3c2a7b51d04e8e",
  "proposer": "autonomy:drift",
  "kind": "update",
  "path": "/compliance-policies",
  "objectId": "b2e4...-win10-baseline",
  "objectName": "Win10 — Corporate Baseline",
  "state": "pending",
  "createdUtc": "2026-06-24T09:15:04Z",
  "changes": [
    {
      "path": "passwordMinimumLength",
      "kind": "Modified",
      "before": 8,
      "after": 6
    },
    {
      "path": "passwordRequired",
      "kind": "Modified",
      "before": true,
      "after": false
    }
  ],
  "blastRadius": {
    "simulatedUtc": "2026-06-24T09:15:03Z",
    "affectedDevices": 1842,
    "affectedUsers": 1610,
    "assignmentGroups": ["All Windows Corporate Devices"],
    "newConflicts": 0,
    "becomesNoncompliant": 0,
    "becomesCompliant": 0,
    "note": "Restoring baseline re-tightens password policy; no new exclude overlaps; no devices flip noncompliant because the corrective body matches the prior enforced state."
  }
}
```

The corrective `bodyJson` (stored, not echoed in the list DTO) is the **base snapshot body** — `passwordMinimumLength: 8`, `passwordRequired: true` — i.e. the inverse of the drift the watcher detected.

### 3. Autonomy-run record (`GET /autonomy/runs/{id}`) — end-to-end

A compliance policy drifted (someone weakened the password rule), the planner proposed the restore with a blast-radius, the operator approved, the next tick verified zero residual drift.

```json
{
  "runId": "run_2026-06-24T09-15-00Z_a17f",
  "tenantId": "a17f...",
  "startedUtc": "2026-06-24T09:15:00Z",
  "finishedUtc": "2026-06-24T09:15:06Z",
  "watched": {
    "auditEventsPulled": 12,
    "snapshotsChanged": 1,
    "objectTypes": ["DeviceCompliancePolicy", "SettingsCatalogPolicy"]
  },
  "detected": [
    {
      "signal": "drift",
      "objectId": "b2e4...-win10-baseline",
      "objectName": "Win10 — Corporate Baseline",
      "severity": "High",
      "baseSnapshotId": "c91a...",
      "headSnapshotId": "f40d...",
      "changes": [
        { "path": "passwordMinimumLength", "kind": "Modified", "before": 8, "after": 6 },
        { "path": "passwordRequired", "kind": "Modified", "before": true, "after": false }
      ]
    }
  ],
  "proposed": [
    {
      "pendingChangeId": "9f3c2a7b51d04e8e",
      "kind": "update",
      "path": "/compliance-policies",
      "blastRadius": { "affectedDevices": 1842, "newConflicts": 0 }
    }
  ],
  "decision": {
    "pendingChangeId": "9f3c2a7b51d04e8e",
    "operator": "acgell@contoso.com",
    "action": "approved",
    "decidedUtc": "2026-06-24T09:41:22Z",
    "appliedObjectId": "b2e4...-win10-baseline",
    "auditEventId": "7c1e..."
  },
  "verify": {
    "verifiedUtc": "2026-06-24T09:56:08Z",
    "verifySnapshotId": "aa18...",
    "residualDriftChanges": 0,
    "converged": true
  }
}
```

The `decision` block mirrors what `/pending-changes/{id}/approve` already writes: the replay (`Program.cs:365`), the snapshot-on-write (`Program.cs:395` `AppendSnapshotIfChangedAsync`), and the audit row (`Program.cs:401` `AppendAuditEventAsync`, actor `"autonomy:drift (approved by operator)"`). `verify.converged` comes from a follow-up `DriftDetectionService.CompareAsync(base, verifySnapshot)` returning `DriftReport.DriftDetected == false`.

---

## Natural-language ops

The M13 MCP server already exposes the read tools (`list_objects`, `get_object`, `get_drift`) and the `propose_*` write tools (`McpWriteTools.cs`). M18 makes a fleet-level intent flow through the **same** plan→simulate→inbox path the autonomous loop uses:

> "Make all Windows devices CIS L1, show me the blast radius first."

1. **Plan over the twin (M17).** The agent resolves "all Windows devices" to the assignment groups in the twin and diffs each affected compliance/settings-catalog policy against the CIS L1 recommended body → a set of corrective `(update, path, body)` writes.
2. **Simulate (M16) — *first*, as asked.** Each candidate write is run through the blast-radius simulator: affected device/user counts, new conflicts, devices that would flip noncompliant. "Show me the blast radius first" is honored because simulation is a *precondition* of enqueueing — `ROADMAP.md`: *"No M18 remediation reaches the inbox without an M16 blast-radius."*
3. **Inbox (M13).** The agent emits one `PendingChange` per write (each carrying its diff + blast-radius, sample #2 above) into `/pending-changes`. The operator sees the full fleet impact and approves the ones they want — per-change, exactly as today.

The intent never auto-applies; it produces a *reviewable batch of simulated proposals*. A batch-approve UX is an open M13 decision (`docs/PLUGINS-MCP.md` open-decision #2), independent of M18.

---

## Definition of done

Mirrors `ROADMAP.md`'s M18 DoD: *"an injected drift surfaces as a pending change carrying both a diff and a blast-radius; approving it re-converges and a verify pass confirms zero residual drift; the whole loop is audited."*

- [ ] An **injected drift** (manually weaken `Win10 — Corporate Baseline` in the tenant) is picked up by the scheduled `service/Sync` loop within one cadence interval.
- [ ] It surfaces in the **existing** `/pending-changes` inbox as a `proposer="autonomy:drift"` row carrying **both** a field-level diff (`DriftChange[]`) **and** an embedded blast-radius (affected device/user counts).
- [ ] **Approving** it replays the corrective write through `/pending-changes/{id}/approve`, snapshots, and audits — re-converging the object to baseline.
- [ ] The **next loop tick verifies** via `DriftDetectionService.CompareAsync`: `residualDriftChanges == 0`, `converged == true`.
- [ ] The full chain (watched → detected → proposed → approved → applied → verified) is queryable via `GET /autonomy/runs/{id}` and the append-only `audit_events`.
- [ ] **Nothing auto-applied**: with the operator never clicking Approve, the drift stays a pending proposal indefinitely and no write occurs.

---

## Implementation status

The **drift** signal is the real end-to-end loop (watch → detect → plan → simulate → propose →
verify), enqueuing `proposer="autonomy:drift"` pending changes with diff + blast-radius. As of
2026-07-13 the two previously-dead signals were activated (`AutonomyEngine.RunTickAsync`):

- **Posture signal — implemented, detection-only.** Each tick (when `signals.postureRegression.enabled`)
  the engine fetches the benchmark score over loopback (`GET /posture/score`), diffs it against a
  per-tenant `posture_watermark`, and emits an `AutonomyDetectionDto(signal:"posture", …)` when the
  drop meets `minScoreDrop` (default 3; severity scales with the drop). Pure decision in
  `AutonomyEngine.EvaluatePostureRegression` (hermetically tested, `AutonomyTests`). **Deliberately
  detection-only:** a score drop has no single *provably-safe* mechanical corrective write, so the loop
  surfaces the regression without fabricating a proposal — the drift signal handles the config restore
  that usually caused it. Auto-*proposing* posture remediations (mapping a regressed control → a
  specific write) is a documented follow-up, gated on the M19 benchmark→setting map.
- **Advisory signal — implemented (fixture-backed detection).** A curated advisory catalog (the embedded
  `service/Api/Autonomy/advisories.json`) is matched against the tenant's current posture gaps each tick
  (`AdvisoryCatalog.Match`, hermetically tested): an advisory fires when its category + keyword condition
  is present in the live posture and its severity meets the policy floor. Detection-only by design — an
  advisory's remediation is a config write whose blast radius must be human-approved through the M13 inbox,
  never auto-enqueued. Swapping the bundled catalog for a live CVE/baseline feed (open question #2) is a
  drop-in replacement of the loader.
- **Decision audit linkage.** `Operator`/`AuditEventId` on the decision are now populated from the
  approve/reject audit row (`QueryAuditAsync`), no longer hardcoded null.
- **Not yet automated:** the injected-drift → approve → converge DoD is an *integration* test (needs a
  live sidecar + tenant); it's covered by live behaviour today, not by a hermetic test.

## Open questions

1. **Autonomy scope — which signals may auto-enqueue.** Drift restore is the safest (the corrective body is provably the prior approved state). Should `postureRegression` and `advisory` default OFF and require explicit opt-in per `/autonomy/policy`? *Lean: drift on, regression opt-in, advisory off by default.*
2. **Advisory source.** Where does the "new advisory" signal come from — a curated CIS/Microsoft baseline feed bundled with the sidecar, a downstream MCP plugin (`plugins.json`, M13.3), or a periodic fetch? *Lean: start with a static bundled CIS L1 baseline; make the feed pluggable.*
3. **Sync cadence / throttling.** Default 15 min vs. operator-tunable; per-tenant inter-run delay to stay polite to Graph (SDK already honors 429 `Retry-After`). How aggressively to coalesce repeated drift on the same object into one pending change rather than re-proposing each tick? *Lean: dedup by `(objectId, headSnapshotId)`; don't re-enqueue while a pending proposal for that object already exists (`maxInflightPending`).*
4. **Verify latency vs. Graph eventual consistency.** A just-applied write may not reflect in the next immediate sync. Should verify wait one full cadence, or poll the specific object with backoff before declaring residual drift?
5. **Proposal staleness.** If an autonomy proposal sits unapproved for hours and the object drifts *again*, the stored `bodyJson` may no longer be the right correction. Tie into the M13 proposal-TTL open item (`docs/PLUGINS-MCP.md` deferred: TTL not yet implemented).
