# M21 — Ecosystem: packs, playbooks, and a plugin market

> GitOps (M15) made config portable and M13.3 already aggregates third-party capability — so M21 makes both *shareable*: versioned policy **packs**, parameterized remediation **playbooks**, and a curated plugin **marketplace**, all landing in the one governed approval inbox (`/pending-changes`) and audit trail.

---

## What makes it nearly free

M21 ships **no new Graph** (`ROADMAP.md` line 379: "M15 GitOps + M13.3 MCP aggregation … No"). Two
already-shipped properties carry it:

- **M15 made desired state a repository.** `ExportService` serializes the live tenant into a
  normalized, GUID-resolved file tree (one file per object); `cmpx plan` runs `JsonDrift.Diff`
  between repo and tenant; `cmpx push`/`ImportService` (+ MigrationTable) applies it
  (`ROADMAP.md` lines 294–301). A **pack is just that repo**, authored once and adopted by anyone —
  the snapshot store is "the **local mirror** of a Git source of truth" (`ROADMAP.md` line 298),
  so a pack is a portable copy of someone else's `main`.
- **M13.3 already aggregates plugins.** `service/Api/Mcp/McpPlugins.cs` loads downstream MCP
  servers from `plugins.json` at startup and re-exposes their tools namespaced
  `plugin_<name>_<tool>` through cmProjectX's one `/mcp` endpoint (`PLUGINS-MCP.md` lines 174,
  108–114). `docs/plugins.example.json` is the existing config shape (http / stdio transports,
  per-server auth headers). A **marketplace is a discovery layer over that file** — it tells an
  operator *which* server to add; the wiring that runs it already exists.

So M21 is wiring: a manifest format on top of M15 repos, an ordered-`propose_*` format on top of the
M13 inbox, and a registry index on top of `plugins.json`. The governance rails
(`PermissionCheckService` scope-gating, approve-replay, audit) are reused verbatim
(`PLUGINS-MCP.md` lines 116–122).

---

## Three shareable artifacts

| Artifact | What it is | Format | Built on | Governed by |
|---|---|---|---|---|
| **Policy pack / baseline** | A versioned repo of desired Intune state others can adopt | Git repo (M15 file tree) + `pack.json` manifest | M15 `ExportService` / `JsonDrift` / `ImportService` | M15 plan → gate → apply; each write a snapshot + audit |
| **Remediation playbook** | A parameterized, ordered set of proposed writes the M18 loop or an operator can run | `playbook.json` (params + ordered `propose_*` steps) | M13 `PendingChange` inbox; M18 autonomy loop | Each step lands in `/pending-changes`; M16 blast-radius attached |
| **Plugin (marketplace)** | A discoverable downstream MCP server (ServiceNow, CMDB, ticketing, PowerShell remediation) | Registry entry → `plugins.json` row | M13.3 `McpPlugins.cs` aggregation | Curated allowlist; downstream writes still gated through the inbox |

All three converge on the same chokepoint: the M13 approval inbox plus the append-only audit
timeline. Nothing in M21 introduces a path that bypasses the human gate
(`ROADMAP.md` lines 406–407: "Propose → human-approve-the-exact-diff → apply. No auto-apply, ever").

---

## Pack model

A **pack** is a versioned M15 desired-state repo plus a top-level `pack.json` **manifest** that
declares identity, target surfaces, and adoption parameters. The repo body is byte-identical to what
`cmpx pull` emits (`ROADMAP.md` line 294), so authoring a pack = exporting a known-good tenant (or a
hand-curated subset) and committing it.

**Adopting a pack** is exactly the M15 round-trip against *your* tenant, parameter-substituted:

1. `POST /packs/{id}/adopt` resolves the manifest's `parameters` against operator-supplied values
   and clones/checks-out the pack repo into a working tree.
2. `JsonDrift.Diff` (the `plan` step) computes the change set between the pack's desired state and
   the live tenant — a Terraform-style preview *before* any write (`ROADMAP.md` lines 296–297).
