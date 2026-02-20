# Architecture & Technical Decisions

## Authentication Architecture

### Decision: Azure.Identity over MSAL
**Rationale:**
- Modern Microsoft-recommended approach
- Built-in multi-cloud support
- Multiple credential types with automatic fallback
- No direct MSAL dependency (uses Microsoft.Identity abstractions)
- Cleaner API surface

**Implementation:**
- Use `TokenCredential` abstraction
- Support multiple credential types via strategy pattern
- Profile-based credential selection

### Supported Credential Types

#### Implemented
```
InteractiveBrowserCredential
- Browser popup authentication
- Supports all clouds via AuthorityHost configuration
- Persistent token cache per profile (MSAL token cache)

ClientSecretCredential
- Service-principal authentication
- Requires ClientSecret field in TenantProfile
- Suitable for automated/unattended scenarios
```

#### Deferred (not yet implemented)
```
ClientCertificateCredential  — deferred; requires cert-store integration
ManagedIdentityCredential    — deferred; not applicable for desktop use
```

`InteractiveBrowserAuthProvider` currently throws `NotSupportedException` for any
unsupported auth method (including certificate/managed identity), so unsupported
methods fail fast instead of falling back to interactive authentication.

---

## Multi-Cloud Strategy

### Cloud Endpoints

| Cloud | Graph Endpoint | Authority Host |
|-------|---------------|----------------|
| Commercial | `https://graph.microsoft.com` | `AzureAuthorityHosts.AzurePublicCloud` |
| GCC | `https://graph.microsoft.com` | `AzureAuthorityHosts.AzurePublicCloud` |
| GCC-High | `https://graph.microsoft.us` | `AzureAuthorityHosts.AzureGovernment` |
| DoD | `https://dod-graph.microsoft.us` | `AzureAuthorityHosts.AzureGovernment` |

### App Registration Strategy
**Decision:** Separate app registration per cloud

**Rationale:**
- GCC-High and DoD require registrations in separate Azure portals
- Isolation between environments
- Simpler permission management
- Avoids cross-cloud authentication errors

**Implementation:**
- Each profile stores cloud-specific ClientId
- User must register app in each cloud they use
- Documentation provides registration instructions per cloud

---

## Profile Management

### Profile Storage Location

**Windows:** `%LOCALAPPDATA%\Intune.Commander\profiles.json`
**Linux:** `~/.config/Intune.Commander/profiles.json`
**macOS:** `~/Library/Application Support/Intune.Commander/profiles.json`

### Profile Schema
```json
{
  "profiles": [
    {
      "id": "guid",
      "name": "Contoso-Prod-GCCHigh",
      "tenantId": "tenant-guid",
      "clientId": "app-guid",
      "cloud": "GCCHigh",
      "authMethod": "Interactive",
      "clientSecret": null,
      "certificateThumbprint": null,
      "lastUsed": "2025-02-14T10:30:00Z"
    }
  ],
  "activeProfileId": "guid"
}
```

### Encryption Strategy
**Decision:** ASP.NET DataProtection with file-system key ring

**Rationale:**
- Cross-platform encryption using a single consistent API
- DPAPI-protected keys on Windows; file-system protected on macOS/Linux
- Keys persist to `%LocalAppData%\Intune.Commander\keys` directory

**Implementation:**
- Profile file encrypted via `Microsoft.AspNetCore.DataProtection`
- File is Base64-encoded with `INTUNEMANAGER_ENC:` prefix; plaintext files are migrated on next save
- `ClientSecret` stored in the encrypted profile; never appears in plaintext on disk

---

## Export/Import Format

### Decision: Maintain PowerShell JSON Compatibility (Read-only)
**Rationale:**
- Users may have existing PowerShell exports
- Migration path from PowerShell version
- Proven format structure

**Format:**
```
ExportFolder/
├── DeviceConfigurations/
│   ├── Policy1.json
│   └── Policy2.json
├── CompliancePolicies/
│   └── Policy3.json
├── Groups/
│   ├── Group1.json
│   └── Group2.json
└── migration-table.json
```

