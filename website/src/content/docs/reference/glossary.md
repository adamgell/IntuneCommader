---
title: Glossary
description: Plain-language definitions of the terms used throughout IntuneCommander.
---

**Client** — the Rust/WinUI 3 desktop app (`app/`) you interact with.

**Sidecar** — the local .NET service (`service/`) that owns Microsoft Graph access; the client talks
to it on `http://127.0.0.1:5099`.

**Tenant profile** — a saved Entra app registration (tenant, client id, secret or auth method,
cloud) that IntuneCommander signs in with. Stored at `%LocalAppData%\Intune.Commander\`.

**Entra / Intune** — Microsoft Entra ID (identity) and Microsoft Intune (device management); both are
reached through **Microsoft Graph**.

**Client secret** — an app-only credential on the app registration; sign-in with it is **silent**.

**Device code** — an interactive sign-in where you enter a short code at `microsoft.com/devicelogin`.

**Cloud** — the sovereign cloud a tenant lives in: Commercial, GCC, GCC High, or DoD.

**Surface** — a manageable resource type (Compliance Policies, Applications, Scope Tags, …).

**Assignment** — how a policy or app is targeted to groups, with include/exclude rules, optional
**filters**, and (for apps) an **intent**.

**Snapshot** — a point-in-time copy of an object's full state, stored append-only.

**Time-machine** — the append-only audit/snapshot store that powers history, drift, search, and
restore.

**Drift** — divergence of the current tenant from a baseline or an earlier snapshot.

**Restore** — re-applying a past snapshot to bring an object back to how it was (point-in-time undo).

**Enrich** — attaching live tenant context to a local log line — the core idea behind IntuneCommander.

**dsregcmd** — the Windows command (`dsregcmd /status`) that reports device registration / Entra
join state; IntuneCommander parses it.

**IME** — the Intune Management Extension, whose cmtrace-format logs IntuneCommander can live-tail.

**OIB / CIS** — community/industry security **baselines** (Open Intune Baseline, Center for Internet
Security) you can compare a tenant against.

**ARM64** — the 64-bit Arm architecture; IntuneCommander is a Windows-on-ARM64 product
(`aarch64-pc-windows-msvc`).
