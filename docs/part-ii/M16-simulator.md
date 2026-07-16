# M16 — Foresight: the what-if / blast-radius simulator

> Never let an operator OR an AI apply a change blind: simulate impact across the assignment graph *before* any write, and attach the blast-radius report to the gate that approves it.

## Why it gates autonomy

Every prior milestone makes a write *visible* — `/preview-diff` (M6) shows the field-level
delta, the M13 inbox parks it as a `PendingChange`, M15 GitOps replays a desired-state file.
None of them answer the only question that matters operationally: **who does this hit?** A
field diff that flips `passwordRequired` from `false` to `true` reads as one line of green;
the truth is *800 devices go non-compliant tonight*. A Conditional Access grant change reads
as one nested object; the truth is *4,200 users can no longer sign in*.

M16 is the precondition for M18 (the autonomous remediation loop). The thesis of autonomy is
that the agent can close the loop — detect drift, propose a fix, apply it — without a human in
the path. That is only safe if the agent can *predict* the fix's blast radius and refuse to
apply when the radius exceeds a policy threshold. **An automated remediation you can't simulate
is an automated outage.** Simulate-before-automate is the rail that lets M18 exist at all:
M18's apply step becomes "simulate → check severity against an allowlist → apply or escalate to
the M13 inbox." No simulator, no auto-apply.

## What it builds on

The simulator is **not** a new Graph engine. It reuses the assignment-resolution kernel that
already powers the assignment explorer.

**`AssignmentCheckerService` (`service/Core/Services/AssignmentCheckerService.cs`,
interface `IAssignmentCheckerService.cs`)** — the IntuneAssignmentChecker engine ported to
.NET. It already resolves effective assignments from every angle M16 needs:

- `GetUserAssignmentsAsync(upn, …)` — every policy effectively assigned to a user via
  transitive group membership, "All Users", "All Licensed Users"; **excluded groups are
  respected and remove a policy from the result.** This is exactly the include/exclude
  resolution a CA-lockout simulation needs.
- `GetGroupAssignmentsAsync(groupId, groupName, …)` — every policy a group is included in *or
  excluded from*. The primitive for "what is the current target set."
- `GetDeviceAssignmentsAsync(deviceName, …)` — device-side resolution via transitive groups /
  "All Devices"; the primitive for a compliance-flip count.
- `GetAllPoliciesWithAssignmentsAsync(…)` — every policy with its resolved target summary
  (All Users / All Devices / group names / exclusions). The **proposed** side reuses this:
  apply the proposed assignment in-memory, then re-resolve.
- `CompareGroupAssignmentsAsync(g1, …, g2, …)` — one row per policy showing each group's status
  (included / excluded / not assigned). Directly powers **conflict & redundancy detection**:
  proposed-target vs. live-target is a two-set compare.
- `GetUnassignedPoliciesAsync` / `GetEmptyGroupAssignmentsAsync` — surface a proposed change
  that targets nothing or an empty group (a "this will do nothing" warning).
- `GetFailedAssignmentsAsync` — current error/conflict/notApplicable/nonCompliant rows; the
  *baseline* a compliance-flip simulation diffs against.
- `PrefetchAllToCacheAsync(…, forceRefresh)` — warms the policy lists into Core's LiteDB
  `ICacheService` so a simulation doesn't re-scan all of Graph per call (see Open questions).

Every method emits `AssignmentReportRow` (`service/Core/Models/AssignmentReportRow.cs`):
`PolicyId`, `PolicyName`, `PolicyType`, `Platform`, `AssignmentSummary`, `AssignmentReason`,
`GroupId/GroupName`, `Group1Status/Group2Status`, and the failed-mode fields
`TargetDevice/UserPrincipalName/Status/LastReported`. The simulator works entirely in this
vocabulary — no new model leaks out of Core.

**`IGroupService` (`service/Core/Services/IGroupService.cs`)** — turns target *groups* into
*member counts and principals*: `GetMemberCountsAsync(groupId)` → `GroupMemberCounts(Users,
Devices, NestedGroups, Total)`, and `ListGroupMembersAsync(groupId)` → `List<GroupMemberInfo>`
(MemberType / DisplayName / Status / Id) for the sample-principals slice of the report.
`ListDynamicGroupsAsync` flags targets whose membership is rule-driven (a fidelity caveat).

The endpoint constructs the checker the same way `AssignmentExplorerEndpoints.cs` already does
(`new AssignmentCheckerService(graphClient)` — there is no DI registration; it has a cache
ctor overload used by `Endpoints/CachedReader.cs`).

