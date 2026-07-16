---
title: Architecture
description: How IntuneCommander is put together — a Rust/WinUI client talking to a local .NET Intune sidecar, with one OpenAPI contract driving both.
---

IntuneCommander is two processes that talk over local HTTP, with a single API contract shared between them.

```
  ┌─────────────────────────────┐        ┌──────────────────────────────────┐
  │  app/  — Rust + WinUI 3      │  HTTP  │  service/  — .NET Intune engine  │
  │  • Reactor UI                │ ◄────► │  • Core/   Graph services (fork) │
  │  • api_client → sidecar      │  :5099 │  • Sync/   Graph delta sync      │
  │  • CMTrace parser (diag)     │  REST  │  • Store/  append-only + search  │
  └─────────────────────────────┘        │  • Api/    ASP.NET minimal host  │
                                          └──────────────┬───────────────────┘
                      contract/openapi.yaml ─────────────┘  (one schema → both sides)
```

## The two halves

- **Client (`app/`)** — a Windows-native Rust/WinUI 3 app. It renders the UI and the local
  diagnostics (the cmtrace parser), and calls the sidecar for everything tenant-related.
- **Sidecar (`service/`)** — a .NET app that owns all Microsoft Graph access:
  - **Core** — the hard-forked Intune management engine (Graph services + multi-cloud auth).
  - **Sync** — Graph **delta** sync.
  - **Store** — the **append-only** audit/config snapshot store plus a full-text index (the
    [time-machine](/using/time-machine/)).
  - **Api** — a thin ASP.NET host that exposes it all over REST on `http://127.0.0.1:5099`.

## One contract, both languages

[`contract/openapi.yaml`](/reference/api/) is the **single source of truth** for the DTOs and
endpoints. Both the C# sidecar and the Rust client's types are generated from it, so the two sides
can't drift apart. If the API surface changes, the contract changes first.

:::note[Why a sidecar?]
The Intune/Graph engine is large and battle-tested in .NET. Rather than re-implement it in Rust,
IntuneCommander runs it as a local sidecar and keeps the client thin — Rust for the native Windows UI and
diagnostics, .NET for the cloud engine.
:::