### Migration Table Format
```json
{
  "objectType": "DeviceConfiguration",
  "originalId": "source-tenant-id",
  "newId": "destination-tenant-id",
  "name": "Policy Name",
  "exportedAt": "2025-02-14T10:00:00Z"
}
```

**Backward Compatibility:** .NET version can **read** PowerShell exports  
**Forward Compatibility:** Not required - PowerShell version doesn't need to read .NET exports

---

## Object Model Strategy

### Decision: Use Microsoft.Graph SDK models directly

**Rationale:**
- Strongly typed
- Automatic updates when Graph API changes
- No manual mapping code
- Built-in serialization

**Exception Cases:**
- Export/Import DTOs when SDK model includes non-serializable properties
- Custom wrapper models for UI binding requirements

### Object Type Mapping

| PowerShell Folder | Graph Entity | SDK Model |
|-------------------|--------------|-----------|
| DeviceConfigurations | `deviceManagement/deviceConfigurations` | `DeviceConfiguration` |
| CompliancePolicies | `deviceManagement/deviceCompliancePolicies` | `DeviceCompliancePolicy` |
| ConfigurationPolicies | `deviceManagement/configurationPolicies` | `DeviceManagementConfigurationPolicy` |
| ManagedAppPolicies | `deviceAppManagement/managedAppPolicies` | `ManagedAppPolicy` |

---

## UI Architecture

### MVVM Pattern
**Framework:** CommunityToolkit.Mvvm

**Rationale:**
- Source generators eliminate boilerplate
- Industry standard for Avalonia/WPF
- Clean separation of concerns
- Testable view models

**Structure:**
```
Views/
  MainWindow.axaml
  LoginView.axaml
  OverviewView.axaml
  GroupLookupWindow.axaml
  DebugLogWindow.axaml
  RawJsonWindow.axaml

ViewModels/
  MainWindowViewModel.cs
  LoginViewModel.cs
  OverviewViewModel.cs
  GroupLookupViewModel.cs
  DebugLogViewModel.cs
  NavCategory.cs / DataGridColumnConfig.cs / row types
```

### Dependency Injection
**Framework:** Microsoft.Extensions.DependencyInjection

**Rationale:**
- Built into .NET
- Standard service registration
- Easy testing with mock services

**Service Lifetime (actual):**
- Singleton: `IAuthenticationProvider`, `IntuneGraphClientFactory`, `ProfileService`, `IProfileEncryptionService`, `ICacheService`
- Transient: `IExportService`, `MainWindowViewModel`
- **Not in DI:** Graph API services — created with `new XxxService(graphClient)` in `MainWindowViewModel.ConnectToProfile(...)` after authentication succeeds

---

## Error Handling Strategy

### Graph API Errors
**Decision:** Translate to user-friendly messages

**Common Scenarios:**
| Graph Error | User Message |
|-------------|--------------|
| `Forbidden` (403) | "Missing permission: DeviceManagementConfiguration.ReadWrite.All" |
| `TooManyRequests` (429) | "Request throttled. Retrying in X seconds..." |
| `Unauthorized` (401) | "Session expired. Please sign in again." |
| `NotFound` (404) | "Object not found. It may have been deleted." |

### Retry Strategy
- Exponential backoff: 1s, 2s, 4s, 8s, 16s
- Respect `Retry-After` headers
- Maximum 5 retries
- Cancel on user request

---

## Logging Strategy

### Framework: DebugLogService (in-memory)
**Implementation:**
- `DebugLogService.Instance` singleton with `ObservableCollection<string> Entries` (capped at 2 000)
- All log dispatches to UI thread via `Dispatcher.UIThread.Post`
- Exposed in the UI via `DebugLogWindow`
- Use `DebugLog.Log(category, message)` / `DebugLog.LogError(...)` throughout ViewModels

**Note:** No file-based logging is currently implemented; the plan to adopt Serilog is deferred.

---

## Testing Strategy

### Unit Tests
**Framework:** xUnit
**Coverage Target:** 40% line coverage (enforced in CI via coverlet)

**Focus Areas:**
- Service logic (IntuneService, ExportService)
- Authentication provider selection
- JSON serialization/deserialization
- Migration table logic

### Integration Tests
**Status:** Implemented — runs in CI (`ci-integration.yml`) against a live tenant

