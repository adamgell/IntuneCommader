# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Intune Commander is a **.NET 10 / Avalonia UI** desktop application for managing Microsoft Intune configurations across multiple cloud environments (Commercial, GCC, GCC-High, DoD). It is a ground-up remake of [Micke-K/IntuneManagement](https://github.com/Micke-K/IntuneManagement) (PowerShell/WPF).

## Build & Run

```bash
# Build all projects
dotnet build

# Run unit tests (excludes integration tests)
dotnet test --filter "Category!=Integration"

# Run unit tests with coverage threshold (40% line coverage enforced)
dotnet test /p:CollectCoverage=true /p:Threshold=40 /p:ThresholdType=line /p:ThresholdStat=total

# Run integration tests (requires AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET env vars)
dotnet test --filter "Category=Integration"

# Run a single test class
dotnet test --filter "FullyQualifiedName~ProfileServiceTests"

# Run the desktop application
dotnet run --project src/Intune.Commander.Desktop
```

## Technology Stack

| Component | Technology |
|-----------|-----------|
| Runtime | .NET 10, C# 12 |
| UI Framework | Avalonia 11.3.x (`.axaml` files) |
| MVVM | CommunityToolkit.Mvvm 8.2.x |
| Authentication | Azure.Identity 1.17.x |
| Graph API | **Microsoft.Graph.Beta** 5.130.x-preview |
| Cache | LiteDB 5.0.x (encrypted via DataProtection) |
| Charts | LiveChartsCore.SkiaSharpView.Avalonia 2.0.x |
| Dialogs | MessageBox.Avalonia 3.2.x |
| Profile storage | `Microsoft.AspNetCore.DataProtection` |
| DI | `Microsoft.Extensions.DependencyInjection` 10.0.x |
| Testing | xUnit |

**Important:** The project uses `Microsoft.Graph.Beta` (not the stable `Microsoft.Graph` package). All models and the `GraphServiceClient` come from `Microsoft.Graph.Beta.*`.

## Architecture

### Project layout

```
src/
  Intune.Commander.Core/    # Business logic (.NET 10 class library)
    Auth/                    # IAuthenticationProvider, InteractiveBrowserAuthProvider, IntuneGraphClientFactory
    Models/                  # Enums (CloudEnvironment, AuthMethod), TenantProfile, ProfileStore,
                             #   CloudEndpoints, MigrationEntry/Table, export DTOs, CacheEntry, GroupAssignmentResult
    Services/                # 30+ Graph API services + ProfileService, CacheService, ExportService, ImportService
    Extensions/              # ServiceCollectionExtensions (AddIntuneCommanderCore)
  Intune.Commander.Desktop/  # Avalonia UI
    Views/                   # MainWindow, LoginView, OverviewView, GroupLookupWindow, DebugLogWindow, RawJsonWindow
    ViewModels/              # MainWindowViewModel, LoginViewModel, OverviewViewModel, GroupLookupViewModel,
                             #   DebugLogViewModel, NavCategory, DataGridColumnConfig, row/item types
    Services/                # DebugLogService (singleton, UI-thread-safe in-memory log)
    Converters/              # ComputedColumnConverters
tests/
  Intune.Commander.Core.Tests/  # xUnit tests mirroring src structure
```

### DI and service lifetimes

`App.axaml.cs` calls `services.AddIntuneCommanderCore()` then registers `MainWindowViewModel` as transient.

`AddIntuneCommanderCore()` registers:
- **Singleton:** `IAuthenticationProvider`, `IntuneGraphClientFactory`, `ProfileService`, `IProfileEncryptionService`, `ICacheService`
- **Transient:** `IExportService`

**Graph API services are NOT registered in DI.** After a successful login, `MainWindowViewModel` creates them directly using `new XxxService(graphClient)`. This means all the `IConfigurationProfileService`, `ICompliancePolicyService`, etc. fields in the VM are nullable and only populated after `ConnectAsync`.

### Authentication and multi-cloud

`IntuneGraphClientFactory.CreateClientAsync(profile)` creates a `GraphServiceClient` using `Azure.Identity` credentials and the correct endpoint from `CloudEndpoints.GetEndpoints(profile.Cloud)`.

Cloud endpoints in `CloudEndpoints.cs`:
- Commercial & GCC → `https://graph.microsoft.com`
- GCC-High → `https://graph.microsoft.us`
- DoD → `https://dod-graph.microsoft.us`

### Navigation and data flow

`MainWindowViewModel` holds 30+ `ObservableCollection<T>` properties (one per Intune object type) and corresponding `Selected*` properties. Navigation is driven by `NavCategories` (an `ObservableCollection<NavCategory>`) and `SelectedCategory`. Each category loads its data lazily on first selection via per-type `_*Loaded` boolean flags; many also use `ICacheService` keyed by a `CacheKey*` constant.

`OverviewViewModel` is a nested VM (not in DI) computed purely from already-loaded collections with LiveCharts series.

### Caching

`CacheService` uses LiteDB with an AES-encrypted database file at `%LocalAppData%\Intune.Commander\cache.db`. The DB password is generated once and stored encrypted via `Microsoft.AspNetCore.DataProtection` in `cache-key.bin`. Cache entries have a 24-hour default TTL and are keyed by tenant ID + data-type string.

