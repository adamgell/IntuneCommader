# cmProjectX — Regression Testing Strategy

Enterprise-grade regression coverage for a two-process Windows desktop app
(Rust + WinUI/Reactor client → .NET sidecar) that manages live Intune/Entra
tenants and persists an append-only audit/drift time-machine.

This document is the detailed plan behind the ROADMAP **M12 "Ship"** line item
(*"contract round-trip tests, sidecar integration tests against a recorded
Graph, Rust UI smoke, build gates"*). It scales that line into a sustained
regression program.

## Operating assumptions (override before RT1 if wrong)

1. **Local hardware, not hosted CI.** The suite runs on self-hosted runners on
   your own Windows boxes — hosted `ubuntu-latest` cannot run the WinUI client,
   cannot reach the tenant, and isn't the release architecture.
2. **Two architectures, 50/50.** `aarch64-pc-windows-msvc` and
   `x86_64-pc-windows-msvc` are co-equal release targets, so the heavy tiers run
   as a **two-arch matrix**. An ARM64-only suite would miss half the install base.
3. **Recorded Graph is the primary isolation mechanism** (per ROADMAP M12), not
   hand-written mocks and not live-tenant-for-everything. Record real payloads
   once; replay deterministically and offline.
4. **Phased gating.** Tiers become PR-blocking as they stabilize, not on day one.

---

## Goals & non-goals

**Goals**

- Catch regressions across all four fragile seams: the **contract** (186
  endpoints, ~37 .NET / ~35 Rust DTOs that must stay byte-compatible over JSON),
  the **store invariants** (append-only, content-hash dedup, delta watermarks),
  the **auth/profile state machine**, and the **per-arch native client**.
- Run **fully offline and deterministically** in the default path (no tenant, no
  secret, no network) so the suite is fast, repeatable, and safe to run on every
  change.
- Validate **both architectures** at the tier that actually differs by arch
  (native client + per-RID publish + native deps like SQLite).
- Keep the **live tenant secret on local hardware only** — never on a cloud
  runner.

**Non-goals (for now)**

- Pixel-level WinUI UI automation beyond the existing launch+screenshot smoke
  harness. (Revisit if screen regressions become frequent.)
- Load/performance benchmarking as a gate. (Track cache-warm timing as a metric,
  not a pass/fail.)
- Testing intentionally-stubbed seams (`Sync/` incremental delta, the 7 stub
  features). Cover them when they're fleshed out, not before.

---

## Principles

- **Local-first execution.** Self-hosted runners are the substrate; a single
  orchestrator script is the source of truth for "what the suite is."
- **Deterministic by default.** The everyday run touches no network. Live-tenant
  runs are a separate, explicit, lower-frequency tier.
- **Contract-first.** The contract is the spec under test. Parity tests assert
  Rust and .NET both round-trip every shape the contract defines — drift between
  the two DTO sets is a *test failure*, not a review catch.
- **Invariant-driven, not example-driven, for the store.** Test the *rules*
  (append-only, dedup, watermark resume), not one happy path.
- **Respect the single-instance constraints.** One sidecar per box owns :5099 and
  the SQLite/Lucene store; never two concurrent `dotnet build` of one project.
  Sidecar-dependent work is **serialized**, the store is **reset to a known
  baseline per run**.

---

## Execution topology

```
   ┌─────────────────────────────────────────────────────────────┐
   │  Hosted PR check (ubuntu, free)   →  Tier 1 only (hermetic)  │
   └─────────────────────────────────────────────────────────────┘
                              │  (same scripts/test.ps1 -Tier 1)
   ┌──────────────────────────┴──────────────────────────────────┐
   │  Self-hosted runner pool (your hardware)                     │
   │                                                              │
   │   runner: [self-hosted, windows, arm64, tenant]              │
   │   runner: [self-hosted, windows, x64,   tenant]              │
   │                                                              │
   │   each runs scripts/test.ps1 → Tier 1 + 2 (+ 3 nightly)      │
   │   each owns its OWN sidecar :5099 + store (no cross-box      │
   │   contention; serialized within a box)                       │
   └──────────────────────────────────────────────────────────────┘
```

- **`scripts/test.ps1` — the single definition.** Parameterized by `-Tier`,
  `-Arch`, `-Record`/`-Replay`. The runner invokes it; you invoke the identical
  thing by hand. It owns: store reset → sidecar start/warm → run suites in order
  → collect coverage + reports → sidecar drain/kill. Mirrors the lifecycle
  `SidecarFixture` already prototypes.
- **Self-hosted runner over pure script.** Keeps PR-check UX, history, and gating
  while executing on local hardware. (If you'd rather keep it entirely off
  GitHub, the same `test.ps1` runs from a Windows Scheduled Task nightly — the
  only thing lost is the PR annotation.)
- **Two native boxes — decided.** One `arm64` runner, one `x64` runner. Each owns
  its own sidecar on :5099, so there's no port/build contention between arches and
  Tier-3 validates each native client binary on real hardware (no emulation).
- **Store reset per run.** Self-hosted runners persist disk between runs. Each
  run starts from a clean `%LocalAppData%\cmProjectX\` (or a redirected test data
  dir via env) so tests are order-independent.

---

## Test tiers

| Tier | What | Graph | Network/secret | Where | Speed | Gate |
|---|---|---|---|---|---|---|
| **1 — Hermetic** | DTO round-trips, contract↔Rust↔.NET parity, `JsonDrift` engine, snapshot dedup + append-only invariants, auth state-machine transitions, audit/search query logic, pending-change FSM, cmtrace parser fixtures | none | none | anywhere incl. hosted PR + both arches | seconds | **blocks PRs** |
| **2 — Recorded integration** | Real sidecar over HTTP, every endpoint (list/get/create/update/delete) against **replayed Graph cassettes**, cache warm/invalidate, store persistence on a temp DB | replay | none | both self-hosted arches | minutes | nightly + pre-release (→ PR once stable) |
| **3 — Live E2E** | Real `POST /auth/signin`, WinUI launch+screenshot smoke (health→sign-in→list→detail), one scoped create→edit→delete round-trip against a namespaced sandbox object + cleanup; **re-records cassettes** | live | local secret + net | both self-hosted arches | slower | nightly + pre-release |

Tier 1 is the fast feedback loop and the only tier that runs on cheap hosted
PR checks. Tiers 2–3 are the local-hardware payload.

---

## Recorded Graph (the isolation mechanism)

The thing that makes deterministic, offline, secret-free integration testing
possible. A record/replay shim at the **Graph SDK's `HttpMessageHandler`** seam:

- **Record mode** (`-Record`, developer-run, occasional): the sidecar talks to
  the real tenant; every Graph request/response is captured to a **cassette**
  (JSON) keyed by method + normalized URL + relevant headers.
- **Replay mode** (`-Replay`, the default everywhere): the handler serves
  responses from cassettes; **no token, no network**. An unmatched request is a
  hard failure (surfaces missing coverage instead of silently hitting live).
- **Sanitization is mandatory.** A redaction pass strips bearer tokens, tenant
  IDs, object GUIDs of real users/devices, serial numbers, and PII before any
  cassette is committed. Cassettes are fixtures — they get reviewed like code.
- **Re-record cadence.** Tier 3 nightly re-records against the live tenant and
  diffs cassettes; a payload-shape change (Graph Beta drift) shows up as a
  cassette diff — an early-warning signal that the real API moved under us.

This cleanly resolves the old "mock vs live tenant" question: **record once from
the real tenant** (high fidelity), **replay forever** (deterministic), **re-record
nightly** (drift detection).

---

## Coverage map (what gets tested where)

| Surface | File(s) | Tier 1 | Tier 2 | Tier 3 |
|---|---|---|---|---|
| **Contract parity** (DTO byte-compat) | `crates/api-types/src/lib.rs`, `service/Api/Contracts.cs`, `contract/openapi.yaml` | every DTO serialize→deserialize→equal on both sides | — | — |
| **Mappers** (Graph→DTO projection) | `service/Api/Mappers/` | mapper unit tests over captured Graph fragments | — | — |
| **Endpoints** (186 ops) | `service/Api/Endpoints/`, `Program.cs` | — | list/get/create/update/delete vs cassettes; 409 when signed-out | thin live read smoke |
| **Store invariants** | `service/Store/SnapshotStore.cs` | append-only, SHA256 dedup, watermark resume, search query (temp DB) | snapshot-on-write via real endpoints | — |
| **Auth/profile FSM** | `service/Api/AuthSession.cs` | state transitions, tenant-switch clears session | profile CRUD vs cassettes | real device-code/secret sign-in |
| **Client logic** | `app/src/api_client.rs`, `features.rs` | api_client decode/error model, feature-registry ↔ routing parity | — | — |
| **Diagnostics parsers** | `app/src/diag_*.rs` (+ cmtraceopen) | parse checked-in sample logs → expected rows | — | — |
| **WinUI smoke** | the run+screenshot harness | — | — | launch → health → sign-in → list populates → detail pane, per arch |
| **Time-machine** | audit/drift/snapshot/search endpoints | drift engine + dedup (Tier 1) | end-to-end capture+query vs cassettes | — |

**Explicitly out (stubbed):** `Sync/` incremental delta, app-assignments,
bulk-assign, ca-pptx, role-assignments, policy-comparison, cache-dev,
detection-remediation. Add coverage when each is fleshed out.

---

## Tooling

| Concern | Choice | Notes |
|---|---|---|
| .NET test runner | xUnit (already in `Api.Tests`) | split into `Api.Tests.Unit` (Tier 1) + `Api.Tests.Integration` (Tier 2, recorded) + keep live as opt-in trait |
| .NET coverage | coverlet + ReportGenerator | metric first, gate later |
| Rust test runner | `cargo nextest` | faster, better output than bare `cargo test`; add `crates/api-types` + `app` unit tests (currently zero) |
| Contract parity | golden-JSON corpus generated from `openapi.yaml` examples, asserted on both sides | one corpus, two consumers — guarantees Rust/.NET stay in lockstep |
| Lint/static | `clippy -D warnings` (Rust), `dotnet format --verify-no-changes` + analyzers (.NET) | new Tier-0 gate, cheap, runs on PR |
| Graph record/replay | custom `DelegatingHandler` + cassette store | the load-bearing new infra (RT2) |
| WinUI smoke | existing launch/foreground/screenshot harness | DPI-aware coords; CMTrace-open focus-steal already a known gotcha |

---

## CI gating evolution (phased)

1. **Advisory** — runners execute Tier 1–2, publish results + coverage, block
   nothing. Builds confidence the suite is green and non-flaky.
2. **Tier 1 + lint blocking** — once hermetic tests are stable, they (and
   clippy/format) become required PR checks. Cheap, fast, runs on hosted too.
3. **Tier 2 blocking on the self-hosted check** — recorded integration becomes
   required once cassettes cover the implemented surface.
4. **Tier 3 as a pre-release gate** — live E2E + WinUI smoke required green on
   both arches before a release tag is cut (wire into the codesign workflow's
   precondition).

---

## Secrets & data hygiene

- The live secret stays in `%LocalAppData%\Intune.Commander\profiles.json` on the
  runner box; it is **never** exported to a cloud runner or committed.
- Cassettes are **sanitized** (tokens/PII/GUIDs redacted) and reviewed before
  commit.
- Tier-3 write tests operate only on **sandbox-namespaced** throwaway objects
  (e.g. a `zzz-cmpx-test-*` naming convention) and **clean up in a `finally`** —
  a write test that can't guarantee cleanup is read-only instead.
- The append-only store means test writes accumulate; the per-run reset + a
  dedicated test data dir keep the real store untouched.

---

## Phased rollout

Each phase ends green on `cargo nextest` + `dotnet test` for its tier and is
wired into `scripts/test.ps1`.

| Phase | Scope | DoD |
|---|---|---|
| **RT1 — Hermetic foundation** ✅ *(x64 leg landed)* | `service/Api.Tests.Unit` + `crates/api-types` parity tests + `scripts/test.ps1 -Tier 1`. **Delivered:** contract parity (shared `contract/examples/` corpus asserted on BOTH .NET and Rust), `JsonDrift`, store append-only/dedup/watermark/search, **auth/profile FSM** (sign-out, warm-stamp lifecycle, tenant-switch invalidation, cloud/auth enum mapping); clippy + `dotnet format` gates; `.NET` suite wired into hosted CI. **Deferred:** the **cmtrace parser fixture** (see note below) and the arm64 leg (waits on the arm64 box). | **Done on x64:** `test.ps1 -Tier 1` green + hosted CI runs the .NET suite, offline in ~seconds. |

> **Parser-fixture deferral (RT1 → Tier 3).** The CMTrace parser is the upstream
> `cmtraceopen-parser` crate (out of scope to test here), and our only consumer is
> `app/src/main.rs` — which builds solely as part of the Windows-only WinUI/Reactor
> `app` crate (heavy, minutes, not "runs anywhere"). A parser fixture there is a
> Tier-3-shaped test, not a fast hermetic Tier-1 one. To make it Tier-1 would first
> require extracting the parse-integration glue into a light library crate, or
> adding a dedicated fixture crate that dev-deps the upstream parser. Tracked for
> Tier 3 (where the app is built anyway) or a follow-up extraction.
| **RT2 — Recorded Graph** ⏳ *(infra landed)* | **Done:** record/replay handler + cassette store + the production `IntuneGraphClientFactory.GraphTransport` seam, all proven through the real Graph SDK offline (`service/Api.Tests.Integration`); wired into `scripts/test.ps1 -Tier 2`. **Remaining:** record the implemented surface once from the tenant + body sanitization (#12); convert the live `Api.Tests` endpoint tests to replay (#13). | Every implemented endpoint has a list+detail cassette; Tier 2 runs with no network/secret; unmatched request = failure. |
| **RT3 — Self-hosted runners** | Register the two native runners (arm64 + x64); add a `regression` workflow calling `test.ps1`; store-reset + serialization wired. Advisory gating. | Tiers 1–2 run on both arches on push; results + coverage published; no port/build contention. |
| **RT4 — Live E2E + WinUI smoke** | Tier 3 against the **sandbox tenant**: real sign-in, screenshot smoke per arch, one sandboxed CRUD round-trip + cleanup, nightly cassette re-record/diff. | Nightly green on both arches; cassette drift surfaces as a diff; sandbox objects always cleaned up. |
| **RT5 — Enforce + broaden** | Flip Tier 1+lint → required, then Tier 2 → required; Tier 3 → release precondition. Backfill coverage per implemented surface; track coverage %. | PRs blocked by Tier 1/lint; release tag blocked unless Tier 3 green on both arches. |

RT1–RT2 are sequenceable immediately and independent of the feature spine.
RT3–RT5 follow on the two-box hardware.

---

## Decisions

**Settled**

- **Two native runner boxes** — one arm64, one x64 (50/50 install base). No
  emulation; each arch's client binary is validated on real hardware. (RT3)
  - **Prerequisite — provision the arm64 box.** Today only the x64 build box
    exists; it has only the `x86_64-pc-windows-msvc` Rust target. The arm64
    runner needs arm64 Windows hardware + `rustup target add
    aarch64-pc-windows-msvc` + the ARM64 VC tools component + a cross dev-shell
    before any arm64 leg (even Tier 1) can run. RT1–RT2 start on the x64 box
    now; the arm64 leg lands when that box is stood up. (See the
    `production-release-topology` / `rust-client-cannot-build-in-worktree` notes.)
- **Dedicated sandbox tenant** for Tier-3 live/write tests — throwaway
  `zzz-cmpx-test-*` objects, created and cleaned up per run, never against a
  production tenant. (RT4)

**Still open**

1. **Cassette storage.** In-repo (reviewed as fixtures, simple) vs a separate
   `tests/cassettes` LFS area if they grow large. Start in-repo; revisit if size
   becomes a problem.
2. **NativeAOT sidecar interaction.** If the M12 AOT-publish path lands, the
   recorded-Graph handler must be AOT-compatible (or injected only in test
   builds). → revisit if/when AOT is pursued.
