# Wave 5: Conditional Access and Identity Governance

**Reference:** [docs/SERVICE-IMPLEMENTATION-PLAN.md](../SERVICE-IMPLEMENTATION-PLAN.md) — Wave 5

## Scope

Wave 5 focuses on implementing services for Conditional Access-adjacent identity and governance features:

1. **Named Locations** — Geographic and IP-based location definitions
2. **Authentication Strengths** — Custom authentication strength policies
3. **Authentication Contexts** — Authentication context class references
4. **Terms of Use** — Identity governance terms of use agreements

## Service Implementations

### 1. Named Location Service

**Interface:** `INamedLocationService`  
**Class:** `NamedLocationService`  
**Graph Endpoints:**
- Base collection: `/identity/conditionalAccess/namedLocations`

**Methods to Implement:**
- [ ] `ListNamedLocationsAsync(CancellationToken cancellationToken = default)`
  - Returns `List<NamedLocation>`
  - Use manual `@odata.nextLink` pagination with `$top=999`
  - Note: May include both `IpNamedLocation` and `CountryNamedLocation` subtypes
- [ ] `GetNamedLocationAsync(string id, CancellationToken cancellationToken = default)`
  - Returns `NamedLocation`
  - Throw if null response
- [ ] `CreateNamedLocationAsync(NamedLocation location, CancellationToken cancellationToken = default)`
  - Returns created `NamedLocation`
  - Throw if null response
- [ ] `UpdateNamedLocationAsync(NamedLocation location, CancellationToken cancellationToken = default)`
  - Returns updated `NamedLocation`
  - Throw if null response
- [ ] `DeleteNamedLocationAsync(string id, CancellationToken cancellationToken = default)`
  - Returns `Task`

### 2. Authentication Strength Service

**Interface:** `IAuthenticationStrengthService`  
**Class:** `AuthenticationStrengthService`  
**Graph Endpoints:**
- Base collection: `/identity/conditionalAccess/authenticationStrength/policies`

**Methods to Implement:**
- [ ] `ListAuthenticationStrengthPoliciesAsync(CancellationToken cancellationToken = default)`
  - Returns `List<AuthenticationStrengthPolicy>`
  - Use manual `@odata.nextLink` pagination with `$top=999`
- [ ] `GetAuthenticationStrengthPolicyAsync(string id, CancellationToken cancellationToken = default)`
  - Returns `AuthenticationStrengthPolicy`
  - Throw if null response
- [ ] `CreateAuthenticationStrengthPolicyAsync(AuthenticationStrengthPolicy policy, CancellationToken cancellationToken = default)`
  - Returns created `AuthenticationStrengthPolicy`
  - Throw if null response
- [ ] `UpdateAuthenticationStrengthPolicyAsync(AuthenticationStrengthPolicy policy, CancellationToken cancellationToken = default)`
  - Returns updated `AuthenticationStrengthPolicy`
  - Throw if null response
- [ ] `DeleteAuthenticationStrengthPolicyAsync(string id, CancellationToken cancellationToken = default)`
  - Returns `Task`

### 3. Authentication Context Service

**Interface:** `IAuthenticationContextService`  
**Class:** `AuthenticationContextService`  
**Graph Endpoints:**
- Base collection: `/identity/conditionalAccess/authenticationContextClassReferences`

**Methods to Implement:**
- [ ] `ListAuthenticationContextClassReferencesAsync(CancellationToken cancellationToken = default)`
  - Returns `List<AuthenticationContextClassReference>`
  - Use manual `@odata.nextLink` pagination with `$top=999`
- [ ] `GetAuthenticationContextClassReferenceAsync(string id, CancellationToken cancellationToken = default)`
  - Returns `AuthenticationContextClassReference`
  - Throw if null response
- [ ] `CreateAuthenticationContextClassReferenceAsync(AuthenticationContextClassReference context, CancellationToken cancellationToken = default)`
  - Returns created `AuthenticationContextClassReference`
  - Throw if null response
- [ ] `UpdateAuthenticationContextClassReferenceAsync(AuthenticationContextClassReference context, CancellationToken cancellationToken = default)`
  - Returns updated `AuthenticationContextClassReference`
  - Throw if null response
- [ ] `DeleteAuthenticationContextClassReferenceAsync(string id, CancellationToken cancellationToken = default)`
  - Returns `Task`

### 4. Terms of Use Service

**Interface:** `ITermsOfUseService`  
**Class:** `TermsOfUseService`  
**Graph Endpoints:**
- Base collection: `/identityGovernance/termsOfUse/agreements`

