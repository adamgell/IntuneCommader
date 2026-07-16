---
title: Cutting a release
description: How IntuneCommander releases are built and published — the repo layout and the local release script.
---

Releases are cut **locally** with a single PowerShell script. There is no GitHub Actions release
workflow; the logic lives in one place a CI runner could also call.

## Repo layout

IntuneCommander is a single public repository —
[`adamgell/IntuneCommander`](https://github.com/adamgell/IntuneCommander) — holding the source, these
docs, and the published binaries on its [Releases](https://github.com/adamgell/IntuneCommander/releases)
page (the [release channel](/get-started/download/)).

## The release script

[`scripts/release.ps1`](https://github.com/adamgell/IntuneCommander/blob/main/scripts/release.ps1)
(PowerShell 7+) is the "go script".

```powershell
# prerelease, x64 (the current target):
.\scripts\release.ps1 -Version 0.2.0 -Arch x64

# build + package only, no publish (artifacts land in .\dist):
.\scripts\release.ps1 -Version 0.2.0 -Arch x64 -DryRun

# stable (non-prerelease) release:
.\scripts\release.ps1 -Version 1.0.0 -Arch x64 -Stable
```

For each architecture it:

1. builds the Rust/WinUI client + `dotnet publish`es the sidecar (self-contained, no `.pdb`);
2. stages a runnable bundle (`IntuneCommander.exe`, `sidecar\`, `docs\`, `Start-IntuneCommander.cmd`, `VERSION`) and zips it;
3. publishes a GitHub Release `v<Version>` on this repo with the zips attached
   (`--prerelease` unless `-Stable`).

## Prerequisites

- Run from a **full checkout**, not a git worktree — the Rust client has path dependencies on the
  sibling `windows-rs` and `cmtraceopen` checkouts, which must sit next to the repo root.
- An **x64 MSVC build environment** (Visual Studio Build Tools 2022 — enter the dev shell before
  running) and the **.NET 10 preview SDK**.
- An authenticated **`gh`** CLI.

The script preflights all of this and stops with a clear message if something's missing.

:::note[x64 only, for now]
Builds currently target **win-x64**. ARM64 is the long-term primary target but needs the
`aarch64-pc-windows-msvc` Rust target and the ARM64 VC tools installed first.
:::

Key flags: `-Arch x64`, `-SelfContained`, `-Notes <text|file>`, `-Stable`, `-DryRun`, `-Force`.
