# IntuneManager - .NET Remake

A cross-platform Intune management tool built with .NET 8 and Avalonia UI, designed to overcome PowerShell WPF limitations in threading, UI refresh, and data caching.

## Project Overview

### Origin
This project is a ground-up remake of [Micke-K/IntuneManagement](https://github.com/Micke-K/IntuneManagement), a PowerShell/WPF-based Intune management tool.

### Goals
- **Multi-cloud support:** Commercial, GCC, GCC-High, DoD tenants
- **Multi-tenant:** Easy switching between tenant environments
- **Native performance:** Compiled .NET code eliminates PowerShell threading issues
- **Cross-platform:** Linux deployment with Docker support (Windows-first development)
- **Backward compatible:** Import/export compatible with PowerShell version JSON format

### Non-Goals (Removed from original)
- ADMX import tooling
- Web-based UI
- Exact feature parity with PowerShell version

## Technology Stack

### Core Technologies
- **.NET 8** - Latest LTS framework
- **C#** - Primary language
- **Avalonia 11.x** - Cross-platform XAML UI framework
- **Azure.Identity** - Modern authentication (no direct MSAL dependency)
- **Microsoft.Graph 5.x** - Official Graph SDK with native multi-cloud support

### Key Libraries
- **CommunityToolkit.Mvvm** - MVVM source generators
- **System.Text.Json** - JSON serialization for export/import
- **xUnit** - Unit testing framework

### Deployment Targets
- **Phase 1-5:** Windows desktop application
- **Phase 6+:** Docker containerization for headless operations

## Architecture Decisions

### Authentication
- **Phase 1:** Interactive browser login (Commercial cloud)
- **Phase 2:** Multi-cloud profile system
- **Phase 5:** Certificate and Managed Identity support
- **Profile-based:** Named configurations stored locally with encryption

### Object Model
- Use Microsoft.Graph SDK models directly where possible
- Custom DTOs only for export/import serialization
- Maintain JSON schema compatibility with PowerShell version

### Export/Import Strategy
- **Backward compatible:** Read PowerShell version JSON exports
- **Migration table:** Preserve ID mapping concept for cross-tenant imports
- **Dependency resolution:** Handle object dependencies (Policy Sets, assignments, etc.)

## Project Structure

```
IntuneManager/
├── docs/                              # Planning and architecture docs
│   ├── PLANNING.md                    # Detailed project plan
│   ├── ARCHITECTURE.md                # Technical architecture
│   └── DECISIONS.md                   # Key decision log
├── src/
│   ├── IntuneManager.Core/            # Shared business logic
│   │   ├── Auth/                      # Authentication providers
│   │   ├── Services/                  # Graph API services
│   │   ├── Models/                    # Data models
│   │   └── Extensions/                # Utility extensions
│   ├── IntuneManager.Desktop/         # Avalonia UI application
│   │   ├── Views/                     # XAML views
│   │   ├── ViewModels/                # MVVM view models
│   │   └── App.axaml                  # Application entry point
│   └── IntuneManager.Cli/             # CLI tool (Phase 6)
└── tests/
    └── IntuneManager.Core.Tests/      # Unit tests
```

## Current Status

**Stage:** Planning Complete  
**Next Step:** Development Environment Setup

## Quick Links

- [Detailed Project Plan](docs/PLANNING.md)
- [Technical Architecture](docs/ARCHITECTURE.md)
- [Decision Log](docs/DECISIONS.md)

## Getting Started

### Prerequisites
- .NET 8 SDK
- Visual Studio 2022 or JetBrains Rider
- Git

### Initial Setup
(To be documented after Phase 1 implementation)

## License

TBD

## Acknowledgments

Based on [Micke-K/IntuneManagement](https://github.com/Micke-K/IntuneManagement) - PowerShell/WPF implementation
