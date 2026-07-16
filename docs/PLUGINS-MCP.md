# cmProjectX — Plugins & MCP: operator-driven AI over the tool surface

**Goal.** Let an operator drive cmProjectX from their **preferred AI/tool consumer** (Claude
Desktop, Cursor, Copilot, any MCP client) by publishing cmProjectX's capabilities as an **MCP
server**, and make new capabilities **easy to add as plugins**. Writes the AI proposes are gated
by a human, reusing the M6 safe-write rails.

This is a parallel track to the M-series management spine (see `ROADMAP.md`); call it **M13**.

---

## Why cmProjectX is already 90% there

Two properties we already shipped make this nearly free:

1. **The capability surface is a data-driven catalog.** Every management surface is a row
   (`app/src/features.rs`) and a uniform HTTP contract: `GET/POST/PATCH/DELETE /{path}[/{id}]`,
   plus `/{path}/{id}/assignments`, `/search`, `/drift`, `/audit`, `/preview-diff`, `/snapshots`.
   The per-resource modules (`service/Api/Endpoints/*Endpoints.cs`) already absorb every
   per-surface Graph quirk (Settings Catalog split update, etc.) **behind that uniform contract.**
   → An MCP tool catalog can be generated mechanically from the surface list; tools are thin
   proxies over the existing HTTP contract and never touch a Graph type.

2. **The sidecar already has the AI guardrails.** M6's `/preview-diff` (field-level diff via
   `JsonDrift.Diff`), snapshot-on-write (`/snapshots`), restore, and the audit log are exactly
   the gate an AI-proposed write needs. An AI write becomes a *deferred, gated replay* of the
   same `(verb, path, body)` request the WinUI client would have sent.

The sidecar (127.0.0.1:5099) is the right home: it already owns auth, Graph, drift, snapshot,
audit, and `PermissionCheckService` scope-gating.

---

## Decisions locked (2026-06-10)

| Fork | Choice | Consequence |
|---|---|---|
| **Chat surface** | **Expose-only** — publish an MCP server; operators use their own client. No in-app chat UI to build. | The confirmation gate can't be a chat bubble we control. → the running cmProjectX app becomes the **approval authority** (see Pending-changes inbox). |
| **Plugin model** | **Both layers** — compile-time surface catalog *and* runtime MCP plugins. | Built-in tools = one catalog row (as today). Third-party tools = downstream MCP servers registered in config, re-exposed through one governed endpoint. |
| **Write policy** | **Read-write, human-in-the-loop** — AI proposes; operator approves the exact diff; snapshot + audit always. | Reuses M6 rails verbatim. No auto-apply in M13. |

---

## Architecture

```
  Operator's preferred MCP client (Claude Desktop / Cursor / Copilot)
            │  MCP (Streamable HTTP, 127.0.0.1)
            ▼
  ┌─────────────────────────── cmProjectX sidecar (5099) ───────────────────────────┐
  │  MCP server  (ModelContextProtocol C# SDK · app.MapMcp())                        │
  │     read tools ──────────────► proxy existing GET endpoints ─► data back to AI   │
  │     write tools (propose_*) ─► /preview-diff ─► enqueue PendingChange ─► id+diff │
  │     plugin tools ────────────► proxy downstream MCP servers (plugins.json)       │
  │                                                                                  │
  │  Surface catalog (single source of truth) ─ drives MCP tool generation           │
  │  PendingChange store (new SQLite table) ─ proposer, verb, path, body, diff, state │
  │  On approve: replay (verb,path,body) through the SAME write pipeline + snapshot   │
  └──────────────────────────────────────────────────────────────────────────────────┘
            ▲ GET /pending-changes        ▲ POST /pending-changes/{id}/{approve|reject}
            │                             │
  ┌─────────┴─────────────────────────────┴──────────┐
  │  cmProjectX WinUI app — "Pending AI changes" inbox │  ← reuses the M6 Review-changes diff panel
  └────────────────────────────────────────────────────┘
```

### 1. MCP server in the sidecar
Add the official **`ModelContextProtocol`** C# SDK. Host over **Streamable HTTP** in-process
(`AddMcpServer().WithHttpTransport()`, `app.MapMcp("/mcp")`) so it shares the singleton
`AuthSession` / `ISnapshotStore` / Graph client. Tools are registered **dynamically from the
surface catalog** (not via `[McpServerTool]` attributes), so adding a surface still grows the
tool set with zero handwritten tool code. Bind to 127.0.0.1 only.