**Methods to Implement:**
- [ ] `ListTermsOfUseAgreementsAsync(CancellationToken cancellationToken = default)`
  - Returns `List<Agreement>`
  - Use manual `@odata.nextLink` pagination with `$top=999`
- [ ] `GetTermsOfUseAgreementAsync(string id, CancellationToken cancellationToken = default)`
  - Returns `Agreement`
  - Throw if null response
- [ ] `CreateTermsOfUseAgreementAsync(Agreement agreement, CancellationToken cancellationToken = default)`
  - Returns created `Agreement`
  - Throw if null response
- [ ] `UpdateTermsOfUseAgreementAsync(Agreement agreement, CancellationToken cancellationToken = default)`
  - Returns updated `Agreement`
  - Throw if null response
- [ ] `DeleteTermsOfUseAgreementAsync(string id, CancellationToken cancellationToken = default)`
  - Returns `Task`
- [ ] `GetAgreementFileAsync(string agreementId, string fileId, CancellationToken cancellationToken = default)`
  - Returns agreement file content if supported
- [ ] `UploadAgreementFileAsync(string agreementId, byte[] fileContent, string fileName, CancellationToken cancellationToken = default)`
  - Uploads agreement file if supported

## Scaffolding Steps

### Core Service Setup
- [ ] Add interface files in `src/Intune.Commander.Core/Services/`
- [ ] Add implementation classes in `src/Intune.Commander.Core/Services/`
- [ ] Ensure constructors accept `GraphServiceClient`
- [ ] Add all service methods with proper signatures
- [ ] Implement manual `@odata.nextLink` pagination in all list methods
- [ ] Ensure all models use `Microsoft.Graph.Beta.Models` types
- [ ] Add null checks and throw on null responses for create/update/get

### Export/Import Integration
- [ ] Extend `ExportService` to handle Wave 5 objects
- [ ] Extend `ImportService` to handle Wave 5 objects
- [ ] Create export wrapper models if needed
- [ ] Add subfolder paths for each type in export structure
- [ ] Maintain migration-table compatibility
- [ ] Consider special handling for agreement files (binary content)

### Desktop UI Integration
- [ ] Add collections in `MainWindowViewModel` for each type
- [ ] Add selection properties for each type
- [ ] Add DataGrid column configurations
- [ ] Add navigation category entries (possibly under "Identity" or "Conditional Access")
- [ ] Implement lazy-load handlers
- [ ] Add cache invalidation keys
- [ ] Wire up export/import commands for new types

### Testing
- [ ] Add `NamedLocationServiceTests.cs` in `tests/Intune.Commander.Core.Tests/Services/`
  - Test pagination continuation
  - Test list/get/create/update/delete success paths
  - Test null response handling
  - Test handling of IP and Country location subtypes
- [ ] Add `AuthenticationStrengthServiceTests.cs` in `tests/Intune.Commander.Core.Tests/Services/`
  - Test pagination continuation
  - Test list/get/create/update/delete success paths
  - Test null response handling
- [ ] Add `AuthenticationContextServiceTests.cs` in `tests/Intune.Commander.Core.Tests/Services/`
  - Test pagination continuation
  - Test list/get/create/update/delete success paths
  - Test null response handling
- [ ] Add `TermsOfUseServiceTests.cs` in `tests/Intune.Commander.Core.Tests/Services/`
  - Test pagination continuation
  - Test list/get/create/update/delete success paths
  - Test null response handling
  - Test file operations if implemented

## Definition of Done

- [ ] All services compile without errors
- [ ] Unit tests added and passing for all services
- [ ] Manual pagination used on every list method
- [ ] CancellationToken passed to all Graph API calls
- [ ] No UI-thread sync blocking introduced
- [ ] Services consumed by desktop ViewModel
- [ ] Export/Import functionality working for all types
- [ ] UI displays and allows interaction with all new types

## Notes

- **Named Locations** have two subtypes: `IpNamedLocation` (IP ranges/CIDR) and `CountryNamedLocation` (country codes)
- **Authentication Strengths** define combinations of authentication methods required for access
- **Authentication Contexts** provide granular step-up authentication triggers for apps
- **Terms of Use** agreements contain PDF files that need special handling
- These resources are part of Azure AD/Entra ID, not Intune — ensure proper permissions are documented
- Consider whether these services should be in a separate namespace (e.g., `Intune.Commander.Core.Services.Identity`)
- Built-in Authentication Strengths are read-only — handle appropriately in UI
- Named Locations may be referenced by Conditional Access policies — consider dependency tracking
