# Next Steps - Getting Started

## Immediate Actions (Before Writing Code)

### 1. Development Environment Setup

**Required Software:**
- [ ] Install Visual Studio 2022 (Community Edition or higher)
  - Workload: .NET desktop development
  - Workload: .NET Multi-platform App UI development (for Avalonia)
- [ ] Install .NET 10 SDK
  - Download: https://dotnet.microsoft.com/download/dotnet/8.0
  - Verify: `dotnet --version` should show 10.0.x
- [ ] Install Git
  - Download: https://git-scm.com/downloads
  - Configure: `git config --global user.name "Your Name"`
  - Configure: `git config --global user.email "your@email.com"`

**Optional but Recommended:**
- [ ] Install JetBrains Rider (alternative to Visual Studio)
- [ ] Install Avalonia XAML Extension for Visual Studio
- [ ] Install GitHub Desktop (if not comfortable with Git CLI)

### 2. Azure/Intune Environment Preparation

**Test Tenant:**
- [ ] Identify test tenant to use for development
  - Recommendation: Use a demo/dev tenant, NOT production
  - Option: Microsoft 365 Developer Program (free dev tenant)
    - https://developer.microsoft.com/microsoft-365/dev-program

**App Registration (Commercial Cloud):**
- [ ] Register application in Azure Portal
  - Portal: https://portal.azure.com
  - Navigate: Azure Active Directory → App registrations → New registration
  - Name: "IntuneManager-Dev"
  - Redirect URI: Public client/native → `http://localhost`
  
- [ ] Configure API permissions:
  - Microsoft Graph (Delegated):
    - `DeviceManagementConfiguration.ReadWrite.All`
    - `DeviceManagementApps.ReadWrite.All`
    - `DeviceManagementManagedDevices.ReadWrite.All`
    - `DeviceManagementServiceConfig.ReadWrite.All`
    - `Directory.Read.All`
    - `Group.ReadWrite.All`
  
- [ ] Grant admin consent (if required by tenant)

- [ ] Copy values for development:
  - Application (client) ID: `________________`
  - Directory (tenant) ID: `________________`

### 3. Repository Setup