### 2. Surface catalog — single source of truth
Today the path→surface mapping lives in two places: `app/src/features.rs` (Rust) and the
endpoint modules (implicit). Introduce a **server-side catalog** (`service/Api/Surfaces.cs`):
`{ path, displayName, writable, assignable, section }` per surface. The MCP generator reads it;
a contract test asserts it stays in lockstep with `features.rs` and the registered routes.

Generated tools per writable surface `/{path}`:
`list_{path}`, `get_{path}`, `propose_create_{path}`, `propose_update_{path}`,
`propose_delete_{path}`, `propose_assign_{path}` (assignable only). Read-only surfaces emit only
`list_`/`get_`. Cross-cutting tools: `search`, `drift`, `audit`, `list_snapshots`, plus the
diagnostics surfaces (`dsregcmd`, `error-db`, …) as read-only tools.

### 3. Read tools — direct proxy
`list_*` / `get_*` / `search` / `drift` call the existing GET handlers in-process and return the
JSON to the AI. Signed-out → the same `409`-as-empty behavior the client relies on.

### 4. Write tools — propose → approve → replay (the HITL gate)
A `propose_*` tool does **not** apply. It:
1. computes the diff via `JsonDrift.Diff` (the `/preview-diff` engine),
2. enqueues a **`PendingChange`** `{ id, proposer, verb, path, id?, bodyJson, diff, createdUtc, state=Pending }`,
3. returns `{ changeId, diff, state: "awaiting_operator_approval" }` to the AI.

A `get_change_status(changeId)` tool lets the AI poll. The running WinUI app shows a **"Pending
AI changes"** inbox (new built-in `Screen`, polled like `/health`): each row renders the diff with
the **existing M6 `drift_row` Review-changes panel**. **Approve** → `POST /pending-changes/{id}/approve`
**replays** the stored `(verb, path, body)` through the same write pipeline used by a human edit
(so per-surface quirks + snapshot-on-write + audit all apply unchanged), then marks the change
applied. **Reject** records the decision. Every proposal + decision is audited with proposer
identity — a clean trail of "what the AI wanted and who let it through."

### 5. Two plugin layers
- **Built-in (compile-time):** one `Surfaces.cs` row → nav (already) + HTTP endpoints (already)
  + MCP tools (generated). Unchanged "one-line to add a feature" ergonomics.