## How a simulation works

A simulation takes a **proposed change** — the same shape M6/M13 already pass around: a verb
(`create | update | delete | assign`), a `path` (the surface, e.g. `compliance-policies`,
`conditional-access`, `settings-catalog`), an optional object id, and the body JSON (for
`assign`, an `Assignment[]` from `crates/api-types/src/lib.rs::Assignment`:
`kind ∈ {group, exclusionGroup, allDevices, allUsers}`, `groupId`, `filterId`, `filterMode`,
`intent`). The pipeline:

1. **Resolve the live target set.** For the object id, call `GetGroupAssignmentsAsync` /
   `GetAllPoliciesWithAssignmentsAsync` to read the *current* include/exclude assignment rows.
2. **Resolve the proposed target set.** Apply the proposed body in-memory to the live
   assignments (add/remove/replace the include & exclude groups, or toggle All Users / All
   Devices), producing the post-change target set — without writing anything to Graph.
3. **Expand both sets to principals.** For each group target, `GetMemberCountsAsync` for the
   headline counts and `ListGroupMembersAsync` for a bounded sample of affected principals.
   "All Users"/"All Devices" expand to the tenant-wide directory counts. Exclusion groups are
   *subtracted* — mirroring `GetUserAssignmentsAsync`'s "excluded groups remove a policy."