3. The plan is gated. If M16 is present, a blast-radius report attaches to the gate
   (`ROADMAP.md` lines 305–311). Each object create/update flows through the normal M6 write
   pipeline → snapshot-on-write → audit.

GUID-bearing fields (group ids, filter ids) are resolved through M15's MigrationTable on import, so a
pack authored in one tenant lands correctly in another (`ROADMAP.md` line 295).

### Sample `pack.json`

```json
{
  "schemaVersion": "1.0",
  "id": "cis-windows-l1",
  "name": "CIS Windows 11 — Level 1 Baseline",
  "version": "2.3.0",
  "publisher": "community",
  "description": "Compliance + Settings Catalog config implementing CIS L1 controls.",
  "targetSurfaces": [
    "deviceCompliancePolicies",
    "configurationPolicies",
    "deviceConfigurations"
  ],
  "minSidecar": "13.4.0",
  "parameters": [
    {
      "name": "targetGroupId",
      "type": "groupId",
      "prompt": "Entra group to assign the baseline to",
      "required": true
    },
    {
      "name": "gracePeriodDays",
      "type": "int",
      "default": 7,
      "appliesTo": "deviceCompliancePolicies[*].scheduledActions"
    }
  ],
  "objectCount": 41,
  "signature": null
}
```

`targetSurfaces` mirror the path catalog (`service/Api/Surfaces.cs`, `PLUGINS-MCP.md` line 165), so
an adopt against a tenant whose token can't write a listed surface fails at the gate rather than
silently — the same scope behavior as the inbox (`PLUGINS-MCP.md` lines 116–119). `signature` is
reserved (see Open questions).

---

## Playbook model

