---
title: Cutting a release
description: How IntuneCommander releases are built, signed, and published — the tag-driven CI workflow, plus the local unsigned dev build.
---

Official releases are cut by **CI**: push a `v<semver>` tag and the signed-release workflow builds,
signs, and attaches the installers to a draft GitHub Release. A local script covers quick unsigned
dev builds.

## Repo layout

IntuneCommander is a single public repository —
[`adamgell/IntuneCommander`](https://github.com/adamgell/IntuneCommander) — holding the source, these
docs, and the published binaries on its [Releases](https://github.com/adamgell/IntuneCommander/releases)
page (the [release channel](/get-started/download/)).

## Cut a signed release (CI)

The [**Signed Release (MSI + MSIX)**](https://github.com/adamgell/IntuneCommander/blob/main/.github/workflows/cmprojectx-codesign.yml)
workflow is the source of truth. It runs on a version tag and produces a **signed MSI, a signed
MSIX, and a portable zip for both `arm64` and `x64`**.

1. **Match the version.** Set `[package] version` in `app/Cargo.toml` to the release version (no
   leading `v`). CI fails fast if the tag and `Cargo.toml` disagree.
2. **Tag and push.**

   ```powershell
   git tag v1.0.0-beta.1
   git push origin v1.0.0-beta.1
   ```

   Or run it manually from **Actions → Signed Release → Run workflow** with a version — the workflow
   creates the tag for you.
3. **CI builds and signs.** For each architecture it builds the Rust/WinUI client, publishes the
   self-contained .NET sidecar, bundles the pinned Maester modules, then packages a **signed MSI +
   MSIX** with Master Packager Dev (Azure Trusted Signing) plus a portable zip. Build provenance is
   attested.
4. **Publish the draft.** CI attaches every asset to a **draft** GitHub Release — a `-beta.N` tag is
   auto-marked *prerelease*. Review it and hit **Publish**.

:::note[Release notes]
Drop curated notes at `docs/release-notes/<tag>.md` (for example `docs/release-notes/v1.0.0-beta.1.md`)
and CI uses them for the release body; otherwise it writes a one-line fallback.
:::

:::caution[Signing & versions]
Signing runs in the `codesigning` GitHub Environment. The Azure Trusted Signing identifiers are
non-secret **variables**; only the client secret (and the optional `SYNCFUSION_LICENSE_KEY` for the
Conditional Access → PowerPoint export) are secrets. Installer versions are 4-part numeric derived
from the SemVer, so each beta cleanly upgrades the last — see
[`packaging/README.md`](https://github.com/adamgell/IntuneCommander/blob/main/packaging/README.md).
:::

## Local dev build (unsigned)

For a quick local build without CI or signing,
[`scripts/release.ps1`](https://github.com/adamgell/IntuneCommander/blob/main/scripts/release.ps1)
(PowerShell 7+) builds the client + self-contained sidecar and stages a runnable, zipped bundle per
architecture. Use `-DryRun` to keep the artifacts local — they land in `.\dist` and nothing is
published:

```powershell
# both arches, unsigned, artifacts only (no GitHub release):
.\scripts\release.ps1 -Version 1.0.0-beta.1 -DryRun
```

These builds are **unsigned** — use them for testing, not distribution. Signed, published releases
always go through the CI workflow above.

:::note[Run from a full checkout]
The Rust client has path dependencies on sibling `windows-rs` and `cmtraceopen` checkouts, which
must sit next to the repo root — so build from a **full checkout, not a git worktree** (CI links
these in automatically). You'll also need the **.NET 10 SDK** and the Rust toolchain with the
`aarch64-pc-windows-msvc` / `x86_64-pc-windows-msvc` targets installed.
:::
