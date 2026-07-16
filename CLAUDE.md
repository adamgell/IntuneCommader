# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Operating guide

[AGENTS.md](./AGENTS.md) is the canonical operating guide — read it first. Its project rules are non-negotiable and not repeated here. The most load-bearing ones:

- `contract/openapi.yaml` is the **single source of truth** for shared DTOs and endpoints. Change the contract first when the API surface changes; don't grow the hand-written Rust/C# DTOs in parallel.
- Don't reimplement the Intune/Graph engine in Rust. The architecture is a Rust client talking to a local .NET sidecar over REST. Graph coverage lives in `service/Core/` (a hard-forked Intune Commander core) — reuse it.
- `service/Store/` is **append-only by design**. Preserve that model.
- The CMTrace parser (`cmtraceopen-parser`) is an **external upstream dependency**, not vendored. Fixes go upstream; this repo changes only the pin or integration code.

## The big picture

A two-process desktop app for **Microsoft Intune / Entra device management**, with a persistent, searchable **audit/drift time-machine**:

```
app/  Rust + WinUI 3 (Windows Reactor)  ──HTTP/REST──▶  service/  .NET 10 sidecar
  • Reactor reactive UI (hooks model)        :5099        • Api/    minimal-API host
  • api_client.rs → 127.0.0.1:5099                        • Core/   hard-forked Graph engine
  • crates/api-types  (shared DTOs)                       • Sync/   Graph delta sync
                          contract/openapi.yaml ──────────• Store/  SQLite + Lucene time-machine
                          (one schema → both sides)
```

The client is **thin**: nearly every screen is data-driven. `app/src/features.rs` is the registry that drives both the left-nav and workspace routing, so adding a LIVE list screen is roughly one registry entry plus one match arm. The API client (`app/src/api_client.rs`) is a thin blocking-`reqwest` wrapper; the server projects rich Graph types down into normalized DTOs (`ListItem`, `Assignment`, etc.) shared via `crates/api-types`.

The server is a **minimal API**: each feature is a `Map*()` extension under `service/Api/Endpoints/`, wired in `service/Api/Program.cs`. `service/Api/Mappers/` projects `Microsoft.Graph.Beta.Models` types → normalized DTOs in `service/Api/Contracts.cs` (camelCase, mirrors the Rust types byte-for-byte over JSON). Graph reads run through Core services (`service/Core/Services/`), the hard-forked engine.

