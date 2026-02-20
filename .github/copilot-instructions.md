# Copilot Instructions

## Project Overview
Intune Commander is a .NET 10 / Avalonia UI desktop app for managing Microsoft Intune configurations across Commercial, GCC, GCC-High, and DoD clouds. It's a ground-up remake of a PowerShell/WPF tool — the migration to compiled .NET specifically targets UI deadlocks and threading issues.

## Critical: Async-First UI Rule
- The UI startup must NEVER block or wait on any async operation. All data loading (profiles, services, etc.) must happen asynchronously after the window is already visible.
- **No `.GetAwaiter().GetResult()`, `.Wait()`, or `.Result` calls on the UI thread — ever.**
- Fire-and-forget pattern for non-blocking loads: `_ = LoadProfilesAsync();` (see `MainWindowViewModel` constructor).
- All `[RelayCommand]` methods returning `Task` get automatic `CancellationToken` support from CommunityToolkit.Mvvm.

## Architecture
- **Two-project solution**: `Intune.Commander.Core` (class library: auth, services, models) and `Intune.Commander.Desktop` (Avalonia UI app).
- **MVVM with CommunityToolkit.Mvvm**: Use `[ObservableProperty]`, `[RelayCommand]`, and `partial` classes. ViewModels extend `ViewModelBase` which provides `IsBusy`/`ErrorMessage`.
- **DI setup**: Services registered in `ServiceCollectionExtensions.AddIntuneCommanderCore()`. Desktop-layer ViewModels registered in `App.axaml.cs`. Graph-dependent services (e.g., `ConfigurationProfileService`) are created manually after auth, not via DI.
- **ViewLocator pattern**: Avalonia resolves Views from ViewModels by naming convention (`FooViewModel` → `FooView`).

## Service-per-Type Pattern
Each Intune object type gets its own interface + implementation:
- `IConfigurationProfileService` / `ConfigurationProfileService` — Device Configurations
- `ICompliancePolicyService` / `CompliancePolicyService` — Compliance Policies
- `IApplicationService` / `ApplicationService` — Applications (read-only)

All take `GraphServiceClient` in constructor, use manual `@odata.nextLink` pagination (see below), accept `CancellationToken`, and return `List<T>`.

## Graph API Pagination — Manual `@odata.nextLink` (REQUIRED)
**Do NOT use `PageIterator`** — it silently truncates results on some tenants. All Graph list operations must use manual `while` loop pagination following `OdataNextLink`:
```csharp
var response = await _graphClient.DeviceAppManagement.MobileApps
    .GetAsync(req =>
    {
        req.QueryParameters.Top = 200;
        // other query params...
    }, cancellationToken);

var result = new List<MobileApp>();
while (response != null)
{
    if (response.Value != null)
        result.AddRange(response.Value);

    if (!string.IsNullOrEmpty(response.OdataNextLink))
    {
        response = await _graphClient.DeviceAppManagement.MobileApps
            .WithUrl(response.OdataNextLink)
            .GetAsync(cancellationToken: cancellationToken);
    }
    else
    {
        break;
    }
}
```
Key rules:
- Always set `$top=999` on the initial request.
- Use `.WithUrl(response.OdataNextLink)` (requires `using Microsoft.Kiota.Abstractions;`) to follow next pages.
- Apply this pattern to **every** service method that lists Graph objects, including `GroupService`.

## Key Conventions
- **Graph SDK models used directly** — no wrapper DTOs. Types like `DeviceConfiguration`, `MobileApp` come from `Microsoft.Graph.Models`.
- **Export format**: Subfolder-per-type (`DeviceConfigurations/`, `CompliancePolicies/`, `Applications/`) with `migration-table.json` for ID mappings. Must maintain read compatibility with the original PowerShell tool's JSON format.
- **Export wrappers** for types with assignments: `CompliancePolicyExport`, `ApplicationExport` bundle the object + its assignments list.
- **Profile storage**: Encrypted JSON at `%LOCALAPPDATA%\Intune.Commander\profiles.json` using `Microsoft.AspNetCore.DataProtection`. Marker prefix `INTUNEMANAGER_ENC:` distinguishes encrypted from plain files (marker preserved as compatibility constant).
- **Multi-cloud**: `CloudEndpoints.GetEndpoints(cloud)` returns `(graphBaseUrl, authorityHost)` tuple. Separate app registrations per cloud.
- **Computed columns**: DataGrid uses `DataGridColumnConfig` with `"Computed:"` prefix in `BindingPath` for values derived in code-behind (e.g., platform inferred from OData type).

## Build & Test
```bash
dotnet build                                    # Build all projects
dotnet test                                     # Run xUnit tests
dotnet run --project src/Intune.Commander.Desktop  # Launch the app
```
Tests live in `tests/Intune.Commander.Core.Tests/` — xUnit with `[Fact]`/`[Theory]`, temp directories for file I/O tests, `IDisposable` cleanup. Tests cover models and services in Core only (no UI tests).

## Adding a New Intune Object Type
1. Create `I{Type}Service` interface in `Core/Services/` following the CRUD + `GetAssignmentsAsync` pattern.
2. Create `{Type}Service` implementation taking `GraphServiceClient`, using manual `@odata.nextLink` pagination for listing (see pagination section above).
3. If assignments are needed, create `{Type}Export` model in `Core/Models/` bundling object + assignments.
4. Add export/import methods to `ExportService`/`ImportService`.
5. Wire into `MainWindowViewModel`: add collection, selection property, column configs, nav category, and load logic.
6. Add tests in `tests/Intune.Commander.Core.Tests/`.