A **playbook** is a *parameterized set of proposed writes* — an ordered sequence of `propose_*`
operations, each of which lands as a `PendingChange` in the M13 inbox. Where a pack converges the
tenant onto a *static* desired state, a playbook expresses a *procedure* ("isolate this device,
open a ticket, push the remediation config"), which is exactly what the M18 autonomy loop needs to
*plan* and enqueue (`ROADMAP.md` lines 328–330: "an agent **plans** a remediation → **simulates**
it → enqueues a `propose_*` into the M13 inbox").

`POST /playbooks/{id}/run` binds parameters, then expands each step into a `propose_*` call against
the loopback contract (`PLUGINS-MCP.md` line 91). Each enqueued change is a normal `PendingChange`
(`crates/api-types/src/lib.rs` line 360: `{ id, proposer, kind, path, objectId?, objectName?,
changes, state, createdUtc }`) — so playbook-driven proposals render in the *same* `pending.rs`
inbox with the *same* `drift_row` diff panel as any AI proposal (`PLUGINS-MCP.md` line 179). The
`proposer` field carries `playbook:{id}` for a clean audit trail of "which playbook proposed this."

A step may invoke an aggregated plugin tool (`plugin_<name>_<tool>`, `McpPlugins.cs`) — e.g. open a
ticket via the `ticketing` server — interleaved with native `propose_*` steps. Plugin steps are
pass-through (not inbox-gated) per the M13.3 trust boundary (`PLUGINS-MCP.md` lines 201–203); native
write steps always gate.

### Sample `playbook.json`

```json
{
  "schemaVersion": "1.0",
  "id": "noncompliant-device-isolate",
  "name": "Isolate + ticket a non-compliant device",
  "version": "1.1.0",
  "parameters": [
    { "name": "deviceId", "type": "deviceId", "required": true },
    { "name": "reason", "type": "string", "default": "CIS L1 drift" }
  ],
  "steps": [
    {
      "id": "open-ticket",
      "tool": "plugin_ticketing_create_incident",
      "args": { "title": "Device {{deviceId}} non-compliant", "body": "{{reason}}" },
      "gated": false
    },
    {
      "id": "tag-device",
      "tool": "propose_update",
      "path": "managed-devices",
      "objectId": "{{deviceId}}",
      "body": { "notes": "Quarantined by playbook: {{reason}}" }
    },
    {
      "id": "push-config",
      "tool": "propose_assignments",
      "path": "configurationPolicies",
      "objectId": "remediation-baseline",
      "body": { "assignments": [ { "kind": "group", "groupId": "{{quarantineGroupId}}" } ] },
      "dependsOn": ["tag-device"]
    }
  ]
}
```

Each `propose_*` step computes its diff via `JsonDrift.Diff` and enqueues; the operator approves the
*exact* diff per step (or, post-M13, a batched "approve all from this playbook" — open decision #2,
`PLUGINS-MCP.md` line 151). `dependsOn` orders enqueue; approval order stays the operator's.

---

## Marketplace / plugins

The marketplace is a **discovery layer** over the existing `plugins.json` aggregation. It does not
change how a plugin *runs* — `McpPlugins.cs` already loads http/stdio downstream servers and
namespaces their tools (`PLUGINS-MCP.md` line 174). It adds a **registry**: a curated index of
known-good servers an operator can browse and install with one click, which appends a row to
`plugins.json` (the shape in `docs/plugins.example.json`).

**Trust model = curated allowlist.** `plugins.example.json` (lines 5–7) is explicit: "you curate
this list — plugin tools are pass-through (not gated by the approval inbox), so only add servers you
trust." The marketplace therefore ships a **curated** index (signed/reviewed entries) and an install
flow that surfaces the trust boundary before writing the `plugins.json` row. Reads pass through;
**native writes the plugin triggers (via a playbook `propose_*`) still gate through the inbox** — the
plugin's own side-effects (a ServiceNow ticket) are the operator's trust decision, recorded in
`plugins.json`.

### Sample marketplace registry entry

```json
{
  "id": "servicenow-itsm",
  "name": "ServiceNow ITSM",
  "category": "ticketing",
  "publisher": "cmprojectx-curated",
  "verified": true,
  "description": "Open/update ServiceNow incidents from playbook steps.",
  "transport": "http",
  "urlTemplate": "https://{{instance}}.service-now.com/api/mcp",
  "configFields": [
    { "name": "instance", "type": "string", "required": true },
    { "name": "token", "type": "secret", "header": "Authorization", "format": "Bearer {{token}}" }
  ],
  "exposesTools": ["create_incident", "update_incident", "get_incident"],
  "trust": "writes-side-effecting"
}
```

Installing this entry writes a `plugins.json` row equivalent to the `ticketing` example
(`plugins.example.json` lines 15–21): `{ "name": "servicenow-itsm", "transport": "http", "url": …,
"headers": { "Authorization": "Bearer …" } }`. The aggregated tools then appear as
`plugin_servicenow-itsm_create_incident`, etc.

---

## Contract additions

`contract/openapi.yaml` is the single source of truth; add these surfaces and their DTOs there
first, then mirror into `crates/api-types`. All write-side endpoints terminate in existing
machinery (`/pending-changes`, M15 plan/apply) — none introduce a new write path.

| Method | Path | Effect | Lands in |
|---|---|---|---|
| `GET` | `/packs` | List installed/available packs (manifest metadata) | — |
| `GET` | `/packs/{id}` | Pack manifest + repo summary | — |
| `POST` | `/packs/{id}/adopt` | Bind params → M15 `plan` (JsonDrift) → gated apply | M15 plan; each object → `/pending-changes` |
| `GET` | `/playbooks` | List playbooks (params + step summary) | — |
| `GET` | `/playbooks/{id}` | Full playbook definition | — |
| `POST` | `/playbooks/{id}/run` | Bind params → expand ordered `propose_*` steps | `PendingChange` rows (M13 inbox) |
| `GET` | `/marketplace` | Curated registry index of plugins | — |
| `POST` | `/marketplace/{id}/install` | Append row to `plugins.json`; reload aggregation | `McpPlugins.cs` reload |

New DTOs (camelCase, mirrored byte-for-byte in `crates/api-types`): `PackManifest`,
`PackParameter`, `PackAdoptRequest`, `Playbook`, `PlaybookStep`, `PlaybookRunRequest`,
`MarketplaceEntry`. `POST /playbooks/{id}/run` returns the list of created `PendingChange` ids so the
caller (or the M18 loop) can poll status via the existing `get_change_status` tool
(`PLUGINS-MCP.md` line 169).

---

## Governance

Everything routes through the chokepoints M13/M15/M16 already own — M21 adds no bypass:

- **Approval inbox.** Every native write a pack adopt or playbook run produces is a `PendingChange`
  approved as an exact diff in `pending.rs` (`PLUGINS-MCP.md` lines 96–102). No auto-apply
  (`ROADMAP.md` line 406).
- **Audit.** Proposals, approvals, rejections, and applied writes all land in the audit timeline
  (`PLUGINS-MCP.md` line 122). `proposer` carries the artifact origin (`pack:{id}`, `playbook:{id}`)
  for provenance.
- **Scope-gating.** `PermissionCheckService` knows what the signed-in app registration can write
  (`PLUGINS-MCP.md` lines 116–119); a pack targeting a surface the token can't write fails at the
  gate / approve-replay (403, recorded `failed`), never silently. (Per the honest gap at
  `PLUGINS-MCP.md` lines 197–199, today the gate is approve-replay rather than pre-filtering.)
- **Blast-radius.** When M16 is present, the plan/proposal carries a blast-radius report
  (`ROADMAP.md` lines 310–311) — a pack adopt that locks out 4,200 users shows it before approval.
- **Plugin allowlist.** The marketplace is curated; installation writes the `plugins.json` trust
  decision explicitly (`plugins.example.json` lines 5–7). Plugin reads pass through; side-effecting
  plugin tools are surfaced as such (`trust: "writes-side-effecting"`).

---

## Definition of done

Per `ROADMAP.md` line 402: **an externally-authored policy pack AND a downstream plugin both drive a
gated write end-to-end.** Concretely:

1. Import a community `pack.json` (authored in a *different* tenant), `POST /packs/{id}/adopt` with
   a `targetGroupId` param → M15 plan renders a non-empty diff → operator approves → object created
   + snapshot + audit, GUIDs resolved via MigrationTable.
2. Install a marketplace plugin → `plugins.json` row written → `McpPlugins.cs` re-exposes its tools
   namespaced → a playbook step invokes `plugin_<name>_<tool>` and a native `propose_*` step → the
   native step lands in the inbox and applies on approval.
3. Both flows appear in the audit timeline with `proposer = pack:{id}` / `playbook:{id}`.

---

## Open questions

1. **Pack signing / trust.** The `signature` field is reserved — do we require detached signatures
   (sigstore? GPG?) on community packs, or rely on the Git remote's PR review + curated index? An
   unsigned pack that silently edits 41 objects is a supply-chain surface. (Ties to M15 open
   decision on GitOps remotes, `ROADMAP.md` line 418.)
2. **Playbook idempotency.** Re-running a playbook against an already-converged tenant should be a
   no-op (empty diffs), but plugin side-effects (ticket creation) are *not* naturally idempotent.
   Do steps carry an idempotency key, or does the M18 loop dedupe on `proposer + step.id`?
3. **Marketplace curation.** Who owns the curated index, what's the review bar for `verified: true`,
   and how does an operator add a private/internal entry without it being "curated"? (Tie to the
   M13.3 plugin trust-model open decision, `PLUGINS-MCP.md` lines 153–154.)
4. **Parameter typing & secrets.** `type: "secret"` config fields land in `plugins.json` headers
   today — should the marketplace install flow push secrets to an OS keychain instead of writing
   tokens in cleartext to `%LocalAppData%`?
