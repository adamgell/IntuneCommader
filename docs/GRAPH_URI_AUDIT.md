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
- **Implemented in Intune Commander: 26 Graph services** (as of last update)

## Current Coverage

All services use `Microsoft.Graph.Beta` and manual `@odata.nextLink` pagination.

| Service interface | Graph API path (Beta) | Operations | Desktop nav |
|---|---|---|---|
| `IConfigurationProfileService` | `/deviceManagement/deviceConfigurations` | List, Get, Create, Update, Delete, GetAssignments | Device Configurations |
| `ICompliancePolicyService` | `/deviceManagement/deviceCompliancePolicies` | List, Get, Create, Update, Delete, GetAssignments, Assign | Compliance Policies |
| `IApplicationService` | `/deviceAppManagement/mobileApps` | List, Get, GetAssignments | Applications, App Assignments |
| `ISettingsCatalogService` | `/deviceManagement/configurationPolicies` | List, Get, GetAssignments | Settings Catalog |
| `IEndpointSecurityService` | `/deviceManagement/intents` | List, Get, Create, Update, Delete, GetAssignments, Assign | Endpoint Security |
| `IAdministrativeTemplateService` | `/deviceManagement/groupPolicyConfigurations` | List, Get, Create, Update, Delete, GetAssignments, Assign | Administrative Templates |
| `IEnrollmentConfigurationService` | `/deviceManagement/deviceEnrollmentConfigurations` | List, Get, Create, Update, Delete | Enrollment Configurations |
| `IAppProtectionPolicyService` | `/deviceAppManagement/managedAppPolicies` | List, Get, Create, Update, Delete | App Protection Policies |
| `IManagedAppConfigurationService` | `/deviceAppManagement/mobileAppConfigurations` + `/targetedManagedAppConfigurations` | List, Get, Create, Update, Delete (both) | Managed + Targeted App Configs |
| `ITermsAndConditionsService` | `/deviceManagement/termsAndConditions` | List, Get, Create, Update, Delete | Terms and Conditions |
| `IScopeTagService` | `/deviceManagement/roleScopeTags` | List, Get, Create, Update, Delete | Scope Tags |
| `IRoleDefinitionService` | `/deviceManagement/roleDefinitions` | List, Get, Create, Update, Delete | Role Definitions |
| `IIntuneBrandingService` | `/deviceManagement/intuneBrandingProfiles` | List, Get, Create, Update, Delete | Intune Branding |
| `IAzureBrandingService` | `/organization/{id}/branding/localizations` | List, Get, Create, Update, Delete | Azure Branding |
| `IAutopilotService` | `/deviceManagement/windowsAutopilotDeploymentProfiles` | List, Get, Create, Update, Delete | Autopilot Profiles |
| `IDeviceHealthScriptService` | `/deviceManagement/deviceHealthScripts` | List, Get, Create, Update, Delete | Device Health Scripts |
| `IMacCustomAttributeService` | `/deviceManagement/deviceCustomAttributeShellScripts` | List, Get, Create, Update, Delete | Mac Custom Attributes |
| `IFeatureUpdateProfileService` | `/deviceManagement/windowsFeatureUpdateProfiles` | List, Get, Create, Update, Delete | Feature Updates |
| `IAssignmentFilterService` | `/deviceManagement/assignmentFilters` | List, Get | Assignment Filters |
| `IPolicySetService` | `/deviceAppManagement/policySets` | List, Get | Policy Sets |
| `IConditionalAccessPolicyService` | `/identity/conditionalAccess/policies` | List, Get | Conditional Access |
| `INamedLocationService` | `/identity/conditionalAccess/namedLocations` | List, Get, Create, Update, Delete | Named Locations |
| `IAuthenticationStrengthService` | `/identity/conditionalAccess/authenticationStrength/policies` | List, Get, Create, Update, Delete | Authentication Strengths |
| `IAuthenticationContextService` | `/identity/conditionalAccess/authenticationContextClassReferences` | List, Get, Create, Update, Delete | Authentication Contexts |
| `ITermsOfUseService` | `/identityGovernance/termsOfUse/agreements` | List, Get, Create, Update, Delete | Terms of Use |
| `IGroupService` | `/groups` + `/groups/{id}/members` | ListDynamic, ListAssigned, Search, GetMembers, GetGroupAssignments | Dynamic Groups, Assigned Groups, Group Lookup |

