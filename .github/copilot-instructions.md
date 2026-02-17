# Copilot Instructions

## Project Overview
IntuneManager is a .NET 8 / Avalonia UI desktop app for managing Microsoft Intune configurations across Commercial, GCC, GCC-High, and DoD clouds. It's a ground-up remake of a PowerShell/WPF tool — the migration to compiled .NET specifically targets UI deadlocks and threading issues.

## Critical: Async-First UI Rule
- The UI startup must NEVER block or wait on any async operation. All data loading (profiles, services, etc.) must happen asynchronously after the window is already visible.
- **No `.GetAwaiter().GetResult()`, `.Wait()`, or `.Result` calls on the UI thread — ever.**
- Fire-and-forget pattern for non-blocking loads: `_ = LoadProfilesAsync();` (see `MainWindowViewModel` constructor).
- All `[RelayCommand]` methods returning `Task` get automatic `CancellationToken` support from CommunityToolkit.Mvvm.

## Architecture
- **Two-project solution**: `IntuneManager.Core` (class library: auth, services, models) and `IntuneManager.Desktop` (Avalonia UI app).
- **MVVM with CommunityToolkit.Mvvm**: Use `[ObservableProperty]`, `[RelayCommand]`, and `partial` classes. ViewModels extend `ViewModelBase` which provides `IsBusy`/`ErrorMessage`.
- **DI setup**: Services registered in `ServiceCollectionExtensions.AddIntuneManagerCore()`. Desktop-layer ViewModels registered in `App.axaml.cs`. Graph-dependent services (e.g., `ConfigurationProfileService`) are created manually after auth, not via DI.
- **ViewLocator pattern**: Avalonia resolves Views from ViewModels by naming convention (`FooViewModel` → `FooView`).

## Service-per-Type Pattern
Each Intune object type gets its own interface + implementation:
- `IConfigurationProfileService` / `ConfigurationProfileService` — Device Configurations
- `ICompliancePolicyService` / `CompliancePolicyService` — Compliance Policies
- `IApplicationService` / `ApplicationService` — Applications (read-only)

All take `GraphServiceClient` in constructor, use `PageIterator` for Graph API pagination, accept `CancellationToken`, and return `List<T>`.

## Key Conventions
- **Graph SDK models used directly** — no wrapper DTOs. Types like `DeviceConfiguration`, `MobileApp` come from `Microsoft.Graph.Models`.
- **Export format**: Subfolder-per-type (`DeviceConfigurations/`, `CompliancePolicies/`, `Applications/`) with `migration-table.json` for ID mappings. Must maintain read compatibility with the original PowerShell tool's JSON format.
- **Export wrappers** for types with assignments: `CompliancePolicyExport`, `ApplicationExport` bundle the object + its assignments list.
- **Profile storage**: Encrypted JSON at `%LOCALAPPDATA%\IntuneManager\profiles.json` using `Microsoft.AspNetCore.DataProtection`. Marker prefix `INTUNEMANAGER_ENC:` distinguishes encrypted from plain files.
- **Multi-cloud**: `CloudEndpoints.GetEndpoints(cloud)` returns `(graphBaseUrl, authorityHost)` tuple. Separate app registrations per cloud.
- **Computed columns**: DataGrid uses `DataGridColumnConfig` with `"Computed:"` prefix in `BindingPath` for values derived in code-behind (e.g., platform inferred from OData type).

## Build & Test
```bash
dotnet build                                    # Build all projects
dotnet test                                     # Run xUnit tests
dotnet run --project src/IntuneManager.Desktop  # Launch the app
```
Tests live in `tests/IntuneManager.Core.Tests/` — xUnit with `[Fact]`/`[Theory]`, temp directories for file I/O tests, `IDisposable` cleanup. Tests cover models and services in Core only (no UI tests).

## Adding a New Intune Object Type
1. Create `I{Type}Service` interface in `Core/Services/` following the CRUD + `GetAssignmentsAsync` pattern.
2. Create `{Type}Service` implementation taking `GraphServiceClient`, using `PageIterator` for listing.
3. If assignments are needed, create `{Type}Export` model in `Core/Models/` bundling object + assignments.
4. Add export/import methods to `ExportService`/`ImportService`.
5. Wire into `MainWindowViewModel`: add collection, selection property, column configs, nav category, and load logic.
6. Add tests in `tests/IntuneManager.Core.Tests/`.