**Requirements:**
- Test tenant (non-production)
- App registration with test permissions (`AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET` secrets)
- Automated cleanup after tests (`IntTest_AutoCleanup_` prefix on created objects)
- Base class: `GraphIntegrationTestBase` with `ShouldSkip()`, `CreateService<T>()`, `RetryOnTransientFailureAsync()`

### Manual Testing
**Every Phase:**
- Happy path scenarios
- Error scenarios
- Cross-cloud testing (Commercial + GCC-High minimum)

---

## Performance Considerations

### Graph API Optimization
1. **Batch requests** where supported (Phase 6)
2. **Select queries** - only request needed properties
3. **Filter queries** - reduce payload size
4. **Parallel requests** with concurrency limits

### UI Responsiveness
1. **Async/await** for all I/O operations
2. **Background tasks** for bulk operations
3. **Progress reporting** via IProgress<T>
4. **Cancellation tokens** for long operations

### Memory Management
1. **Streaming** for large exports
2. **Dispose** Graph clients properly
3. **Weak events** in ViewModels to prevent leaks

---

## Security Considerations

### Token Storage
- Never log access tokens
- Clear tokens on logout
- Encrypted profile storage
- No secrets in config files

### Certificate Handling
- Certificate-based auth is **deferred** — no certificate credential code paths exist yet
- When implemented: store thumbprints only, not private keys; use Windows cert store

### Network Security
- HTTPS only
- Certificate validation enabled
- No proxy credential storage

---

## Build & Deployment

### Build Configuration
**Debug:**
- Verbose logging enabled
- Source maps included
- No optimizations

**Release:**
- Information logging only
- Optimizations enabled
- Trimmed assemblies (single-file if possible)

### Deployment Methods

#### Phase 1-5: Desktop App
- Self-contained executable
- Include .NET runtime
- Windows x64 only initially

#### Phase 6: Docker
- Multi-stage Dockerfile
- Base: `mcr.microsoft.com/dotnet/runtime:8.0`
- Final size target: <250MB
- Linux x64

### Versioning
**Scheme:** Semantic Versioning (SemVer)
- Major.Minor.Patch
- Example: 1.0.0, 1.1.0, 1.1.1

**Pre-release tags:**
- `alpha` - Phase 1-2
- `beta` - Phase 3-5
- `rc` - Phase 6 (release candidate)
- (none) - Production release

---

## Technology Constraints

### .NET Version
**Decision:** .NET 10 (LTS)

**Rationale:**
- Long-term support until Nov 2026
- Latest performance improvements
- Required for latest Avalonia versions

### C# Language Version
**Decision:** C# 12

**Features Used:**
- Primary constructors
- Collection expressions
- Required members
- File-scoped types

### Minimum Requirements
**OS:** Windows 10 1809+ (initial target)  
**RAM:** 512MB minimum, 1GB recommended  
**.NET:** Bundled with app (self-contained)

---

## External Dependencies

### Actual NuGet Packages (current)

**Intune.Commander.Core:**
```xml
<PackageReference Include="Azure.Identity" Version="1.17.1" />
<PackageReference Include="LiteDB" Version="5.0.21" />
<PackageReference Include="Microsoft.AspNetCore.DataProtection" Version="8.0.12" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.3" />
<PackageReference Include="Microsoft.Graph.Beta" Version="5.130.0-preview" />
```

**Intune.Commander.Desktop:**
```xml
<PackageReference Include="Avalonia" Version="11.3.12" />
<PackageReference Include="Avalonia.Controls.DataGrid" Version="11.3.12" />
<PackageReference Include="Avalonia.Themes.Fluent" Version="11.3.12" />
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.2.1" />
<PackageReference Include="LiveChartsCore.SkiaSharpView.Avalonia" Version="2.0.0-rc6.1" />
<PackageReference Include="MessageBox.Avalonia" Version="3.2.0" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.3" />
```

> **Important:** The project uses `Microsoft.Graph.Beta`, **not** the stable `Microsoft.Graph` package.

### Dependency Management
- Package versions are pinned directly in each `.csproj` file
- No central package management (Directory.Packages.props)
- Review updates monthly
- Test before updating Graph SDK
