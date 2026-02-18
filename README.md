# Intune Commander - .NET Remake

A cross-platform Intune management tool built with .NET 10 and Avalonia UI, designed to overcome PowerShell WPF limitations in threading, UI refresh, and data caching.

## Project Overview

### Goals
- **Multi-cloud support:** Commercial, GCC, GCC-High, DoD tenants
- **Multi-tenant:** Easy switching between tenant environments with profile management
- **Native performance:** Compiled .NET code eliminates PowerShell threading issues
- **Cross-platform:** Linux and macOS support via Avalonia (planned)
- **Backward compatible:** Import/export compatible with PowerShell version JSON format

## Technology Stack

| Component | Technology |
|-----------|-----------|
| Runtime | .NET 10, C# 12 |
| UI Framework | Avalonia 11.3.x (`.axaml` files, FluentTheme) |
| MVVM | CommunityToolkit.Mvvm 8.2.x |
| Authentication | Azure.Identity 1.17.x |
| Graph API | **Microsoft.Graph.Beta** 5.130.x-preview |
| Cache | LiteDB 5.0.x (AES-encrypted via DataProtection) |
| Charts | LiveChartsCore.SkiaSharpView.Avalonia |
| DI | Microsoft.Extensions.DependencyInjection 10.0.x |
| Testing | xUnit |

> **Note:** This project uses `Microsoft.Graph.Beta`, **not** the stable `Microsoft.Graph` package. All models and `GraphServiceClient` come from `Microsoft.Graph.Beta.*`.

## Getting Started

### Prerequisites
- .NET 10 SDK
- Visual Studio 2022, JetBrains Rider, or VS Code with C# Dev Kit
- An Azure AD app registration with appropriate Microsoft Graph Beta permissions

### Build & Run

```bash
# Build all projects
dotnet build

# Run unit tests
dotnet test

# Run a single test class
dotnet test --filter "FullyQualifiedName~ProfileServiceTests"

# Run the desktop application
dotnet run --project src/IntuneManager.Desktop
```

### App Registration Setup

1. Go to **Azure Portal → Entra ID → App Registrations → New registration**
2. Name your app (e.g. `IntuneCommander-Dev`)
3. Set **Redirect URI** to `http://localhost` (Mobile and desktop applications)
4. Under **API permissions**, add `Microsoft Graph → Delegated → DeviceManagementConfiguration.ReadWrite.All` and related Intune scopes
5. Grant admin consent for the tenant

For **Government clouds** (GCC-High, DoD), register separate apps in the respective Azure portals (`portal.azure.us`, `portal.apps.mil`).

### Authentication Methods

| Method | Description |
|--------|-------------|
| **Interactive** (default) | Browser popup with persistent token cache |
| **Client Secret** | Unattended service principal authentication |

## Architecture Summary

```
src/
  IntuneManager.Core/        # Business logic (.NET 10 class library)
    Auth/                    # Azure.Identity credential providers
    Models/                  # Enums, TenantProfile, ProfileStore, DTOs, CacheEntry
    Services/                # 30+ Graph API services + ProfileService, CacheService, ExportService
    Extensions/              # DI registration (AddIntuneManagerCore)
  IntuneManager.Desktop/     # Avalonia UI application
    Views/                   # MainWindow, LoginView, OverviewView, DebugLogWindow, RawJsonWindow
    ViewModels/              # MainWindowViewModel (+ feature partial files), LoginViewModel, OverviewViewModel
    Services/                # DebugLogService (in-memory log, UI-thread-safe)
    Converters/              # ComputedColumnConverters
tests/
  IntuneManager.Core.Tests/  # xUnit tests (200+ cases)
```

Graph API services are created **after** authentication (`new XxxService(graphClient)`) — they are not registered in DI at startup.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for full architectural decisions.

## Supported Intune Object Types

Device Configurations · Compliance Policies · Settings Catalog · Endpoint Security ·
Administrative Templates · Enrollment Configurations · App Protection Policies ·
Managed Device App Configurations · Targeted Managed App Configurations ·
Terms and Conditions · Scope Tags · Role Definitions · Intune Branding · Azure Branding ·
Autopilot Profiles · Device Health Scripts · Mac Custom Attributes · Feature Updates ·
Named Locations · Authentication Strengths · Authentication Contexts · Terms of Use ·
Conditional Access · Assignment Filters · Policy Sets · Applications ·
Application Assignments · Dynamic Groups · Assigned Groups

## Acknowledgments

This project is a ground-up remake of [Micke-K/IntuneManagement](https://github.com/Micke-K/IntuneManagement), a PowerShell/WPF-based Intune management tool.
