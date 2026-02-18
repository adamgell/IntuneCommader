# CLAUDE.md - AI Assistant Guide for Intune Commander

## Project Overview

Intune Commander is a **.NET 10 / Avalonia UI** desktop application for managing Microsoft Intune configurations across multiple cloud environments (Commercial, GCC, GCC-High, DoD). It is a ground-up remake of [Micke-K/IntuneManagement](https://github.com/Micke-K/IntuneManagement) (PowerShell/WPF).

**Current Status:** Phase 1 implemented. Core infrastructure (auth, Graph API services, export/import, basic UI) is in place.

## Repository Structure

```
IntuneCommander/
├── CLAUDE.md                  # This file
├── README.md                  # Project overview
├── .gitignore                 # .NET / editor exclusions
├── IntuneManager.sln          # Solution file
├── docs/
│   ├── ARCHITECTURE.md        # Technical architecture & design decisions
│   ├── DECISIONS.md           # 15 recorded architectural decisions with rationale
│   ├── PLANNING.md            # 6-phase development plan with success criteria
│   └── NEXT-STEPS.md          # Pre-coding checklist, Phase 1 guide, resources
├── src/
│   ├── IntuneManager.Core/           # Shared business logic (.NET 10 class library)
│   │   ├── Auth/                     # Authentication providers
│   │   │   ├── IAuthenticationProvider.cs   # Auth provider interface
│   │   │   ├── InteractiveBrowserAuthProvider.cs  # Azure.Identity interactive auth
│   │   │   └── GraphClientFactory.cs        # IntuneGraphClientFactory
│   │   ├── Services/                 # Graph API and business services
│   │   │   ├── IIntuneService.cs     # Device Configuration CRUD interface
│   │   │   ├── IntuneService.cs      # Graph API implementation
│   │   │   ├── IExportService.cs     # Export interface
│   │   │   ├── ExportService.cs      # JSON file export
│   │   │   ├── IImportService.cs     # Import interface
│   │   │   ├── ImportService.cs      # JSON file import + Graph API create
│   │   │   └── ProfileService.cs     # Tenant profile CRUD and persistence
│   │   ├── Models/                   # Data models and enums
│   │   │   ├── CloudEnvironment.cs   # Commercial/GCC/GCCHigh/DoD enum
│   │   │   ├── AuthMethod.cs         # Interactive/Certificate/ManagedIdentity enum
│   │   │   ├── TenantProfile.cs      # Profile model
│   │   │   ├── ProfileStore.cs       # Collection of profiles for serialization
│   │   │   ├── CloudEndpoints.cs     # Graph endpoints & authority hosts per cloud
│   │   │   ├── MigrationEntry.cs     # Single ID mapping entry
│   │   │   └── MigrationTable.cs     # Collection of migration entries
│   │   └── Extensions/
│   │       └── ServiceCollectionExtensions.cs  # DI registration
│   └── IntuneManager.Desktop/        # Avalonia UI application
│       ├── App.axaml / App.axaml.cs  # App entry with DI setup
│       ├── Program.cs                # Main entry point
│       ├── ViewLocator.cs            # ViewModel-to-View resolution
│       ├── Views/
│       │   ├── MainWindow.axaml/.cs  # Main window with toolbar, list, detail pane
│       │   └── LoginView.axaml/.cs   # Login form (tenant ID, client ID)
│       └── ViewModels/
│           ├── ViewModelBase.cs      # Base with IsBusy, ErrorMessage
│           ├── LoginViewModel.cs     # Auth flow, profile creation
│           └── MainWindowViewModel.cs # Config list, export/import, state mgmt
└── tests/
    └── IntuneManager.Core.Tests/     # xUnit tests (30 tests)
        ├── Models/
        │   ├── CloudEndpointsTests.cs
        │   └── MigrationTableTests.cs
        └── Services/
            ├── ProfileServiceTests.cs
            └── ExportServiceTests.cs
```

## Technology Stack

| Component | Technology | Version |
|-----------|-----------|---------|
| Runtime | .NET 10 (LTS) | 10.0.x |
| Language | C# 12 | — |
| UI Framework | Avalonia | 11.3.x |
| MVVM Toolkit | CommunityToolkit.Mvvm | 8.2.x |
| Authentication | Azure.Identity | 1.17.x |
| Graph API | Microsoft.Graph SDK | 5.102.x |
| DI | Microsoft.Extensions.DependencyInjection | 10.0.x |
| Testing | xUnit | 2.5.x |

## Build & Run

```bash
# Build all projects
dotnet build

# Run unit tests (30 tests)
dotnet test

# Run the desktop application
dotnet run --project src/IntuneManager.Desktop
```

## Development Phases

1. **Phase 1 (DONE)** — Foundation: Auth, Device Configurations CRUD, export/import, basic UI
2. **Phase 2** — Multi-Cloud + Profile System: All gov clouds, saved profiles
3. **Phase 3** — Expand Object Types: Compliance, Settings Catalog, Apps, CA
4. **Phase 4** — Bulk Operations: Multi-select export/import, dependency handling
5. **Phase 5** — Auth Expansion: Certificate auth, Managed Identity
6. **Phase 6** — Polish & Docker: Logging, CLI mode, containerization

## Key Architecture Decisions

- **Azure.Identity over MSAL** — `TokenCredential` abstraction, no direct MSAL dependency
- **Separate app registration per cloud** — GCC-High/DoD require isolated registrations
- **Microsoft.Graph SDK models directly** — No custom model layer; custom DTOs only for export edge cases
- **MVVM with CommunityToolkit.Mvvm** — Source generators, `[ObservableProperty]`, `[RelayCommand]`
- **Read-only backward compatibility** — Can import PowerShell version JSON exports
- **Class name: `IntuneGraphClientFactory`** — Renamed from `GraphClientFactory` to avoid collision with `Microsoft.Graph.GraphClientFactory`

## Coding Conventions

### C# Style

- Use C# 12 features: primary constructors, collection expressions, required members, file-scoped types
- Async/await for all I/O operations
- Nullable reference types enabled (`<Nullable>enable</Nullable>`)
- Follow .NET naming: PascalCase public, `_camelCase` private fields

### Naming Patterns

- **Namespaces:** `IntuneManager.Core.*`, `IntuneManager.Desktop.*`
- **Interfaces:** `I{Name}` (e.g., `IIntuneService`, `IAuthenticationProvider`)
- **Services:** `I{Name}Service` / `{Name}Service`
- **ViewModels:** `{ViewName}ViewModel` — must be `partial class` for source generators
- **Views:** `{Name}View.axaml` or `{Name}Window.axaml`
- **XAML DataType:** Always set `x:DataType` for compiled bindings

### DI Service Lifetimes

- **Singleton:** `IntuneGraphClientFactory`, `ProfileService`, `IAuthenticationProvider`
- **Transient:** `IExportService`, ViewModels

### ViewModelBase Pattern

All ViewModels inherit from `ViewModelBase` which provides:
- `IsBusy` (bool) — for loading states
- `ErrorMessage` (string?) — for error display
- `ClearError()` / `SetError(message)` helpers

## Testing

- **Framework:** xUnit (30 tests currently passing)
- **Coverage areas:** CloudEndpoints, MigrationTable, ProfileService, ExportService
- **Run:** `dotnet test`
- **Convention:** Tests in `tests/IntuneManager.Core.Tests/` mirror source structure

## Multi-Cloud Configuration

Defined in `CloudEndpoints.cs`:

| Cloud | Graph Endpoint | Authority Host |
|-------|---------------|----------------|
| Commercial | `https://graph.microsoft.com` | `AzureAuthorityHosts.AzurePublicCloud` |
| GCC | `https://graph.microsoft.com` | `AzureAuthorityHosts.AzurePublicCloud` |
| GCC-High | `https://graph.microsoft.us` | `AzureAuthorityHosts.AzureGovernment` |
| DoD | `https://dod-graph.microsoft.us` | `AzureAuthorityHosts.AzureGovernment` |

## Export/Import Format

```
ExportFolder/
├── DeviceConfigurations/
│   ├── PolicyName.json          # Serialized DeviceConfiguration
│   └── ...
└── migration-table.json         # ID mapping (originalId → newId)
```

## Important Context for AI Assistants

- The project targets **Avalonia UI** (not WPF). XAML files use `.axaml` extension.
- Compiled bindings are enabled (`AvaloniaUseCompiledBindingsByDefault`). Always set `x:DataType`.
- Use `Microsoft.Graph` SDK models directly — avoid creating redundant model classes.
- The Graph client factory is named `IntuneGraphClientFactory` (not `GraphClientFactory`) to avoid namespace collision.
- All Graph API calls must support multi-cloud endpoints configured per profile.
- Export JSON format should be backward-compatible with the PowerShell version.
- Use `Azure.Identity` credential types, not raw MSAL.
- The project is a hobby/personal project — keep solutions pragmatic and avoid over-engineering.