- **Runtime MCP plugins:** a `plugins.json` (in `%LocalAppData%\cmProjectX\`) registers downstream
  MCP servers (a PowerShell-remediation server, ServiceNow, a CMDB, ticketing…). The cmProjectX
  server **aggregates** them — namespaces their tools (`plugin.{name}.{tool}`) and re-exposes them
  through the one governed endpoint, so **third-party writes flow through the same approval inbox +
  audit + scope-gating** as built-ins. (Rationale for aggregating rather than letting the client
  connect to each server directly: centralized governance. The simpler non-aggregating option —
  cmProjectX publishes only its own tools, operator adds other servers in their client — stays
  available as a fallback if aggregation proves heavy.)

### 6. Governance
- **Scope gating:** `PermissionCheckService` already knows what the signed-in app registration can
  write. Omit (or hard-error) `propose_*` tools for surfaces the token can't write — the AI never
  sees a capability the operator lacks.
- **Conditional Access stays read-only** (read-only by design, per ROADMAP) — no `propose_*` tools.
- **Audit:** proposals, approvals, rejections, and applied writes all land in the audit timeline.

---

## New surface area (small)
- **Service:** `ModelContextProtocol` NuGet; `service/Api/Mcp/` (server wiring + dynamic tool
  generator); `service/Api/Surfaces.cs` (catalog); `PendingChange` table in `SnapshotStore`;
  `/pending-changes` GET + approve/reject endpoints; optional `plugins.json` aggregator.
- **Client:** one new built-in `Screen::PendingChanges` + an inbox workspace reusing `drift_row`;
  one `features.rs` row; api_client methods `list_pending / approve_change / reject_change`.
- **Contract:** add the `/pending-changes` + `/mcp` surfaces to `openapi.yaml`.
- **Tests:** catalog ↔ `features.rs` lockstep; propose→approve→replay round-trip on a throwaway
  object; a read-only surface exposes no `propose_*`; scope-gated surface omits writes.

---

## Phased plan (M13)
- **M13.1 — Expose (read-only):** MCP server + surface catalog + read/search/drift/diagnostics
  tools. DoD: a real MCP client lists tools and reads live tenant data. *Ships value alone.*
- **M13.2 — HITL writes:** `propose_*` tools, `PendingChange` store, approval inbox in the app,
  approve=replay. DoD: AI proposes an edit → operator approves the diff → write + snapshot + audit.
- **M13.3 — Runtime plugins:** `plugins.json` aggregation + tool namespacing; route plugin writes
  through the same inbox. DoD: a downstream PowerShell-remediation MCP server's tool runs end-to-end.
- **M13.4 — Polish:** scope-gated tool filtering, per-tool enable/disable in app settings, rate/size
  caps on proposals, packaging note for the MCP endpoint.

## Open decisions (surface before the relevant phase)
1. **MCP transport / discovery.** Streamable HTTP on 5099 vs. a thin stdio shim for clients that
   only spawn stdio servers. *Lean: Streamable HTTP now; add a stdio shim if a target client needs it.* (M13.1)
2. **Approval batching.** Per-change approve vs. "approve all from this session/proposer." *Lean:
   per-change first; batch later.* (M13.2)
3. **Proposal TTL / size caps.** Expire stale pending changes; cap body size. (M13.2)
4. **Plugin trust model.** Allowlist downstream servers in `plugins.json`; whether plugin tools
   require approval even for reads. *Lean: writes always gated; reads pass through.* (M13.3)
5. **Auto-apply scopes.** A future opt-in allowlist of low-risk tools that skip the inbox
   (explicitly out of scope for M13). (post-M13)

---

## Implementation status (shipped)

All four phases landed. Where it lives:

**Service (`service/Api/`)**
- `Surfaces.cs` — server-side surface catalog (mirrors `features.rs`).
- `Loopback.cs` — the shared loopback `HttpClient` the tools + approve-replay use.
- `Mcp/McpTools.cs` — 8 read tools (`list_surfaces`, `list_objects`, `get_object`,
  `get_assignments`, `search`, `audit`, `get_drift`, `get_snapshots`), generated generically.
- `Mcp/McpWriteTools.cs` — 6 write tools (`propose_create/update/delete/assignments`,
  `get_change_status`, `list_pending_changes`).
- `Mcp/McpSetup.cs` — server wiring (`AddCmProjectXMcp` / `MapCmProjectXMcp`),
  `ConfigureSessionOptions` tool population, the `CMPROJECTX_MCP_READONLY` kill switch,
  and `LoadMcpPluginsAsync`.
- `Mcp/McpPlugins.cs` — M13.3 downstream-server aggregation from `plugins.json`.
- `Program.cs` — `/pending-changes` (enqueue / list / status / **approve-replay** / reject),
  the 256 KB proposal cap, `app.MapCmProjectXMcp()`, `await app.LoadMcpPluginsAsync()`.
- `Store/SnapshotStore.cs` — `pending_changes` table + Append/Get/List/SetState.

**Client (`app/src/`)** — `pending.rs` inbox workspace (reuses the M6 `drift_row` panel),
`Builtin::PendingChanges` registry row, `api_client.rs` list/approve/reject, `PendingChange`
DTO in `crates/api-types`.

**Config & contract** — `plugins.json` lives at `%LocalAppData%\cmProjectX\plugins.json`
(see `docs/plugins.example.json`); `contract/openapi.yaml` carries the `/pending-changes`
paths + `PendingChange`/`CreatePendingChange` schemas and a note on `/mcp`.

**Controls (M13.4)** — `CMPROJECTX_MCP_READONLY=1` drops all `propose_*` tools; the 256 KB
body cap rejects oversized proposals at enqueue.

**Verified at runtime** — MCP handshake + `tools/list` (8 read tools) + `list_surfaces`
executes; propose→list→reject with audit trail, and propose→approve→replay (fails gracefully
signed-out, marked `failed`)→`get_change_status` all pass (`.smoke/mcp_smoke.ps1`,
`.smoke/mcp_write_smoke.ps1`).

### Deferred (honest gaps)
- **Permission-scoped tool filtering** (hide `propose_*` for surfaces the token can't write)
  is *not* implemented — the HITL approval is the gate, and a write the token can't perform
  fails at approve-replay (403) and is recorded `failed`. `PermissionCheckService` integration
  is the follow-up.
- **Plugin writes are pass-through**, not routed through the approval inbox (we can't
  generically classify a downstream tool as a write). Trust boundary = the curated
  `plugins.json` allowlist.
- **Proposal TTL** (auto-expire stale pending changes) not yet implemented.
