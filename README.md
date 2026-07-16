# IntuneCommander

![Intune Commander](docs/images/logo_small.png)

> A Windows-native control plane for **Microsoft Intune / Entra device management** — a Rust +
> WinUI 3 client over a hard-forked .NET Graph engine, with a persistent, searchable **audit / drift
> time-machine** and a human-gated automation layer on top. **MIT licensed.**

**[intunecommander.com](https://intunecommander.com)** &middot; **[Download the beta](https://github.com/adamgell/IntuneCommander/releases)**

---

## v1.0 is a ground-up rewrite

IntuneCommander started as a **.NET 10 + React** desktop app plus an `ic.exe` CLI (the **`v0.x`**
releases). **v1.0** replaces that shell with a **native Rust / WinUI 3 client** and a **.NET
sidecar** — same brand, same Graph engine — plus a time-machine, a diagnostics suite, and automation
layers the old app never had.

> **Where the code lives:** v1 is being finalized on the
> **[`v1` branch](https://github.com/adamgell/IntuneCommander/tree/v1)** and will become the default
> branch shortly. This `main` branch still holds the legacy v0.x .NET/React app, preserved in the
> `v0.x` tags and this repo's history.

> **Status:** first public beta — **`v1.0.0-beta.1`**. Full CRUD across ~42 surfaces, the
> time-machine, and the diagnostics suite are implemented. Grab a signed build from
> **[Releases](https://github.com/adamgell/IntuneCommander/releases)**.

---

## What it does

- **~42 Intune/Entra surfaces** — a consistent view &rarr; edit &rarr; clone &rarr; delete &rarr;
  assign flow, with a diff-preview + confirm gate before every write (full CRUD on ~29 writable
  surfaces).
- **A time-machine** — audit timeline, field-level drift, full-text search, point-in-time restore.
- **Bulk & lifecycle** — backup/export, gated import/restore, bulk app assignment, CIS/OIB baseline
  compare, Conditional Access &rarr; PowerPoint.
- **Diagnostics** — CMTrace/IME logs, dsregcmd, Event Log, Sysmon, Secure Boot, timeline
  correlation, one-click collector. All local, no sign-in.
- **Platform layer** — GitOps plan/apply, a blast-radius simulator, a queryable tenant digital twin,
  continuous posture scoring, multi-tenant fleet, and Maester (CIS/SCuBA/EIDSCA) checks.
- **Human-gated automation** — proposals land in an approval inbox with the diff + blast-radius;
  nothing auto-applies.

> ⚠️ **Writes hit your live tenant — there is no sandbox.** A diff-preview &rarr; confirm gate
> protects every change, but a confirmed change is real. Start on low-risk surfaces (Scope Tags,
> Device Categories). Conditional Access is read-only by design.

## Architecture

A two-process desktop app: a thin **Rust / WinUI 3** client talks to a local **.NET 10 sidecar** over
loopback. All Graph/Intune coverage lives in the sidecar; the client is data-driven. A single
`contract/openapi.yaml` is the source of truth for the DTOs both sides share, and two storage layers
back it — an append-only audit/drift time-machine (SQLite + Lucene) and a read-through blob cache.

Full architecture, build, and release detail lives in the
[**`v1` branch README**](https://github.com/adamgell/IntuneCommander/tree/v1#architecture).

## Install

Signed installers for **Windows 11** (**ARM64** primary + **x64**) — MSI + MSIX, code-signed with
Azure Trusted Signing — are on the **[Releases](https://github.com/adamgell/IntuneCommander/releases)**
page. Requires the
[Windows App SDK runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads); the
.NET runtime is bundled.

## License

**MIT** — see [`LICENSE`](./LICENSE). The forked Graph engine derives from the MIT-licensed
[IntuneManagement](https://github.com/Micke-K/IntuneManagement) by Micke-K.
