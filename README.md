# IntuneCommander

> A Windows-native control plane for **Microsoft Intune / Entra device management** — a Rust +
> WinUI 3 client over a hard-forked .NET Graph engine, with a persistent, searchable **audit / drift
> time-machine** and a human-gated automation layer on top. **MIT licensed.**

**Version 1.0 is a ground-up rewrite.** IntuneCommander started as a .NET 10 + React desktop app +
`ic.exe` CLI (the `v0.x` releases). v1.0 replaces that shell with a **native Rust/WinUI 3 client** and
a **.NET sidecar** — same brand, same Graph engine (hard-forked into [`service/Core/`](./service/Core/)),
plus the time-machine, diagnostics suite, and automation layers the old app never had. The legacy
.NET/React app is preserved in the `v0.x` tags and this repo's history.

> **Status:** first public beta, **`v1.0.0-beta.1`**. It's a working application — full CRUD across
> ~42 surfaces, the time-machine, the diagnostics suite, and most of the platform layer (device
> actions, GitOps, simulator, twin, posture, fleet, ecosystem) are implemented. Grab a signed build
> from **[Releases](../../releases)**.

> **Codename:** some build files and internal docs still use the codename **`cmProjectX`** (the repo
> directory, `CmProjectX.*` .NET namespaces, `*.slnx`). The product name is **IntuneCommander**.

---

## Download & install

Signed installers (Windows 11, **ARM64** primary + **x64**) are on the
**[Releases](../../releases)** page — MSI + MSIX, code-signed with **Azure Trusted Signing**, built
with **[Master Packager Dev](https://www.masterpackager.com/)**:

- `IntuneCommander-<version>-<arch>.msi` — recommended (Program Files + Start-Menu shortcut)
- `IntuneCommander-<version>-<arch>.msix` — MSIX / App-Installer / managed deployment
- `IntuneCommander-<version>-win-<arch>.zip` — portable (run `Start-IntuneCommander.cmd`)

Requires the **[Windows App SDK runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)**;
the .NET runtime is bundled. Prereqs + a smoke test are in [`docs/RUNBOOK.md`](./docs/RUNBOOK.md).

> ⚠️ **Writes hit your live tenant — there is no sandbox.** A diff-preview → confirm gate protects
> every change, but a confirmed change is real. Start on low-risk surfaces (Scope Tags, Device
> Categories). Conditional Access is read-only by design.

---

## Architecture

A **two-process desktop app**: a thin Rust/WinUI client talks to a local .NET sidecar over loopback.
All Graph/Intune coverage lives in the sidecar; the client is data-driven.

```
  app/  Rust + WinUI 3 (Windows Reactor)  ──HTTP/REST──▶  service/  .NET 10 sidecar
    • Reactor reactive UI (hooks model)     127.0.0.1:5099    • Api/    minimal-API host (~280 routes)
    • api_client.rs (blocking reqwest)                        • Core/   hard-forked Graph engine (~60 services)
    • crates/api-types (shared DTOs)                          • Sync/   Graph delta sync + autonomy watcher
                            contract/openapi.yaml ────────────• Store/  SQLite + Lucene time-machine
                            (one schema → both sides)
```

- **The client owns the sidecar** — `app/src/sidecar.rs::ensure_running()` spawns (or borrows) it on
  launch; `cargo run -p app` boots the whole stack.
- **`contract/openapi.yaml` is the single source of truth** for shared DTOs/endpoints (Rust + C#
  types mirror it byte-for-byte over JSON).
- **Two storage layers:** an append-only **audit/drift time-machine** (`service/Store/`, SQLite +
  Lucene) and a read-through **blob cache** (`service/Api/Endpoints/CachedReader.cs`, LiteDB).
- Loopback-only, single-instance mutex; no inbound HTTP auth — "auth" is per-tenant Entra sign-in
  applied to the outbound Graph client.

See [`AGENTS.md`](./AGENTS.md) (canonical operating guide) and [`CLAUDE.md`](./CLAUDE.md).

## What it does

- **~42 Intune/Entra surfaces** with a consistent view → edit → clone → delete → assign flow
  (diff-preview + confirm before every write; full CRUD on ~29 writable surfaces).
- **A time-machine** — audit timeline, field-level drift, full-text search, point-in-time restore.
- **Bulk & lifecycle** — backup/export, gated import/restore, bulk app assignment, CIS/OIB baseline
  compare, Conditional Access → PowerPoint.
- **Diagnostics** — CMTrace/IME logs, dsregcmd, Event Log, Sysmon, Secure Boot, timeline correlation,
  one-click collector. All local, no sign-in.
- **Platform layer** — GitOps plan/apply, a blast-radius simulator, a queryable tenant digital twin,
  continuous posture scoring, multi-tenant fleet, a policy-pack/playbook ecosystem, and Maester
  (CIS/SCuBA/EIDSCA) checks.
- **Human-gated automation** — proposals land in an approval inbox with the diff + blast-radius;
  nothing auto-applies. (Cross-MDM is an experimental spike.)

## Build & run

Prereqs (see [`docs/RUNBOOK.md`](./docs/RUNBOOK.md)): **Windows 11 on ARM64** (primary), the **.NET 10
SDK**, **Rust stable**, the **Windows App SDK runtime**, and the two sibling checkouts the client's
path-deps need — `../windows-rs` (Windows Reactor fork) and `../cmtraceopen` (parser).

```powershell
cargo build --workspace
dotnet build service/CmProjectX.slnx --configuration Release
cargo run -p app          # client spawns the sidecar
```

## Releasing

Push a `v<semver>` tag → the **[signed-release workflow](./.github/workflows/cmprojectx-codesign.yml)**
builds the client + self-contained sidecar (+ bundled Maester), packages a **signed MSI + MSIX** per
arch with mpdev (Azure Trusted Signing), and publishes a draft release here:

```powershell
git tag v1.0.0-beta.1 && git push origin v1.0.0-beta.1
```

Signing config lives in the `codesigning` environment secrets. Packaging is defined by
[`packaging/installer.mpdev.json`](./packaging/installer.mpdev.json) (see
[`packaging/README.md`](./packaging/README.md)). `scripts/release.ps1` is a local unsigned dev build.

## License

**MIT** — see [`LICENSE`](./LICENSE). Bundled third-party components are listed in
[`THIRD-PARTY-NOTICES.md`](./THIRD-PARTY-NOTICES.md). The forked `service/Core/` derives from the
MIT-licensed original IntuneCommander.
