# CLAUDE.md - AI Assistant Guide for IntuneManager

## Project Overview

IntuneManager is a **greenfield .NET 8 / Avalonia UI** desktop application for managing Microsoft Intune configurations across multiple cloud environments (Commercial, GCC, GCC-High, DoD). It is a ground-up remake of [Micke-K/IntuneManagement](https://github.com/Micke-K/IntuneManagement) (PowerShell/WPF).

**Current Status:** Planning complete, pre-implementation. No source code exists yet — only planning documentation.

## Repository Structure

```
IntuneGUI/
├── CLAUDE.md                  # This file
├── README.md                  # Project overview
├── .gitignore                 # OS/editor exclusions
└── docs/
    ├── ARCHITECTURE.md        # Technical architecture & design decisions
    ├── DECISIONS.md           # 15 recorded architectural decisions with rationale
    ├── PLANNING.md            # 6-phase development plan with success criteria
    └── NEXT-STEPS.md          # Pre-coding checklist, Phase 1 guide, resources
```

### Planned Source Structure (not yet created)

```
src/
├── IntuneManager.Core/            # Shared business logic (.NET 8 class library)
│   ├── Auth/                      # Authentication providers (Azure.Identity)
│   ├── Services/                  # Graph API services
│   ├── Models/                    # Data models, enums, DTOs
│   └── Extensions/                # Utility extensions
├── IntuneManager.Desktop/         # Avalonia UI application
│   ├── Views/                     # XAML views (.axaml files)
│   ├── ViewModels/                # MVVM view models
│   └── App.axaml                  # Application entry point
└── IntuneManager.Cli/             # CLI tool (Phase 6)

tests/
└── IntuneManager.Core.Tests/      # xUnit test project
```

## Technology Stack

| Component | Technology | Version |
|-----------|-----------|---------|
| Runtime | .NET 8 (LTS) | 8.0.x |
| Language | C# 12 | — |
| UI Framework | Avalonia | 11.2.x |
| MVVM Toolkit | CommunityToolkit.Mvvm | 8.3.x |
| Authentication | Azure.Identity | 1.13.x |
| Graph API | Microsoft.Graph SDK | 5.88.x |
| JSON | System.Text.Json | 8.0.x |
| DI | Microsoft.Extensions.DependencyInjection | 8.0.x |
| Logging | Serilog (Phase 6) | 4.1.x |
| Testing | xUnit | — |

## Development Phases

The project follows a 6-phase iterative MVP approach:

1. **Phase 1** — Foundation: Single tenant, Device Configurations CRUD, basic UI
2. **Phase 2** — Multi-Cloud + Profile System: All gov clouds, saved profiles
3. **Phase 3** — Expand Object Types: Compliance, Settings Catalog, Apps, CA
4. **Phase 4** — Bulk Operations: Multi-select export/import, migration table, dependency handling
5. **Phase 5** — Auth Expansion: Certificate auth, Managed Identity
6. **Phase 6** — Polish & Docker: Logging, CLI mode, containerization

See `docs/PLANNING.md` for full phase details and success criteria.

## Key Architecture Decisions

These are documented in `docs/DECISIONS.md` and `docs/ARCHITECTURE.md`. The most important ones:

- **Azure.Identity over MSAL** — Use `TokenCredential` abstraction, no direct MSAL dependency
- **Separate app registration per cloud** — GCC-High/DoD require isolated registrations
- **Microsoft.Graph SDK models directly** — No custom model layer; custom DTOs only for export edge cases
- **MVVM with CommunityToolkit.Mvvm** — Source generators, clean separation, testable ViewModels
- **Read-only backward compatibility** — Can import PowerShell version JSON exports, but forward compat not required
- **Windows-first** — Phases 1-5 target Windows; Linux Docker in Phase 6
- **Central Package Management** — `Directory.Packages.props` for version pinning

## Coding Conventions

### C# Style

- Use C# 12 features: primary constructors, collection expressions, required members, file-scoped types
- Async/await for all I/O operations
- Nullable reference types enabled
- Follow standard .NET naming conventions (PascalCase for public members, camelCase for private fields with `_` prefix)

### Naming Patterns

- **Namespaces:** `IntuneManager.*` prefix
- **Interfaces:** `I{Name}` (e.g., `IIntuneService`, `IAuthenticationProvider`)
- **Services:** `I{Name}Service` / `{Name}Service`
- **ViewModels:** `{ViewName}ViewModel`
- **Views:** `{Name}View.axaml` or `{Name}Window.axaml`

### DI Service Lifetimes

- **Singleton:** `GraphClientFactory`, `ProfileManager`
- **Scoped:** `IntuneService` (per active profile)
- **Transient:** ViewModels

### Error Handling

- Translate Graph API errors to user-friendly messages
- Retry with exponential backoff: 1s, 2s, 4s, 8s, 16s (max 5 retries)
- Respect `Retry-After` headers from Graph API
- Support cancellation tokens for long operations

## Build & Run

### Prerequisites

- .NET 8 SDK
- Visual Studio 2022 or JetBrains Rider

### Commands (once source exists)

```bash
# Create solution structure
dotnet new sln -n IntuneManager
dotnet new classlib -n IntuneManager.Core -f net8.0
dotnet new avalonia.app -n IntuneManager.Desktop -f net8.0
dotnet new xunit -n IntuneManager.Core.Tests -f net8.0

# Build
dotnet build

# Run tests
dotnet test

# Run desktop app
dotnet run --project src/IntuneManager.Desktop
```

### Package Installation

```bash
# Core project
dotnet add IntuneManager.Core package Azure.Identity
dotnet add IntuneManager.Core package Microsoft.Graph
dotnet add IntuneManager.Core package System.Text.Json
dotnet add IntuneManager.Core package Microsoft.Extensions.DependencyInjection

# Desktop project
dotnet add IntuneManager.Desktop package CommunityToolkit.Mvvm
dotnet add IntuneManager.Desktop package Microsoft.Extensions.DependencyInjection
```

## Testing

- **Framework:** xUnit
- **Coverage target:** >70% for Core library
- **Focus areas:** Service logic, auth provider selection, JSON serialization, migration table
- **UI testing:** Manual (Avalonia UI test tooling is immature)
- **Integration tests:** Deferred to Phase 6+ (requires test tenant)

## Multi-Cloud Configuration

| Cloud | Graph Endpoint | Authority Host |
|-------|---------------|----------------|
| Commercial | `https://graph.microsoft.com` | `AzureAuthorityHosts.AzurePublicCloud` |
| GCC | `https://graph.microsoft.com` | `AzureAuthorityHosts.AzurePublicCloud` |
| GCC-High | `https://graph.microsoft.us` | `AzureAuthorityHosts.AzureGovernment` |
| DoD | `https://dod-graph.microsoft.us` | `AzureAuthorityHosts.AzureGovernment` |

## Export/Import Format

Maintains read-only compatibility with the PowerShell version's JSON export format:

```
ExportFolder/
├── DeviceConfigurations/
│   ├── Policy1.json
│   └── Policy2.json
├── CompliancePolicies/
│   └── Policy3.json
├── Groups/
│   └── Group1.json
└── migration-table.json
```

## Profile Storage

- **Windows:** `%LOCALAPPDATA%\IntuneManager\profiles.json`
- **Linux:** `~/.config/IntuneManager/profiles.json`
- **macOS:** `~/Library/Application Support/IntuneManager/profiles.json`
- Sensitive fields encrypted with platform-native encryption (DPAPI on Windows)

## Security Considerations

- Never log or store access tokens
- Use platform-native credential storage (DPAPI / Keychain / libsecret)
- Store certificate thumbprints only, never private keys
- HTTPS only, certificate validation enabled
- Profiles are per-user encrypted and not portable

## Key Reference Documents

- `docs/ARCHITECTURE.md` — Full technical architecture with implementation patterns
- `docs/PLANNING.md` — Phase-by-phase plan with success criteria
- `docs/DECISIONS.md` — 15 architectural decisions with rationale and alternatives considered
- `docs/NEXT-STEPS.md` — Pre-coding checklist, Phase 1 implementation guide, validation checklist

## Important Context for AI Assistants

- This is a **pre-implementation project**. No `.sln`, `.csproj`, or source files exist yet. All content is planning documentation.
- When generating code, follow the planned structure in `docs/ARCHITECTURE.md` and `docs/NEXT-STEPS.md`.
- The project targets **Avalonia UI** (not WPF). XAML files use the `.axaml` extension and Avalonia-specific namespaces.
- Use `Microsoft.Graph` SDK models directly — avoid creating redundant model classes.
- All Graph API calls must support multi-cloud endpoints configured per profile.
- Export JSON format must be backward-compatible with the PowerShell version ([Micke-K/IntuneManagement](https://github.com/Micke-K/IntuneManagement)).
- Use `Azure.Identity` credential types, not raw MSAL. The `TokenCredential` abstraction enables future auth method expansion.
- The project is a hobby/personal project — keep solutions pragmatic and avoid over-engineering.
