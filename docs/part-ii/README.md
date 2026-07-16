# Part II — detailed milestone designs (M14–M22)

This directory builds out each Part-II flight from [`../ROADMAP.md`](../ROADMAP.md) (the
"platform play" section) into a standalone design doc — thesis, architecture, Core changes,
contract additions, **realistic sample JSON**, client surface, DoD, and open questions. Same
relationship the [`../PLUGINS-MCP.md`](../PLUGINS-MCP.md) (M13) and
[`../REGRESSION-TESTING.md`](../REGRESSION-TESTING.md) (the RT program) docs have to their
ROADMAP line items.

Every claim is grounded in the actual fork — each doc cites `file:method`/`file:line` for the
Core engines it wires. Where a flight needs net-new Core, it's flagged.

## The arc

| Doc | Flight | Bet | Net-new Core? |
|---|---|---|---|
| [M14-device-ops.md](./M14-device-ops.md) | **Hands** | Live device & remote action (sync/wipe/retire/collect-logs…) over the list-only `ManagedDeviceService`; fold in `MacCustomAttribute` | **Yes** — action verbs |
| [M15-gitops.md](./M15-gitops.md) | **Source** | Policy-as-Code / GitOps — the snapshot store *is* a content-hashed object store; `pull`/`plan`/`push` wire Export + Import + JsonDrift | No — wiring |
| [M16-simulator.md](./M16-simulator.md) | **Foresight** | What-if / blast-radius simulator over `AssignmentCheckerService`, before any write | Mostly no |
| [M17-twin.md](./M17-twin.md) | **Twin** | Tenant digital twin + graph analytics (orphans, conflicts, cycles, escape paths) | No — assembly |
| [M18-autonomy.md](./M18-autonomy.md) | **Autonomy** | Closed loop: watch→plan→simulate→gate→apply→verify (the AI SRE), terminating at the M13 inbox | **Yes** — sync scheduler + planner |
| [M19-posture.md](./M19-posture.md) | **Posture** | Continuous benchmark scoring + audit-evidence packs, extending the existing `SecurityPosture` DTO | No — wiring |
| [M20-fleet.md](./M20-fleet.md) | **Fleet** | MSP-scale multi-tenant fan-out (Pattern G), golden templates, campaigns | **Yes** — fan-out + GDAP |
| [M21-ecosystem.md](./M21-ecosystem.md) | **Ecosystem** | Shareable policy packs / remediation playbooks / plugin marketplace | No — M15 + M13.3 |
| [M22-cross-mdm.md](./M22-cross-mdm.md) | **Moonshot** | The same contract over Jamf / Workspace ONE — *stretch/vision, not committed* | **Yes** — a provider |

**Spine:** M14 → M15 → M16 → M17 → M18 (you can't safely automate what you can't simulate; you
can't simulate without a twin). M19/M20 ride alongside; M21/M22 are the endgame.

## Companion plans

- [IMPLEMENTATION-PROMPTS.md](./IMPLEMENTATION-PROMPTS.md) — copy-paste prompts for driving each
  milestone (M15–M22) with a local Claude Code session.
- [UNBUILT-SCREENS.md](./UNBUILT-SCREENS.md) — build-out plan for the 7 stubbed + 1 partial client
  screens (effort, layer-by-layer work, templates, checklists, risks, smoke). The "knock out the
  remaining screens in one session" plan.

## Three patterns introduced here

- **F — device-action:** POST a Graph action verb over a device-set, gated by the M6 confirm
  dialog, recorded as an audit event (the *action* is the record — no config diff). [M14]
- **G — multi-tenant fan-out:** the same `(verb, path, body)` replayed across a tenant set,
  results merged tenant-tagged; a fleet write is N gated M13 replays. [M20]
- **H — declarative loop:** plan (`JsonDrift`) → gate → apply (`ImportService`) → snapshot →
  verify-converged. Reused by M15 GitOps, M18 autonomy, and M20 campaigns alike. [M15]

## Locked guarantees (hold across every doc)

- **Propose → human-approve-the-exact-diff → apply.** No auto-apply, ever — autonomy is in
  *watching, planning, and simulating*, never unattended writes (M13 policy, unchanged).
- **Conditional Access stays read-only by design.** M16 simulates it, M19 documents it, M15
  versions it — nothing writes it.
- **No Graph logic in the Rust client.** Part II adds Core methods and *wires/assembles* existing
  engines; it does not move Graph into Rust. (This is exactly what makes M22 conceivable.)
- **Append-only store.** GitOps mirrors it; nothing mutates history in place.
- **Simulate before automate.** No M18 remediation reaches the inbox without an M16 blast-radius.
</content>
</invoke>