4. **Classify the deltas** (proposed-set ⊖ live-set, a `CompareGroupAssignmentsAsync`-style
   two-set compare):
   - **newly-affected** — principals in proposed but not live (the lockout / compliance-flip
     count).
   - **no-longer-affected** — principals in live but not proposed (a relaxation).
   - **conflicts** — the same principal landing in both an *include* and an *exclude* target,
     or a proposed include that overlaps an existing exclude (and vice-versa). Surfaced from
     the include/exclude status columns the checker already returns.
   - **redundant** — a proposed target already fully covered by a live target (e.g. assigning a
     group that's a subset of an existing "All Users", or re-adding an already-present group).
5. **Score & summarize.** Roll counts + conflicts into a `severity`
   (`info | low | medium | high | critical`) and a one-line human `summary`. For
   compliance/CA paths, cross-reference `GetFailedAssignmentsAsync` so the report can say "of
   the 800 newly-targeted, 740 currently pass — they will flip."

The result is a **blast-radius report** — pure compute over resolved assignments, no write —
attached to whatever gate requested it.

## Contract additions

All under `contract/openapi.yaml` (source of truth), mirrored into
`crates/api-types/src/lib.rs` and `service/Api/Contracts.cs` (camelCase, byte-for-byte). New
endpoint module `service/Api/Endpoints/SimulateEndpoints.cs` → `app.MapSimulate()` in
`Program.cs`.

| Method & path | Body | Returns | Notes |
|---|---|---|---|
| `POST /simulate` | `SimulateRequest { verb, path, objectId?, bodyJson }` | `BlastRadiusReport` | Generic pre-flight for any proposed write. |
| `POST /simulate/assignment` | `SimulateRequest` with `verb:"assign"` + `Assignment[]` body | `BlastRadiusReport` | Convenience alias; same engine, assignment-only validation (conflict/redundancy heavy). |
| `GET  /simulate/graph?objectId=` | — | resolved current target set | Debug/inspection: the live assignment graph the sim diffs against. |

`BlastRadiusReport` (new DTO): `{ severity, summary, affectedUserCount, affectedDeviceCount,
noLongerAffectedCount, sampleAffectedPrincipals[], conflicts[], redundancies[],
crossPolicyImpacts[] }`. `SimulateRequest` reuses the `PendingChange` field vocabulary
(`proposer/verb/path/objectId/bodyJson`) so the same payload flows from inbox → simulator.

**M13 wiring (automatic).** Each `propose_*` tool in `service/Api/Mcp/McpWriteTools.cs`
already POSTs to `/preview-diff` before enqueuing a `PendingChange`. M16 inserts one hop: after
`/preview-diff` returns the field diff, the tool POSTs the same `{verb, path, objectId,
bodyJson}` to `/simulate`, and stores the `BlastRadiusReport` alongside the diff on the
`PendingChange`. The AI never proposes blind, and the operator opening the inbox sees the diff
*and* the blast radius in one row. M18 reads `severity` off the same report to decide
auto-apply vs. escalate.

## Sample data

### Example A — Conditional Access lockout warning

**Request** — an operator (or AI) tightens a CA policy to require a compliant device, and the
proposed include target widens from a pilot group to All Users:

```json
{
  "proposer": "operator:acgell995",
  "verb": "update",
  "path": "conditional-access",
  "objectId": "ca-9f12...require-compliant",
  "bodyJson": {
    "displayName": "Require compliant device — all access",
    "state": "enabled",
    "conditions": { "users": { "includeUsers": ["All"], "excludeGroups": ["grp-breakglass"] } },
    "grantControls": { "operator": "OR", "builtInControls": ["compliantDevice"] }
  }
}
```

**Response:**

```json
{
  "severity": "critical",
  "summary": "Enabling this CA policy for All Users requires a compliant device for 4,217 users; 3,910 of them currently have NO compliant device and will be blocked at next sign-in. Break-glass group is correctly excluded.",
  "affectedUserCount": 4217,
  "affectedDeviceCount": 0,
  "noLongerAffectedCount": 0,
  "sampleAffectedPrincipals": [
    { "type": "user", "displayName": "Dana Okoro", "upn": "dana.okoro@contoso.com", "id": "8c2a...", "reason": "now in include scope (All Users), no compliant device" },
    { "type": "user", "displayName": "Raj Patel", "upn": "raj.patel@contoso.com", "id": "1f77...", "reason": "now in include scope (All Users), no compliant device" }
  ],
  "conflicts": [],
  "redundancies": [
    { "kind": "subsetCovered", "detail": "Pilot group 'CA-Pilot' (212 members) is now fully covered by include:All Users; the explicit pilot include is redundant." }
  ],
  "crossPolicyImpacts": [
    { "policyId": "comp-win-baseline", "policyName": "Windows Compliance Baseline", "detail": "3,910 of the newly-blocked users own only devices that fail this compliance policy" }
  ]
}
```

### Example B — compliance policy flip

**Request** — tighten a Windows compliance policy to require BitLocker, targeted at All Devices:

```json
{
  "proposer": "ai:remediation-bot",
  "verb": "update",
  "path": "compliance-policies",
  "objectId": "comp-win-baseline",
  "bodyJson": {
    "displayName": "Windows Compliance Baseline",
    "bitLockerEnabled": true,
    "secureBootEnabled": true,
    "assignments": [{ "kind": "allDevices" }]
  }
}
```

**Response:**

```json
{
  "severity": "high",
  "summary": "Requiring BitLocker on the Windows baseline (All Devices) flips 812 of 6,540 evaluated devices from compliant to non-compliant. 812 devices currently report BitLocker off.",
  "affectedUserCount": 0,
  "affectedDeviceCount": 812,
  "noLongerAffectedCount": 0,
  "sampleAffectedPrincipals": [
    { "type": "device", "displayName": "LT-FIN-0421", "id": "d31a...", "reason": "BitLocker off → will report nonCompliant" },
    { "type": "device", "displayName": "LT-HR-1180",  "id": "a902...", "reason": "BitLocker off → will report nonCompliant" }
  ],
  "conflicts": [
    { "kind": "includeExcludeOverlap", "detail": "Proposed include:All Devices overlaps existing exclude group 'Kiosk-Devices' (88 members); kiosks remain excluded — confirm intent." }
  ],
  "redundancies": [],
  "crossPolicyImpacts": [
    { "policyId": "ca-9f12...require-compliant", "policyName": "Require compliant device", "detail": "The 812 newly non-compliant devices' owners (≈760 users) will be blocked by this CA policy at next sign-in" }
  ]
}
```

Note the `crossPolicyImpacts` chain: a compliance flip cascades into a CA lockout because the
checker resolves the *same* devices against *both* policies. That second-order radius is the
whole point.

## Integration points

- **M6 confirm gate.** The "Review changes" panel that today shows `/preview-diff` output gains
  a blast-radius header: severity badge + the one-line `summary` + affected counts, fetched from
  `/simulate` when the panel opens. The operator approves the diff *and* the radius.
- **M13 inbox diff.** `McpWriteTools.cs` calls `/simulate` after `/preview-diff`; the
  `BlastRadiusReport` rides on the `PendingChange` and renders in the inbox row
  (`app/src/pending.rs`), reusing the M6 panel. A `critical` report can be styled to require an
  extra confirm.
- **M15 GitOps apply.** The desired-state apply planner simulates each file's resulting write
  before committing, so a "git apply" of a repo change still produces a blast-radius preview —
  GitOps doesn't become a blind-write bypass.
- **M18 autonomous loop.** The remediation loop calls `/simulate` as its gate: compare
  `severity` against an auto-apply allowlist (e.g. `≤ low` auto-applies, `≥ medium` escalates
  to the M13 inbox). This is the rail that makes M18 a *governed* loop rather than an outage
  generator.

## Definition of done

- `POST /simulate` with a proposed compliance/CA/assignment change returns a `BlastRadiusReport`
  with non-zero affected counts **before** any write reaches Graph.
- The reported `affectedDeviceCount` / `affectedUserCount` matches **post-apply reality** on a
  sandbox object: simulate against a throwaway compliance policy targeting a small test group,
  apply, re-run `GetFailedAssignmentsAsync` / `GetDeviceAssignmentsAsync`, and assert the count
  the sim predicted equals the count that actually flipped (±0 for static groups).
- Conflict detection fires on a proposed include that overlaps a live exclude group;
  redundancy detection fires on re-adding an already-covered group.
- Every `propose_*` MCP tool attaches a `BlastRadiusReport` to its `PendingChange`; the inbox
  row renders severity + summary.
- Integration test (`Api.Tests`): simulate → apply → measure round-trip on a sandbox object,
  asserting predicted == actual.

## Implementation status

- **Cross-policy cascade — landed.** `crossPolicyImpacts` is no longer a stub; it computes the
  compliance ⇄ Conditional Access coupling through the `compliantDevice` grant control, live
  against the tenant (`SimulateEndpoints.cs::DetectCrossPolicyImpactsAsync`):
  - **Direction A (Example A):** a CA change that (newly) requires a compliant device — either by
    widening scope or by adding the grant to an already-broad policy — surfaces the device-compliance
    policies that now gate those users' sign-in (or a hard-lockout warning when the tenant has none).
  - **Direction B (Example B):** a compliance change targeting devices surfaces every *enabled*
    CA policy that requires a compliant device (report-only annotated) whose owners would be blocked.
  - The pure classifiers (`BuildCaLockoutImpacts` / `BuildComplianceFlipImpacts`) are hermetically
    unit-tested in `Api.Tests.Unit/SimulateCrossPolicyTests.cs`; a non-mutating live E2E test
    (`Api.Tests/SimulateEndpointTests.cs`) drives the full `/simulate` path against a real tenant.
- **Deferred (v1 scope):** cross-*dimension* precision — the exact device→owner join that yields the
  doc's illustrative "≈760 users" figure — is intentionally not computed (see the tenant-scale open
  questions below); the report states the coupling and the counts it can compute honestly. The
  mutating simulate→apply→**measure** round-trip on a sandbox object needs a live Ivy24 session to
  author/validate safely (and compliance evaluation is asynchronous, so it isn't reliably assertable
  synchronously) — it remains a manual smoke step, tracked, not yet automated.