**Two storage layers, don't conflate them.** (1) `service/Store/SnapshotStore.cs` is the **append-only audit/drift time-machine** — config snapshots + audit events in SQLite with a Lucene full-text index (deduped by content hash; delta watermarks in a `sync_state` table). (2) A **read-through blob cache** (M12.1) sits in front of every LIST/DETAIL Graph fetch: `Endpoints/CachedReader.cs` wraps each Core fetch as a delegate, backed by Core's LiteDB `ICacheService`, tenant-scoped by `{tenantId}|{dataType}` key; `Endpoints/CacheInvalidation.cs` evicts on writes. Both live under `%LocalAppData%\cmProjectX\`. See `docs/CACHE.md` / `docs/CACHE-M12.1.md`.

Status: a working skeleton, unevenly filled in. `service/Core/` is **not** a stub — it's a substantial hard-forked Intune Commander engine (~100 Graph service classes). Thinner/stubbed seams remain in `service/Sync/` and some UI screens. Ignore stubs unless the task is to flesh them out.

## Commands

Rust (workspace = `app` + `crates/api-types`; run from repo root):

```powershell
cargo build                 # whole workspace
cargo build --workspace     # what CI runs
cargo test --workspace      # what CI runs (few/no Rust tests yet)
cargo run -p app            # launch the client (expects the sidecar already running)
```

.NET (run from repo root; targets `net10.0`, SDK pinned by `service/global.json`):

```powershell
dotnet build service/CmProjectX.slnx --configuration Release   # what CI runs
dotnet run --project service/Api/Api.csproj                    # start the sidecar on :5099
dotnet test service/Api.Tests/Api.Tests.csproj                 # integration tests (xUnit) — see caveat
```

Run a single .NET test (xUnit, filter on the fully-qualified name or method):

```powershell
dotnet test service/Api.Tests/Api.Tests.csproj --filter "FullyQualifiedName~ListEndpointTests"
```

> **Test caveat:** `Api.Tests` are **black-box integration tests**, not hermetic unit tests. The fixture (`SidecarFixture.cs`) reuses a running sidecar on `127.0.0.1:5099` (or spawns `Api.dll`), signs in once, and hits live endpoints against a real tenant. They need a valid, non-expired Entra client secret and network — they will not pass offline.

## Running the full stack

**The client owns the sidecar.** `app/src/sidecar.rs::ensure_running()` checks `127.0.0.1:5099/health` on launch: if a sidecar is already up it borrows it, otherwise it spawns one (bundled `Api.exe`, else `dotnet …/Api.dll` in dev) and kills it on exit. So `cargo run -p app` alone now boots the whole stack — there's no "start the sidecar first" step.

For active service work, still run the two separately so you can see sidecar logs (the app routes the child's stdout/stderr to NULL):

```powershell
dotnet run --project service/Api/Api.csproj   # terminal 1 — wait for :5099, watch its logs
cargo run -p app                              # terminal 2 — borrows the running sidecar
```

`Program.cs` holds a **single-instance mutex** guarding port 5099 and the store — only one sidecar can run at a time. If a launch fails to bind, kill the stale sidecar / free 5099 before retrying (an app-spawned sidecar can outlive a crashed client). Smoke-test steps (health → sign-in → list populates → detail pane) are in [docs/RUNBOOK.md](./docs/RUNBOOK.md).

## Things that will bite you

- **Windows ARM64 first.** The dev box and primary release target are `aarch64-pc-windows-msvc` / `win-arm64`; the codesign workflow also ships x64. Reactor/WinUI links against the installed Windows App SDK (framework-dependent, not bundled).
- **`app/` is edition 2024**, deliberately not the workspace's 2021 — the Windows Reactor DSL is authored against 2024. Don't "fix" the mismatch.
- **Windows Reactor is a pinned git dep with a local fork patch.** `[patch]` in the root `Cargo.toml` redirects `windows-reactor`/`windows-reactor-setup` to a sibling `../windows-rs/` checkout carrying a nested-dirty-reconcile fix. Without it, state-driven child components under a structurally-stable ancestor (e.g. under `NavigationView`) never re-render, and async-fetch results (`use_resource`/`use_mutation`) update state but don't repaint. If a Reactor child won't update on its own state, lift the state to the dispatched parent.
- **Never run two `dotnet build` of the same project concurrently** — the second wedges on the build-server nodes (looks hung, empty output). Let one finish first.

## Where things live

| Path | What |
|---|---|
| `app/src/main.rs` | NavigationView shell, root `/health` poll (auth + sync state), workspace dispatch |
| `app/src/features.rs` | Feature registry — single source of truth for nav + routing |
| `app/src/api_client.rs` | Blocking HTTP client → `127.0.0.1:5099` |
| `app/src/sidecar.rs` | Spawns/borrows + owns the .NET sidecar process (`ensure_running()`) |
| `app/src/signin.rs`, `assignments.rs`, `config_view.rs`, `bulk.rs`, `screen_*.rs`, `diag_*.rs` | Workspace implementations (`diag_*` are pure-Rust local diagnostics, no sidecar) |
| `crates/api-types/src/lib.rs` | Shared DTOs mirroring the contract |
| `service/Api/Program.cs` | DI, auth bootstrap, endpoint registration, single-instance guard |
| `service/Api/Endpoints/`, `service/Api/Mappers/`, `service/Api/Contracts.cs` | Endpoint modules, Graph→DTO mappers, DTOs |
| `service/Api/Endpoints/CachedReader.cs`, `CacheInvalidation.cs` | M12.1 read-through blob cache (LiteDB) + write-driven eviction |
| `service/Api/AuthSession.cs` | Sign-in state machine + tenant-profile lifecycle |
| `service/Core/Services/` | Hard-forked Graph engine (~100 `*Service.cs` + `I*` interfaces) — reuse, don't reimplement |
| `service/Store/SnapshotStore.cs` | Append-only SQLite + Lucene time-machine (distinct from the blob cache) |
| `contract/openapi.yaml` | Canonical API contract |
| `docs/` | `MVP-PLAN.md` (thesis/decisions), `ROADMAP.md` (M1–M13 milestones), `RUNBOOK.md` (prereqs + smoke test), `CACHE*.md` (M12 cache), `PLUGINS-MCP.md` (M13 MCP/HITL track) |
| `website/` | Astro + Starlight docs site (`npm run dev` / `build`); deployed by `cmprojectx-docs.yml` |