## Uncovered Endpoint Families (backlog)

### Scripts

| Endpoint | Suggested service | Priority |
|---|---|---|
| `/deviceManagement/deviceManagementScripts` | `IDeviceManagementScriptService` | High — migration-critical |
| `/deviceManagement/deviceShellScripts` | `IDeviceShellScriptService` | High — migration-critical |
| `/deviceManagement/deviceComplianceScripts` | `IComplianceScriptService` | Medium |

### Policy Support Objects

| Endpoint | Suggested service | Priority |
|---|---|---|
| `/deviceManagement/groupPolicyUploadedDefinitionFiles` | `IAdmxFileService` | Medium — needed for Admin Template imports |
| `/deviceManagement/reusablePolicySettings` | `IReusablePolicySettingService` | Medium |
| `/deviceManagement/notificationMessageTemplates` | `INotificationTemplateService` | Low |

### Update Plane

| Endpoint | Suggested service | Priority |
|---|---|---|
| `/deviceManagement/windowsQualityUpdateProfiles` | `IQualityUpdateProfileService` | Medium |
| `/deviceManagement/windowsDriverUpdateProfiles` | `IDriverUpdateProfileService` | Low |

### Cloud PC

| Endpoint | Suggested service | Priority |
|---|---|---|
| `/deviceManagement/virtualEndpoint/provisioningPolicies` | `ICloudPcProvisioningService` | Low |
| `/deviceManagement/virtualEndpoint/userSettings` | `ICloudPcUserSettingsService` | Low |

### Enrollment / Apple Long-Tail

| Endpoint | Suggested service | Priority |
|---|---|---|
| `/deviceManagement/depOnboardingSettings` | `IAppleDepService` | Low |
| `/deviceManagement/importedAppleDeviceIdentities` | *(part of AppleDep)* | Low |
| `/deviceManagement/deviceCategories` | `IDeviceCategoryService` | Low |

### App Management Long-Tail

| Endpoint | Suggested service | Priority |
|---|---|---|
| `/deviceAppManagement/vppTokens` | `IVppTokenService` | Low |

### RBAC Extensions

| Endpoint | Suggested service | Priority |
|---|---|---|
| `/deviceManagement/roleAssignments` | *(extend `IRoleDefinitionService`)* | Low |

## Suggested Build Sequence (next waves)

Per [SERVICE-IMPLEMENTATION-PLAN.md](SERVICE-IMPLEMENTATION-PLAN.md):

1. **Wave 7 — Scripts** (highest migration value): `IDeviceManagementScriptService`, `IDeviceShellScriptService`, `IComplianceScriptService`, `IAdmxFileService`, `IReusablePolicySettingService`
2. **Wave 8 — Update Plane**: Quality Updates, Driver Update Profiles
3. **Wave 9 — Enrollment + Apple**: DEP onboarding, device categories
4. **Wave 10 — Cloud PC + Long-Tail**: W365 provisioning/user settings, VPP tokens

## Service Implementation Standard

For each new service:

- Define `I{Type}Service` in `src/Intune.Commander.Core/Services/`.
- Implement `{Type}Service` with `GraphServiceClient` constructor injection.
- Use manual `@odata.nextLink` pagination (`$top=999` on initial request, then `.WithUrl(nextLink)`).
- Accept `CancellationToken` in all async methods.
- Return Graph SDK models directly (`Microsoft.Graph.Beta.Models`).
- Add export/import support for all migration-relevant types.
- Add focused Core tests: pagination, CRUD/null handling, serialization.

## Notes

- Inventory includes templated URIs with PowerShell variables (e.g. `/$($objectType.API)/...`); these indicate dynamic object handling in the legacy tool.
- All endpoints use the **Beta** Graph API (`https://graph.microsoft.com/beta/`).
- The current architecture is service-per-type; new services must follow the same pattern.
- `$top=999` is the correct pagination default (not `$top=200` as the CSV audit originally noted).