## Open questions

- **Dynamic-group fidelity.** `ListDynamicGroupsAsync` flags rule-driven groups whose
  membership can shift between simulation and apply. For a dynamic target the report should mark
  the count *estimated* and surface the membership rule, not a hard number — the simulation is
  only as fresh as the last directory evaluation.
- **Staleness vs. live truth.** `GetMemberCountsAsync` reads through Core's `ICacheService`; a
  simulation off a stale cache can under/over-count. Should `/simulate` force a fresh
  `PrefetchAllToCacheAsync(forceRefresh:true)` for the targeted groups, or accept cache TTL and
  stamp the report with the resolution timestamp?
- **Caching the assignment graph.** Resolving the full live target set per call is expensive
  (it scans every policy type). Cache the resolved assignment graph keyed by
  `{tenantId}|assignment-graph` in `ICacheService` (the same pattern as
  `Endpoints/CachedReader.cs`), invalidated on any write via `Endpoints/CacheInvalidation.cs` —
  so the simulator reuses a warm graph and only re-resolves the deltas the proposed change
  touches.
- **Tenant-scale counts for "All Users" / "All Devices."** Expanding tenant-wide targets to
  exact principal lists is prohibitive; the report uses directory *counts* for headlines and a
  bounded `sampleAffectedPrincipals` slice. Is a count + sample sufficient for the confirm gate,
  or does M18's auto-apply decision need the full set?
- **Nested-group depth.** `GroupMemberCounts.NestedGroups` is non-zero; deep nesting inflates
  resolution cost. Bound the transitive expansion depth, or trust the checker's existing
  transitive resolution and cache aggressively?
