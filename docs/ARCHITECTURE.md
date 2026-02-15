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

#### Phase 1-2: Interactive
```
InteractiveBrowserCredential
- Browser popup authentication
- Supports all clouds via AuthorityHost configuration
```

#### Phase 5: Expanded
```
ClientCertificateCredential
- Certificate from Windows cert store
- Non-interactive, suitable for automation

ManagedIdentityCredential
- Azure VM/Container Apps
- Zero configuration when running in Azure
- Fallback to interactive for local dev
```

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

**Windows:** `%LOCALAPPDATA%\IntuneManager\profiles.json`  
**Linux:** `~/.config/IntuneManager/profiles.json`  
**macOS:** `~/Library/Application Support/IntuneManager/profiles.json`

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
      "certificateThumbprint": null,
      "lastUsed": "2025-02-14T10:30:00Z"
    }
  ],
  "activeProfileId": "guid"
}
```

### Encryption Strategy
**Decision:** DPAPI (Windows) / Keychain (macOS) / libsecret (Linux)

**Rationale:**
- Platform-native credential storage
- No custom encryption keys to manage
- OS-level security guarantees

**Implementation:**
- Sensitive fields encrypted before JSON serialization
- Certificate thumbprints stored encrypted
- Never store secrets/passwords (use secure vaults)

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
  ObjectListView.axaml

ViewModels/
  MainWindowViewModel.cs
  LoginViewModel.cs
  ObjectListViewModel.cs
```

### Dependency Injection
**Framework:** Microsoft.Extensions.DependencyInjection

**Rationale:**
- Built into .NET
- Standard service registration
- Easy testing with mock services

**Service Lifetime:**
- Singleton: GraphClientFactory, ProfileManager
- Scoped: IntuneService (per active profile)
- Transient: ViewModels

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

### Framework: Serilog
**Rationale:**
- Structured logging
- Multiple sinks (file, console)
- Easy to configure
- Industry standard

### Log Levels
- **Verbose:** Graph API requests/responses (debug only)
- **Debug:** State transitions, method entry/exit
- **Information:** User actions (login, export, import)
- **Warning:** Recoverable errors (retry attempts)
- **Error:** Failures that block operation
- **Fatal:** Application crash

### Log Location
**Windows:** `%LOCALAPPDATA%\IntuneManager\logs\`  
**Linux/macOS:** `~/.local/share/IntuneManager/logs/`

### Log Retention
- Rolling file: 1 file per day
- Retain 30 days
- Max 100MB per file

---

## Testing Strategy

### Unit Tests
**Framework:** xUnit
**Coverage Target:** >70% for Core library

**Focus Areas:**
- Service logic (IntuneService, ExportService)
- Authentication provider selection
- JSON serialization/deserialization
- Migration table logic

### Integration Tests
**Scope:** Phase 6+

**Requirements:**
- Test tenant (non-production)
- App registration with test permissions
- Automated cleanup after tests

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
- Store thumbprints only, not private keys
- Use Windows cert store (protected by OS)
- Certificate permissions validation

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
**Decision:** .NET 8 (LTS)

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

### Required NuGet Packages

**Phase 1:**
```xml
<PackageReference Include="Avalonia" Version="11.2.*" />
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.3.*" />
<PackageReference Include="Azure.Identity" Version="1.13.*" />
<PackageReference Include="Microsoft.Graph" Version="5.88.*" />
<PackageReference Include="System.Text.Json" Version="8.0.*" />
```

**Phase 6:**
```xml
<PackageReference Include="Serilog" Version="4.1.*" />
<PackageReference Include="Serilog.Sinks.File" Version="6.0.*" />
<PackageReference Include="System.CommandLine" Version="2.0.*" />
```

### Dependency Management
- Use central package management (Directory.Packages.props)
- Pin major versions, float minor/patch
- Review updates monthly
- Test before updating Graph SDK
