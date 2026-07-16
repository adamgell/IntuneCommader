# IntuneCommander — Runbook

A concise guide to building, running, and smoke-testing the IntuneCommander app
(Rust/WinUI 3 client + .NET Intune sidecar) against a live tenant this weekend.

For the full flight plan and what is stubbed vs. live, see [`ROADMAP.md`](./ROADMAP.md).

---

## 1. Prerequisites

This is a **Windows-on-ARM64** product (`aarch64-pc-windows-msvc`); treat ARM64 as a
first-class constraint, not an afterthought.

- **Windows 11 on ARM64** (the dev box; the only Rust host/target installed).
- **.NET SDK 10** — the sidecar targets `net10.0` (see `service/Api/Api.csproj`).
- **Rust 1.95 stable** — builds the `app` client crate from the workspace root.
- **Windows App SDK runtime** (2.0.1, installed via winget) — required by the WinUI 3 client.

### Tenant data / sign-in

The hard-forked Core reads the **existing IntuneCommander data** at
`%LocalAppData%\Intune.Commander\`:

- `profiles.json` — saved tenant profiles (Entra app registrations).
- DataProtection `keys\` — `AddIntuneCommanderCore()` decrypts the profile secrets
  transparently (frozen app name `IntuneManager`, purpose `Intune.Commander.Profiles.v1`).

The **active profile is client-secret (app-only)**, so `POST /auth/signin` does a
**silent** token acquisition (no device-code prompt) and flips to `SignedIn` in a few
seconds. Graph access is governed by the app registration's *application* permissions
consented in Entra. If sign-in returns `authState: Failed`, the Entra client secret has
likely expired and must be refreshed in the saved profile.

---

## 2. Build

Both builds are **green today**.

```powershell
# .NET sidecar
dotnet build service/Api/Api.csproj

# Rust client (run from the workspace root)
cargo build -p app
```

---

## 3. Run

```powershell
# 1. Start the sidecar — listens on http://127.0.0.1:5099
dotnet run --project service/Api

# 2. Launch the client (separate terminal)
./target/debug/app.exe
```

Then, in the client:

1. When signed out, the client shows a **full-window sign-in screen**. With the
   client-secret profile, sign-in completes **silently** (no prompt) and the app
   advances to the main shell; the **top bar (upper-right)** then shows the tenant,
   **Sign out**, and **Sync now**. (If a profile uses device-code, the sign-in
   screen surfaces the code + verification URL to enter.)
2. The **LIVE screens populate** — list + view across the Intune management surface.
3. Click **Sync now** (or `POST /sync`) to refresh the **audit / drift** snapshots.

Quick cURL equivalents:

```powershell
curl http://127.0.0.1:5099/health          # liveness + auth state
curl -X POST http://127.0.0.1:5099/auth/signin
curl http://127.0.0.1:5099/apps            # 409 until signed in, then rows
curl -X POST http://127.0.0.1:5099/sync    # refresh audit/drift snapshots
```

---

## 4. What works today

- **Unified grouped navigation** — 9 sections (Overview, Devices, Apps, Enrollment,
  Identity & Access, Tenant Admin, Groups & Monitoring, Drift & Compare, Diagnostics),
  ~68 features.
- **LIVE list + view** across the whole Intune management surface — Devices, Apps,
  Enrollment, Identity, Tenant Admin, Groups (~39 surfaces). Each list projects its
  Graph element into a normalized `{id, title, subtitle, badge}` row; the detail pane
  shows the full object JSON.
- **Full CRUD** (create / edit / delete via the generic JSON editor) on the ~29
  writable surfaces. Read-only surfaces (e.g. Conditional Access, VPP Tokens, Apple
  DEP, Policy Sets, Assignment Filters, Managed Devices, Groups) expose list/view only.
- **Logs** live-tail — the Intune Management Extension (IME) cmtrace-format logs.
- **Diagnostics** — `dsregcmd /status` (device registration) analysis and the
  Windows/Intune **error-code database** lookup.

The full management contract (162 module endpoints) is the source of truth in
[`contract/openapi.yaml`](../contract/openapi.yaml).

---

## 5. Smoke test (5 steps)

1. **Health** — `GET /health` returns **200** (sidecar is up).
2. **Sign in** — the full-window sign-in screen completes silently for client-secret
   profiles (or `POST /auth/signin`); the top bar shows the tenant + **Sign out** / **Sync now**.
3. **Apps populate** — `GET /apps` returns rows (was `409` before sign-in).
4. **Devices › Compliance Policies** — open it from the nav; the list **populates**.
5. **Open a Scope Tag** (Tenant Admin) — the detail pane shows its **JSON**.

If all five pass, the read path end-to-end (auth → Graph → normalize → render) is healthy.

---

## 6. Known limitations (set expectations)

- **Writes hit the LIVE tenant.** There is no sandbox. Test create/edit/delete on
  **low-risk objects** — Scope Tags, Device Categories — not production policy.
- **Deeply-nested object JSON may serialize partially** in the detail/editor view for
  some complex Graph types.
- **Some surfaces are read-only by design** — Conditional Access never exposes write;
  Applications, VPP Tokens, Apple DEP, Cloud PC Provisioning/User Settings, Assignment
  Filters, Policy Sets, Managed Devices, and Groups are list/view-only.
- **ADMX files** are create + delete only (immutable; no update).
- **Diagnostics breadth is still landing** — only dsregcmd, the error-code DB, and the
  IME log live-tail are wired today; the rest of the cmtrace suite is in progress.