### Profile storage

`ProfileService` persists `ProfileStore` (list of `TenantProfile`) to `%LocalAppData%\Intune.Commander\profiles.json`. When `IProfileEncryptionService` is injected (always the case in production), the file is prefixed with `INTUNEMANAGER_ENC:` and the payload is DataProtection-encrypted. Plaintext files are migrated to encrypted on next save. On first launch after upgrade from a pre-rename build, `ProfileService.LoadAsync` detects and auto-migrates data from the legacy `%LocalAppData%\IntuneManager\profiles.json` path.

> **Legacy compatibility constants** (do not change without careful consideration): `INTUNEMANAGER_ENC:` marker, `IntuneManager.Profiles.v1` DataProtection purpose (fallback decryptor), and `SetApplicationName("IntuneManager")` in `ServiceCollectionExtensions` (changing this makes all existing encrypted data unreadable).

### DebugLogService

`DebugLogService.Instance` is a singleton with an `ObservableCollection<string> Entries` (capped at 2000). All logging dispatches to the UI thread. Use `DebugLog.Log(category, message)` / `DebugLog.LogError(...)` throughout the VM. It is exposed via the `DebugLogWindow`.

### Export/Import format

Each object type exports to its own subfolder under the chosen output directory (e.g., `DeviceConfigurations/`, `CompliancePolicies/`, etc.). Files are named `{DisplayName}.json` containing the serialized Graph Beta model. A `migration-table.json` at the root maps original IDs to new IDs after import.

## Coding Conventions

- **C# 12:** primary constructors, collection expressions (`[]`), required members, file-scoped namespaces
- **Nullable reference types enabled** everywhere
- **Private fields:** `_camelCase`; public: `PascalCase`
- **ViewModels** must be `partial class` for CommunityToolkit.Mvvm source generators (`[ObservableProperty]`, `[RelayCommand]`)
- **XAML:** always set `x:DataType` — `AvaloniaUseCompiledBindingsByDefault` is on
- **Namespaces:** `Intune.Commander.Core.*`, `Intune.Commander.Desktop.*`
- **Graph client factory class name:** `IntuneGraphClientFactory` (not `GraphClientFactory`) to avoid collision with `Microsoft.Graph.GraphClientFactory`

## Key Architecture Decisions

- **Azure.Identity over raw MSAL** — `TokenCredential` abstraction, one code path for all clouds
- **Microsoft.Graph.Beta SDK models directly** — no custom model layer; custom DTOs (`*Export` models) only where the Graph model needs augmenting for export
- **Separate app registration per cloud** — GCC-High/DoD require isolated app registrations
- **Graph services created post-auth, not in DI** — services that require a `GraphServiceClient` are instantiated in `MainWindowViewModel` after the user authenticates, not at startup
- **LiteDB cache keyed by tenant ID** — multiple tenant profiles can share the same cache database; TTL is 24 hours
- **Hobby project** — keep solutions pragmatic; avoid over-engineering

## CI Workflows

| Workflow | File | Trigger | Purpose |
|----------|------|---------|---------|
| CI — Test & Coverage | `.github/workflows/ci-test.yml` | All pushes + PRs to main | Unit tests with 40% line coverage threshold (coverlet.msbuild) |
| CI — Integration Tests | `.github/workflows/ci-integration.yml` | Push/PR to main + manual | Graph API integration tests against live tenant |
| Build Release | `.github/workflows/build-release.yml` | All pushes + PRs | Builds self-contained Windows x64 executable |

- Unit test CI uses `--filter "Category!=Integration"` to skip integration tests
- Integration CI requires repository secrets: `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`
- Coverage threshold is enforced via `/p:Threshold=40` — failing builds if line coverage drops below 40%

## Testing Conventions

### Unit tests (`tests/Intune.Commander.Core.Tests/`)
- xUnit with `[Fact]`/`[Theory]`, no mocking framework
- Service contract tests verify interface conformance, method signatures, return types, and `CancellationToken` parameters via reflection
- File I/O tests use temp directories with `IDisposable` cleanup

### Integration tests (`tests/Intune.Commander.Core.Tests/Integration/`)
- Tagged with `[Trait("Category", "Integration")]` — **always** use this trait for any test hitting Graph API
- Base class `GraphIntegrationTestBase` provides `GraphServiceClient` from env vars and `ShouldSkip()` for graceful no-op when credentials are missing
- Read-only tests (List + Get) are safe for any tenant
- CRUD tests use `IntTest_AutoCleanup_` prefix and clean up in `finally` blocks
- Setup script: `scripts/Setup-IntegrationTestApp.ps1` creates the app registration with all required Graph permissions (see `docs/GRAPH-PERMISSIONS.md`)

## PowerShell Scripts

- Scripts must use **ASCII-only characters** — no Unicode decorations (e.g., `━─→✓✗○—`) as they break PowerShell 5.1 parsing
- Save `.ps1` files with ASCII encoding
- Target PowerShell 5.1+ compatibility