**Create Private GitHub Repository:**
- [ ] Go to https://github.com/new
- [ ] Repository name: `IntuneManager` (or preferred name)
- [ ] Visibility: Private
- [ ] Initialize with README: No (we'll push existing docs)
- [ ] Create repository

**Initialize Local Repository:**
```bash
# Navigate to planning directory
cd /path/to/IntuneManager-Planning

# Initialize git
git init

# Add all planning documents
git add .

# Initial commit
git commit -m "Initial planning documents"

# Add remote (replace with your repo URL)
git remote add origin https://github.com/YOUR-USERNAME/IntuneManager.git

# Push to GitHub
git branch -M main
git push -u origin main
```

### 4. Project Structure Creation

**Create Solution Structure:**
```bash
# Navigate to where you want your code (not the planning folder)
mkdir IntuneManager
cd IntuneManager

# Create solution file
dotnet new sln -n IntuneManager

# Create projects
dotnet new classlib -n IntuneManager.Core -f net10.0
dotnet new avalonia.app -n IntuneManager.Desktop -f net10.0
dotnet new xunit -n IntuneManager.Core.Tests -f net10.0

# Add projects to solution
dotnet sln add Intune.Commander.Core/Intune.Commander.Core.csproj
dotnet sln add Intune.Commander.Desktop/Intune.Commander.Desktop.csproj
dotnet sln add Intune.Commander.Core.Tests/Intune.Commander.Core.Tests.csproj

# Add project references
dotnet add Intune.Commander.Desktop/Intune.Commander.Desktop.csproj reference Intune.Commander.Core/Intune.Commander.Core.csproj
dotnet add Intune.Commander.Core.Tests/Intune.Commander.Core.Tests.csproj reference Intune.Commander.Core/Intune.Commander.Core.csproj
```

### 5. Install Initial Dependencies

**IntuneManager.Core:**
```bash
cd IntuneManager.Core

dotnet add package Azure.Identity --version 1.13.1
dotnet add package Microsoft.Graph --version 5.88.0
dotnet add package System.Text.Json --version 8.0.5
dotnet add package Microsoft.Extensions.DependencyInjection --version 8.0.1

cd ..
```

**IntuneManager.Desktop:**
```bash
cd IntuneManager.Desktop

dotnet add package CommunityToolkit.Mvvm --version 8.3.2
dotnet add package Microsoft.Extensions.DependencyInjection --version 8.0.1

cd ..
```

### 6. Configuration Files

**Create .gitignore:**
```gitignore
# Build results
bin/
obj/
[Dd]ebug/
[Rr]elease/

# Visual Studio
.vs/
*.user
*.suo
*.userosscache
*.sln.docstates

# Rider
.idea/
*.sln.iml

# User-specific files
*.rsuser
*.userprefs

# Test results
TestResults/
*.trx

# NuGet
*.nupkg
*.snupkg
packages/
.nuget/

# Environment files
appsettings.local.json
appsettings.*.local.json

# Logs
logs/
*.log

# OS files
.DS_Store
Thumbs.db

# Sensitive data
profiles.json
*.pfx
*.p12
```

**Create Directory.Build.props (optional - for central package management):**
```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>
</Project>
```

---

## Phase 1 Implementation Checklist

### Week 1: Core Infrastructure

**Day 1-2: Authentication**
- [ ] Create `CloudEnvironment` enum
- [ ] Create `TenantConfiguration` model
- [ ] Create `IAuthenticationProvider` interface
- [ ] Implement `InteractiveBrowserAuthProvider`
- [ ] Create `GraphClientFactory`
- [ ] Write unit tests for auth provider

**Day 3-4: Graph Service**
- [ ] Create `IIntuneService` interface
- [ ] Implement `IntuneService` for Device Configurations
- [ ] Methods: ListAsync, GetAsync, CreateAsync, UpdateAsync, DeleteAsync
- [ ] Handle Graph SDK exceptions
- [ ] Write unit tests with mocked Graph client

**Day 5: Export/Import Service**
- [ ] Create `IExportService` interface
- [ ] Implement `ExportService`
  - Export single object to JSON
  - Create folder structure
  - Generate migration table
- [ ] Create `IImportService` interface
- [ ] Implement `ImportService`
  - Read JSON files
  - Parse migration table
  - Import object via Graph

### Week 2: UI Implementation

**Day 1-2: Login View**
- [ ] Create `LoginView.axaml`
- [ ] Create `LoginViewModel`
- [ ] Tenant ID input field
- [ ] Login button → triggers auth
- [ ] Display authentication status
- [ ] Navigate to main window on success

**Day 3-4: Main Window & Object List**
- [ ] Create `MainWindow.axaml`
- [ ] Create `MainWindowViewModel`
- [ ] Object list DataGrid (bound to Device Configurations)
- [ ] Refresh button
- [ ] Detail pane for selected object
- [ ] Export selected button
- [ ] Import button (file picker)

**Day 5: Testing & Polish**
- [ ] End-to-end test: Login → List → Export → Import
- [ ] Error handling for all user actions
- [ ] Loading indicators
- [ ] Basic styling (not critical for Phase 1)

---

## Validation Checklist (End of Phase 1)

Before proceeding to Phase 2, verify:

**Authentication:**
- [ ] Can successfully authenticate to Commercial tenant
- [ ] Token is cached (second launch doesn't require re-auth)
- [ ] Error message shown if authentication fails

**Graph API:**
- [ ] Can retrieve Device Configurations from test tenant
- [ ] Device Configuration details display correctly
- [ ] Handles tenant with zero Device Configurations gracefully

**Export:**
- [ ] Exported JSON matches PowerShell format structure
- [ ] Can export multiple objects (select 2-3)
- [ ] Folder structure created correctly
- [ ] Migration table JSON is valid

**Import:**
- [ ] Can import previously exported JSON
- [ ] Object appears in Intune portal after import
- [ ] Migration table is updated with new ID
- [ ] Can import PowerShell-exported JSON (backward compatibility test)

**UI/UX:**
- [ ] Application doesn't crash on normal operations
- [ ] Loading states show during API calls
- [ ] Error messages are user-friendly
- [ ] Can navigate back from main window to login

---

## Resources & References

### Official Documentation
- Microsoft Graph .NET SDK: https://github.com/microsoftgraph/msgraph-sdk-dotnet
- Azure Identity: https://learn.microsoft.com/en-us/dotnet/api/azure.identity
- Avalonia UI Docs: https://docs.avaloniaui.net/
- Microsoft Graph API Reference: https://learn.microsoft.com/en-us/graph/api/overview

### Sample Code
- Graph SDK Samples: https://github.com/microsoftgraph/msgraph-sdk-dotnet/tree/dev/samples
- Avalonia Samples: https://github.com/AvaloniaUI/Avalonia.Samples
- Original PowerShell Version: https://github.com/Micke-K/IntuneManagement

### Community
- Avalonia Discord: https://discord.gg/avaloniaui
- Microsoft Graph GitHub Issues: https://github.com/microsoftgraph/msgraph-sdk-dotnet/issues
- r/dotnet: https://reddit.com/r/dotnet

---

## Questions to Answer Before Coding

1. **App Registration Client ID:**
   - Will you use the same Client ID as PowerShell version?
   - Or create new app registration for .NET version?

2. **Test Data:**
   - Do you have PowerShell-exported JSON files to test import?
   - If not, can you run PowerShell version once to generate test data?

3. **Development Cadence:**
   - How many hours per week can you dedicate?
   - Affects timeline estimates

4. **Code Review:**
   - Working solo or want checkpoint reviews?
   - Can provide architecture review after Phase 1

---

## Ready to Start?

Once you've completed the checklist items above, you're ready to begin Phase 1 implementation!

**Recommended starting point:** Authentication infrastructure (Week 1, Day 1-2)

Would you like detailed implementation guidance for any specific component?
