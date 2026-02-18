# Graph URI Audit and Service Build-Out Plan

This document inventories Graph API URI usage from the `IntuneManagement` submodule and maps it to the current Intune Commander service/view architecture.

## Artifacts

- Raw endpoint inventory (generated): `docs/graph-uri-inventory.csv`
- Source scanned: `IntuneManagement/**/*.ps1`, `IntuneManagement/**/*.psm1`
- Extraction captured:
  - `Invoke-GraphRequest -Url "..."` calls
  - `API = "..."` object type definitions

## Summary

- Extracted URI rows: **132**
- Unique URI patterns: **104**
- Stable/non-templated URI patterns: **63**

## Current Coverage (already in Intune Commander)

| Area | Graph family | Current service(s) | Current view/nav |
|---|---|---|---|
| Device Configurations | `/deviceManagement/deviceConfigurations` | `IConfigurationProfileService` / `ConfigurationProfileService` | Device Configurations |
| Compliance Policies | `/deviceManagement/deviceCompliancePolicies` | `ICompliancePolicyService` / `CompliancePolicyService` | Compliance Policies |
| Applications | `/deviceAppManagement/mobileApps` | `IApplicationService` / `ApplicationService` | Applications + App Assignments |
| Settings Catalog | `/deviceManagement/configurationPolicies` | `ISettingsCatalogService` / `SettingsCatalogService` | Settings Catalog |
| Groups | `/groups`, member expansion | `IGroupService` / `GroupService` | Dynamic Groups + Assigned Groups + Group Lookup |

## Uncovered Endpoint Families (high-value backlog)

### Identity / Access

| Endpoint family | Suggested service |
|---|---|
| `/identity/conditionalAccess/policies` | `IConditionalAccessPolicyService` |
| `/identity/conditionalAccess/namedLocations` | `INamedLocationService` |
| `/identity/conditionalAccess/authenticationContextClassReferences` | `IAuthenticationContextService` |
| `/identity/conditionalAccess/authenticationStrengths/policies` | `IAuthenticationStrengthService` |
| `/identityGovernance/termsOfUse/agreements` | `ITermsOfUseService` |

### Device Management Governance

| Endpoint family | Suggested service |
|---|---|
| `/deviceManagement/assignmentFilters` | `IAssignmentFilterService` |
| `/deviceManagement/roleDefinitions`, `/roleAssignments`, `/roleScopeTags` | `IRbacRoleService` |
| `/deviceManagement/templates`, `/configurationPolicyTemplates` | `IPolicyTemplateService` |

### Policy / Profile Extensions

| Endpoint family | Suggested service |
|---|---|
| `/deviceManagement/intents` | `ISettingsCatalogIntentService` (or `IIntentService`) |
| `/deviceManagement/groupPolicyConfigurations` (+ categories/definitions) | `IAdministrativeTemplateService` |
| `/deviceManagement/reusablePolicySettings` | `IReusablePolicySettingService` |
| `/deviceManagement/deviceComplianceScripts` | `IComplianceScriptService` |
| `/deviceManagement/deviceHealthScripts` | `IDeviceHealthScriptService` |
| `/deviceManagement/deviceManagementScripts` | `IDeviceManagementScriptService` |
| `/deviceManagement/deviceShellScripts` | `IDeviceShellScriptService` |
| `/deviceManagement/deviceCustomAttributeShellScripts` | `ICustomAttributeScriptService` |

### App Management Extensions

| Endpoint family | Suggested service |
|---|---|
| `/deviceAppManagement/policySets` | `IPolicySetService` |
| `/deviceAppManagement/managedAppPolicies` | `IManagedAppPolicyService` |
| `/deviceAppManagement/mobileAppConfigurations` | `IMobileAppConfigurationService` |
| `/deviceAppManagement/targetedManagedAppConfigurations` | `ITargetedManagedAppConfigurationService` |
| `/deviceAppManagement/vppTokens` | `IVppTokenService` |

### Enrollment / Autopilot / Updates

| Endpoint family | Suggested service |
|---|---|
| `/deviceManagement/deviceEnrollmentConfigurations` | `IDeviceEnrollmentConfigurationService` |
| `/deviceManagement/windowsAutopilotDeploymentProfiles` | `IAutopilotProfileService` |
| `/deviceManagement/windowsFeatureUpdateProfiles` | `IWindowsFeatureUpdateService` |
| `/deviceManagement/windowsQualityUpdateProfiles` | `IWindowsQualityUpdateService` |
| `/deviceManagement/windowsDriverUpdateProfiles` | `IWindowsDriverUpdateService` |
| `/deviceManagement/virtualEndpoint/provisioningPolicies`, `/virtualEndpoint/userSettings` | `ICloudPcService` |

## Suggested Build Sequence (pragmatic)

1. **Conditional Access read-only** (`IConditionalAccessPolicyService`) + new nav category.
2. **Assignment Filters** (`IAssignmentFilterService`) because many policy assignments depend on filters.
3. **Policy Sets** (`IPolicySetService`) to support bundled policy migration.
4. **RBAC roles** (`IRbacRoleService`) for governance parity.
5. **Scripts family** (compliance/device mgmt/shell) as one implementation wave.
6. **Update/Autopilot/Enrollment** profile services.

## Service Implementation Standard (same pattern as existing)

For each new service:

- Define `I{Type}Service` in `src/IntuneManager.Core/Services/`.
- Implement `{Type}Service` with `GraphServiceClient` constructor injection.
- Use manual `@odata.nextLink` pagination (`$top = 999` on initial request).
- Accept `CancellationToken` in all async methods.
- Return Graph SDK models directly (`Microsoft.Graph.Beta.Models`).
- Add export/import support only when required by migration scenarios.

## Notes

- Inventory includes templated URIs with PowerShell variables (for example `/$($objectType.API)/...`); these are expected and indicate dynamic object handling in the legacy tool.
- The current Intune Commander architecture is already aligned with service-per-type; build-out should continue that pattern instead of a single generic service.
