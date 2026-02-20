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
| PowerPoint Export | Syncfusion.Presentation.Net.Core 28.1.x |
| DI | Microsoft.Extensions.DependencyInjection 10.0.x |
| Testing | xUnit |

> **Note:** This project uses `Microsoft.Graph.Beta`, **not** the stable `Microsoft.Graph` package. All models and `GraphServiceClient` come from `Microsoft.Graph.Beta.*`.

## Getting Started

### Prerequisites
- .NET 10 SDK
- Visual Studio 2022, JetBrains Rider, or VS Code with C# Dev Kit
- An Azure AD app registration with appropriate Microsoft Graph permissions (for use with the beta Microsoft Graph SDK/endpoint)
- (Optional) Syncfusion license key for PowerPoint export feature - see [Syncfusion Licensing](#syncfusion-licensing)

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

### Profile Management

Intune Commander stores connection details as **profiles** (tenant ID, client ID, cloud, auth method). Profiles are persisted locally in an encrypted file and never leave your machine.

**Manually adding a profile:**
1. Launch the app — you'll land on the login screen
2. Fill in Tenant ID, Client ID, Cloud, and (optionally) Client Secret
3. Click **Save Profile** to persist it for future sessions

**Importing profiles from a JSON file:**
1. Click **Import Profiles** on the login screen
2. Select a `.json` file containing one or more profile definitions
3. Profiles are merged in — duplicates (same Tenant ID + Client ID) are skipped automatically
4. The imported profiles appear immediately in the **Saved Profiles** dropdown

A ready-to-use template is available at [`.github/profile-template.json`](.github/profile-template.json). Download it, fill in your real Tenant IDs and Client IDs, and import it directly.

**Supported JSON shapes:**

```json
// Array of profiles (recommended)
[
  {
    "name": "Contoso-Prod",
    "tenantId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
    "clientId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
    "cloud": "Commercial",
    "authMethod": "Interactive"
  }
]
```

Valid `cloud` values: `Commercial`, `GCC`, `GCCHigh`, `DoD`
Valid `authMethod` values: `Interactive` (browser popup), `ClientSecret` (include `"clientSecret"` field)

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
    ViewModels/              # MainWindowViewModel, LoginViewModel, OverviewViewModel
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

## Features

### Conditional Access PowerPoint Export

Export Conditional Access policies to a comprehensive PowerPoint presentation with:
- Cover slide with tenant name and export timestamp
- Tenant summary with policy counts
- Policy inventory table showing all policies
- Detailed slides for each policy (conditions, grant controls, assignments)

**Usage:**
1. Navigate to the Conditional Access category
2. Load CA policies
3. Click "📊 Export PowerPoint" button
4. Choose save location
5. Open the generated `.pptx` file

**Current Limitations (v1):**
- Commercial cloud only (GCC/GCC-High/DoD support planned for future release)
- Basic policy details (advanced dependency lookups deferred)
- Feature-level parity with idPowerToys CA decks (not pixel-perfect template matching)

### Syncfusion Licensing

The PowerPoint export feature uses Syncfusion.Presentation.Net.Core, which requires a license key:

**Community License (FREE):**
- For companies/individuals with < $1M annual revenue
- Maximum 5 developers
- Register at: https://www.syncfusion.com/sales/communitylicense

**Commercial License:**
- Required for companies exceeding Community License thresholds
- Visit: https://www.syncfusion.com/sales/products

**Setup:**
Set environment variable: `SYNCFUSION_LICENSE_KEY=your-license-key-here`

The app will run without a license key but will display watermarks on exported PowerPoint files.

## Acknowledgments

This project is a ground-up remake of [Micke-K/IntuneManagement](https://github.com/Micke-K/IntuneManagement), a PowerShell/WPF-based Intune management tool.
Additional thanks to Merill Fernando for originally creating [idPowerToys](https://github.com/merill/idPowerToys).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on submitting pull requests, code standards, and development workflow.

For current PR status and organization, see [PR_STATUS.md](PR_STATUS.md).
