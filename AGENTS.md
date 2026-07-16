# cmProjectX Agent Guide

Use this file as the default operating guide for work in this repository. Keep changes minimal, respect the Rust/.NET seam, and prefer existing docs over re-explaining project context.

## Start Here

- Product and architecture overview: [README.md](./README.md)
- MVP scope and design decisions: [docs/MVP-PLAN.md](./docs/MVP-PLAN.md)
- API contract and codegen direction: [contract/README.md](./contract/README.md)
- .NET Core fork guidance: [service/Core/README.md](./service/Core/README.md)
- Parser dependency rules: [crates/parser/README.md](./crates/parser/README.md)

## Repository Shape

- `app/`: Rust desktop client. Today it is a thin client that pings the local service.
- `crates/api-types/`: Rust DTOs that currently mirror the API contract by hand.
- `contract/`: OpenAPI schema. This is the source of truth for shared DTOs and endpoints.
- `service/Api/`: ASP.NET API host.
- `service/Core/`: hard-forked Intune Commander core seam. Reuse it; do not re-implement Graph coverage in Rust.
- `service/Store/`: append-only storage seam.
- `service/Sync/`: Graph delta sync seam.

## Commands

- Rust build: `cargo build`
- Rust app: `cargo run -p app`
- .NET build from repo root: `dotnet build service/CmProjectX.slnx`
- .NET API run from repo root: `dotnet run --project service/Api/Api.csproj`

## Project-Specific Rules

- Treat [contract/openapi.yaml](./contract/openapi.yaml) as canonical. Prefer generating C# and Rust types from it instead of expanding hand-written DTOs in parallel.
- Keep the parser as an external upstream dependency. Parser fixes belong upstream first; this repo should normally only change the dependency pin or integration code.
- Do not rewrite the Intune/Graph engine in Rust. The intended architecture is a Rust client talking to a local .NET sidecar.
- `service/Store/` is append-only by design. Preserve that model when implementing storage behavior.
- Avoid editing build artifacts and local state in `target/`, `bin/`, `obj/`, `.vs/`, or local database files unless the task is explicitly about generated output.
- The Rust client currently depends on a sibling `cmtraceopen` checkout via a path dependency. If you need parser-related builds to work outside that environment, switch to a pinned git dependency rather than vendoring code here.
- Windows Reactor in [app/Cargo.toml](./app/Cargo.toml) is an **active git dependency pinned to a commit** (`windows-reactor` / `windows-reactor-setup` from `microsoft/windows-rs`; not on crates.io). A `[patch]` in the root [Cargo.toml](./Cargo.toml) redirects both to a sibling `../windows-rs/` fork carrying a nested-dirty reconcile fix — keep that patch; without it state-driven Reactor children under a stable ancestor (e.g. `NavigationView`) never re-render. Don't bump the rev or drop the patch casually.

## Working Norms

- Prefer narrow validation for the stack you touched: `cargo build` for Rust-only changes, `dotnet build service/CmProjectX.slnx` for service-only changes.
- If a task touches the API surface, update the OpenAPI contract first or confirm the contract already matches the intended behavior.
- Link to the docs above when explaining architecture or product intent instead of copying long sections into new files.
- Ignore checked-in stubs unless the task explicitly asks to flesh them out; this repo still contains planned seams that are intentionally incomplete.
