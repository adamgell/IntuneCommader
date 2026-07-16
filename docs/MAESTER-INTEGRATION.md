# Maester ↔ cmProjectX — Integration Review

**Status:** Implemented — Direction A1 + A2 landed (see §7); this doc also carries the original review/decision rationale.
**Subject:** Adding part or all of [Maester](https://github.com/maester365/maester) to cmProjectX (this repo), and the reverse contribution direction.
**TL;DR:** A *full* merge in either direction is not viable — the runtimes and the product theses don't line up. The high-value move is a **thin, partial integration**: run Maester as an external check-runner *inside* cmProjectX (sharing our existing auth token), surface its results as a Compliance/Posture screen, and — the part neither tool has alone — **snapshot Maester results into the time-machine so you get posture drift over time**. Giving back to Maester is a separate, smaller effort: contribute Intune `Test-Mt*` functions derived from our embedded baselines.

---

## 1. What each tool is

| | **cmProjectX (this repo)** | **Maester** |
|---|---|---|
| Purpose | Interactive Intune/Entra **management** + persistent **audit/drift time-machine** | **Security-as-code testing** — continuously validate M365 config |
| Runtime | .NET 10 sidecar + Rust/WinUI 3 client | PowerShell 7 + Pester |
| Graph access | Hard-forked Intune Commander engine (~100 `*Service.cs`, `Microsoft.Graph.Beta`) | `Microsoft.Graph` PowerShell SDK via `Invoke-MtGraphRequest` |
| Write? | **Yes** — CRUD across ~27 surfaces, assignments, bulk import/export | **No** — read-only assessment by design |
| State | Append-only SQLite + Lucene **time-machine**; LiteDB blob cache | **Stateless** — each run is a fresh report |
| Output | Reactive desktop UI, drift diffs, restore/undo | HTML/JSON/MD/CSV/Excel report, CI-friendly |
| Test corpus | Embedded OIB/CIS baselines (Settings Catalog, Endpoint Security, Compliance) + a 0–100 posture score | **280+ Pester tests** aligned to EIDSCA / CISA SCuBA / CIS M365 / ORCA |
| Distribution | Self-contained MSIX (arm64 + x64) | PowerShell Gallery module + GitHub Action |

**The thesis difference is the whole story.** Maester is a *stateless, scheduled, read-only* posture scanner you wire into CI. cmProjectX is a *stateful, interactive, write-capable* console whose differentiator is remembering history. They overlap on "read Intune config from Graph and judge it against a baseline," and they are complementary everywhere else.

## 2. Where they overlap vs. complement

**Overlap (don't rebuild):**
- Both query Graph for Intune configuration.
- Both compare config to a security baseline: cmProjectX via `BaselineService` (embedded OIB/CIS, `service/Core/Services/`) + `SecurityPostureEndpoints` (0–100 score); Maester via its CIS/SCuBA/EIDSCA test set.

**Complement (the actual opportunity):**
- Maester → us: a **large, community-maintained, framework-aligned** test corpus we would never want to hand-port, plus report formats and CI muscle.
- Us → Maester: **history**. Maester deliberately keeps no state; our time-machine turns its pass/fail into a trend ("MT.1054 regressed on 2026-06-12, here's the config snapshot from that day"). Also write-back / remediation, which Maester will never do.

## 3. Maester's extension model (why "thin" is realistic)

A Maester Intune check (e.g. `powershell/public/maester/intune/Test-MtMdmAuthority.ps1`) is a small, self-contained function:

1. `Get-MtLicenseInformation` — gate on licensing.
2. `Invoke-MtGraphRequest 'organization/{id}?$select=…'` — query Graph (uses the connection from `Connect-Maester`).
3. Return `$true` / `$false` / `$null` and call `Add-MtTestResultDetail` for the report.

`Invoke-Maester` runs the `.Tests.ps1` Pester wrappers and emits a structured **JSON** result. Two consequences:
- **We don't have to embed Maester's internals** — shelling out to `Invoke-Maester -OutputJson` and parsing the result is a clean seam.
- **Auth is the only real coupling point.** Maester connects via `Connect-MgGraph`. Our `AuthSession` (`service/Api/AuthSession.cs`) already holds a live `TokenCredential` + `Scopes` for the active profile (Commercial/GCC/GCCHigh/DoD; Interactive/DeviceCode/ClientSecret). We can mint a raw token with `credential.GetTokenAsync(new TokenRequestContext(scopes))` and pass it to `Connect-MgGraph -AccessToken`, so Maester runs as the *same* signed-in identity with no second sign-in.

## 4. Options

### Direction A — bring Maester *into* cmProjectX (recommended)

**A1 — Maester as an external check-runner (the foundation).**
The sidecar shells out to PowerShell 7, runs `Connect-Maester` with our minted access token + `Invoke-Maester -OutputJson`, parses results, and exposes them via a new endpoint group (`POST /maester/run`, `GET /maester/results`). A new data-driven screen in the Rust client (`app/src/features.rs` + one match arm) lists tests, severities, pass/fail, and remediation hints.
- *Pros:* reuses 280+ tests verbatim; no reimplementation; aligns with the "don't reimplement the Graph engine" rule; CIS/SCuBA/EIDSCA alignment for free.
- *Cons:* introduces a **PowerShell 7 + Maester module + Microsoft.Graph PowerShell SDK** runtime dependency the MSIX must carry or bootstrap (heavy; see §5). Cross-runtime error/version handling.
- *Effort:* Medium.

**A2 — Snapshot Maester results into the time-machine (the differentiator).**
Persist each `Invoke-Maester` run into `SnapshotStore` (append-only, dedup-by-hash already exists). Now posture findings get the same treatment config already gets: timeline, full-text search, and **drift** ("which checks changed since last week, and what config change caused it"). This is the part **neither tool can do alone** and it's a small delta on top of A1 — the store, the diff engine (`JsonDrift.cs`), and the UI patterns already exist.
- *Effort:* Small once A1 lands.

**A3 — Reimplement Maester checks natively in .NET. Not recommended.** Throws away the community corpus, creates a permanent maintenance tax to track upstream, and contradicts the repo's reuse ethos.

### Direction B — contribute *to* Maester

**B1 — Contribute Intune `Test-Mt*` functions from our baselines.** Our embedded OIB/CIS baseline knowledge (`BaselineService`) is reusable IP; rewriting select checks as PowerShell/Pester functions is good open-source citizenship and raises the project's profile. Independent of Direction A; medium effort per batch of checks.

**B2 — Push the time-machine concept upstream. Not recommended.** Maester is intentionally stateless and CI-oriented; durable history is a different product and would likely be declined.

**"Full" merge either way — not viable.** PowerShell vs .NET+Rust, read-only vs write-capable, stateless vs stateful. It would be a rewrite, not an integration.

## 5. Risks & constraints to weigh

- **Runtime weight (the biggest cost of Direction A).** Today we ship a self-contained MSIX. Maester needs PowerShell 7 + the Maester module + the Microsoft.Graph PowerShell SDK. Options: (a) bundle a portable PS7 + modules (large, but self-contained stays true); (b) detect/bootstrap on first use (smaller package, online dependency, version drift). Decide before committing to A1.
- **Auth scopes.** Maester's full test set spans Entra/Exchange/Defender/Intune and wants broad read scopes; our app registrations are Intune-management-scoped. Either restrict to Maester's **Intune** tests, or broaden the requested scopes (and update `PermissionCheckService`).
- **Multi-cloud.** `Connect-Maester`/`Connect-MgGraph` must be pointed at the right national cloud to match our active `TenantProfile.Cloud`.
- **Licensing.** Confirm Maester's license permits redistribution inside a packaged app (Direction A). Direction B (contributing upstream) sidesteps this.
- **Append-only contract.** Maester results go into `SnapshotStore` as new append-only records — same model as config snapshots. Don't bolt mutable result state onto the store.

## 6. Recommendation

1. **Do Direction A1 + A2.** Embed Maester as an external Intune check-runner sharing our existing token, then snapshot its results into the time-machine for **posture-drift over time** — a feature neither product has on its own. Scope the first cut to Maester's **Intune** test set to keep auth scopes and runtime footprint contained.
2. **Resolve the PowerShell-runtime packaging question first** (§5) — it gates whether A1 is a feature or a distribution headache.
3. **Optionally do B1 in parallel** — contribute baseline-derived Intune `Test-Mt*` functions upstream as good citizenship; it's independent of A.
4. **Skip A3, B2, and any "full" merge.**

A natural home for this is a new roadmap milestone alongside the M13 MCP/HITL track (`docs/PLUGINS-MCP.md`), since both are about feeding the engine with external, governed signal.

---

## 7. Implementation (landed — A1 + A2)

The first slice of Direction A is implemented as a thin vertical slice across all four
layers, contract-first.

**Contract** (`contract/openapi.yaml`): `GET /maester/status`, `POST /maester/run`,
`GET /maester/results` + schemas `MaesterStatus`, `MaesterRunRequest`,
`MaesterRunResult`, `MaesterCategory`, `MaesterControl`.

**Sidecar** (`service/Api/Endpoints/MaesterEndpoints.cs`):
- `MaesterRunner.ProbeAsync` shells out to `pwsh` to check PowerShell 7 + the Maester
  module are present (→ `/maester/status`; `available:false` with guidance when not).
- `MaesterRunner.RunAsync` mints a token from the live `AuthSession.Credential` (reusing
  the session's scopes so the audience matches the active national cloud), passes it to
  `Connect-MgGraph -AccessToken` via an **env var** (never on the command line), runs
  `Invoke-Maester -OutputJsonFile`, and projects the JSON into normalized DTOs.
- `Categorize` buckets each control into Entra / Intune / Exchange / Defender / Teams /
  SharePoint / Other by Pester tags then the Describe block.
- **A2:** each run is appended to the append-only time-machine
  (`AppendSnapshotIfChangedAsync`, object id `maester-run|{tenantId}`) with `ExecutedAt`
  stripped so identical posture dedups by content hash — a new snapshot (and the `/drift`
  signal) appears only when a control's result actually changed. An audit event records
  the run. `/maester/results` returns the latest stored run.
- The pure `Parse` / `Categorize` helpers are unit-tested offline in
  `service/Api.Tests.Unit/MaesterResultTests.cs` (no PowerShell needed).

**Client** (`app/src/screen_maester.rs`, `features.rs`, `api_client.rs`): a new
**Security & Compliance** nav section with **All / Entra / Intune Controls** screens
(category-filtered views of the same run) — a category roll-up with scores plus the
per-control pass/fail list and a "Run checks now" action.

### Known limitations / follow-ups
- **Packaging (modules bundled, pinned; pwsh 7 required).** `scripts/release.ps1` bundles
  Maester + Microsoft.Graph.Authentication + Pester **plus the service modules**
  ExchangeOnlineManagement, MicrosoftTeams and PnP.PowerShell (~270 MB/arch, whole/untrimmed)
  into `sidecar/psmodules`, at **pinned versions** so a release is reproducible (bump the
  `$pinnedModules` list deliberately after re-testing). Downloaded once and copied into each
  arch (the service modules ship all-arch native runtimes in one package); the PSGallery infra
  modules Save-Module drags in (PackageManagement/PowerShellGet) are dropped. The sidecar
  prepends `psmodules` to `PSModulePath` (`MaesterRunner.BundledModulePath`) so runs — now
  including **Exchange / Teams / SharePoint / EOP** — work **offline**, no PSGallery at runtime
  (important in locked-down / national-cloud tenants). Pester is held at **5.x** (Maester 2.x
  targets Pester 5; 6.0 is a ground-up rewrite). On a clean/offline target the bundled versions
  are the only ones present, so the pin holds; a target that *already* has newer modules
  installed may load those instead (module resolution prefers the highest version). PowerShell 7
  itself is **not** bundled: the target must have `pwsh` on PATH, or `/maester/status` reports
  unavailable and `/maester/run` returns 501 with an install hint. (Bundling a portable PS7 was
  the heavier alternative; deferred.)
- **Scopes — broadened.** The run mints its token with an explicit delegated READ scope set
  (Directory/Policy/Reports/RoleManagement/PrivilegedAccess/AuthMethod/Application/Domain/
  DeviceManagement*/SecurityEvents/IdentityRiskyUser/AuditLog `.Read.All`) instead of the
  Intune-scoped `.default`, so an interactive sign-in incrementally consents them and far more
  Entra/Intune/Defender controls run (live-validated on Ivy24: Entra 37→77 passed, errors 14→6).
  Falls back to session `.default` if the broad acquisition fails.
- **Non-Graph services — connected.** The driver now mints a per-audience token from the same
  session and runs `Connect-ExchangeOnline`, `Connect-IPPSSession` (Security & Compliance / EOP),
  `Connect-MicrosoftTeams` and `Connect-PnPOnline` before the suite, so Exchange / EOP / Teams /
  SharePoint controls actually run instead of Skipping (live-validated on Ivy24). Each connect is
  best-effort — a failure is recorded to the run diagnostics and only that service's tests Skip;
  the run always proceeds. The modules those cmdlets come from are bundled (see packaging, above).
- **Async run — done.** `POST /maester/run` is now fire-and-forget (mirrors `POST /sync`):
  it probes prerequisites synchronously (501 up front), then runs on a background task
  (`MaesterRunTracker`) and returns `202` immediately. The client polls the cheap in-memory
  `GET /maester/run/status` (`Idle|Running|Completed|Failed`) and keys the results fetch on
  the run's `finishedAt`, so a completed pass — even one triggered out-of-band — auto-refreshes
  the screen. The old 20-min client timeout is gone.
- **Exchange / Teams / SharePoint — working (needs app-registration permissions).** Maester's
  non-Graph tests reach these through separate modules and *different token audiences*. The driver
  now mints a **per-service token** for each audience from the session credential and connects
  best-effort — `Connect-ExchangeOnline` (audience `outlook.office365.com`),
  `Connect-MicrosoftTeams` (Skype/Teams admin API `48ac35b8-…`), `Connect-PnPOnline` (SPO admin
  host) — each recorded to a diagnostics file surfaced in the sidecar log, alongside Maester's own
  `Test-MtConnection` verdict. A failed connect is swallowed (that service just Skips). **The gate
  is the app registration**, not the code: it must have these *delegated* permissions granted +
  admin-consented, or the tokens carry no service scope and the connect returns `UnAuthorized`
  (Exchange) / `AADSTS650057` (Teams):
  - **Office 365 Exchange Online** → `Exchange.Manage` (+ `Exchange.ManageV2`) — this same resource
    also owns `ps.compliance.protection.outlook.com`, so it covers **Security & Compliance / EOP**
    (`Connect-IPPSSession`) with no extra grant.
  - **Skype and Teams Tenant Admin API** → `user_impersonation`
  - SharePoint connects via the SPO admin host (already worked; its posture tests also run via Graph).

  All five token-reachable services now connect — Graph, Exchange, **EOP** (`Connect-IPPSSession`,
  IsEopSession), Teams, SharePoint — and `Test-MtConnection` reports True for each. Live-validated on
  Ivy24 after `az ad app permission add … && az ad app permission admin-consent` (and clearing the
  stale MSAL token cache so a fresh token carries the new scope): **Exchange `0/0/114` → `59/45/8`**,
  Teams `0/1/5` → `4/2/0`; **overall skips 232 → 99**. The residual ~99 skips are services with no
  token bridge yet — Azure (`Connect-AzAccount`), Dataverse, Azure DevOps — plus license/feature-gated
  controls. Bundling ExchangeOnlineManagement / MicrosoftTeams / PnP into the release is still open
  (they're required on the box today).
- The nav's Entra/Intune screens currently run the **full** suite and filter the view by
  category (robust against Maester tag-name drift); per-category `-Tag` runs can come
  later via `MaesterRunRequest.tags`.
- **Give-back (B1) started.** A draft Intune `Test-Mt*` derived from the OIB/CIS baseline lives
  in [`contrib/maester/`](../contrib/maester/) — not yet submitted upstream (see its README).
