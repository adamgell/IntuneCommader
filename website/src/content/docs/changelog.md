---
title: Changelog
description: Notable changes to IntuneCommander, newest first.
---

Notable changes to IntuneCommander, newest first. Versions follow
[semantic versioning](https://semver.org); pre-`1.0` builds are prereleases.

## v0.1.0 — 2026-06-21

_Prerelease._ First published build.

- First downloadable release: **win-x64**, with a self-contained .NET sidecar (no separate .NET
  install needed). See [Download a release](/get-started/download/).
- Two-process desktop app — Rust/WinUI client + local sidecar on `127.0.0.1:5099` — with the
  audit/drift [time-machine](/using/time-machine/).
- Bundled launcher (`Start-IntuneCommander.cmd`) starts the sidecar, then the client.

[Release notes ›](https://github.com/gellorg/intunecommander-release/releases/tag/v0.1.0)
