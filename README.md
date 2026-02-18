# Intune Commander - .NET Remake

A cross-platform Intune management tool built with .NET 10 and Avalonia UI, designed to overcome PowerShell WPF limitations in threading, UI refresh, and data caching.

## Project Overview

### Goals
- **Multi-cloud support:** Commercial, GCC, GCC-High, DoD tenants
- **Multi-tenant:** Easy switching between tenant environments with profile management
- **Native performance:** Compiled .NET code eliminates PowerShell threading issues
- **Cross-platform:** Linux and macOS support via Avalonia (EVENTUALLY)
- **Backward compatible:** Import/export compatible with PowerShell version JSON format

## Technology Stack

### Core Technologies
- **.NET 10** - Latest LTS framework
- **C# 12** - Primary language with nullable reference types
- **Avalonia 11.3** - Cross-platform XAML UI framework (FluentTheme)
- **Azure.Identity 1.17** - Modern authentication (Interactive Browser + Client Secret)
- **Microsoft.Graph 5.x** - Official Graph SDK with native multi-cloud support

## Getting Started

### Prerequisites
- .NET 10 SDK
- Visual Studio 2022, JetBrains Rider, or VS Code with C# Dev Kit
- Azure AD app registration with appropriate Microsoft Graph permissions

### Authentication Setup

For client secret authentication:
1. Follow the same steps as above
2. Create a client secret in your Azure AD app registration
3. Grant admin consent for application permissions




## Acknowledgments

This project is a ground-up remake of [Micke-K/IntuneManagement](https://github.com/Micke-K/IntuneManagement), a PowerShell/WPF-based Intune management tool.
Additional thanks to Merill Fernando for originally creating [idPowerToys](https://github.com/merill/idPowerToys).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on submitting pull requests, code standards, and development workflow.

For current PR status and organization, see [PR_STATUS.md](PR_STATUS.md).