# Service Implementation Wave Tracking Issues

This directory contains detailed tracking documents for implementing Intune Commander services in five waves, as outlined in [docs/SERVICE-IMPLEMENTATION-PLAN.md](../SERVICE-IMPLEMENTATION-PLAN.md).

## Wave Documents

### [Wave 1: Endpoint Security, Administrative Templates, and Enrollment Configurations](WAVE-1-ENDPOINT-SECURITY-TEMPLATES-ENROLLMENT.md)
**Priority:** High — Core policy management services  
**Services:**
1. Endpoint Security Service (`/deviceManagement/intents`)
2. Administrative Template Service (`/deviceManagement/groupPolicyConfigurations`)
3. Enrollment Configuration Service (`/deviceManagement/deviceEnrollmentConfigurations`)

**Status:** 🔲 Not Started

---

### [Wave 2: App Protection and Managed App Configurations](WAVE-2-APP-PROTECTION-CONFIGURATIONS.md)
**Priority:** High — Application management plane  
**Services:**
1. App Protection Service (`/deviceAppManagement/managedAppPolicies`)
2. Managed App Configuration Service (mobile + targeted configurations)
3. Terms and Conditions Service (`/deviceManagement/termsAndConditions`)

**Status:** 🔲 Not Started

---

### [Wave 3: Tenant Administration Services](WAVE-3-TENANT-ADMINISTRATION.md)
**Priority:** Medium — Tenant-level administrative features  
**Services:**
1. Scope Tag Service (`/deviceManagement/roleScopeTags`)
2. Role Definition Service (`/deviceManagement/roleDefinitions`)
3. Intune Branding Service (`/deviceManagement/intuneBrandingProfiles`)
4. Azure Branding Service (`/organization/{organizationId}/branding/localizations`)

**Status:** 🔲 Not Started

---

### [Wave 4: Enrollment and Device Management Services](WAVE-4-ENROLLMENT-DEVICE-MANAGEMENT.md)
**Priority:** Medium — Device lifecycle management  
**Services:**
1. Autopilot Service (`/deviceManagement/windowsAutopilotDeploymentProfiles`)
2. Device Health Script Service (`/deviceManagement/deviceHealthScripts`)
3. Mac Custom Attribute Service (`/deviceManagement/deviceCustomAttributeShellScripts`)
4. Feature Update Service (`/deviceManagement/windowsFeatureUpdateProfiles`)

**Status:** 🔲 Not Started

---

### [Wave 5: Conditional Access and Identity Governance](WAVE-5-CONDITIONAL-ACCESS-IDENTITY.md)
**Priority:** Low — Identity-adjacent features  
**Services:**
1. Named Location Service (`/identity/conditionalAccess/namedLocations`)
2. Authentication Strength Service (`/identity/conditionalAccess/authenticationStrengths/policies`)
3. Authentication Context Service (`/identity/conditionalAccess/authenticationContextClassReferences`)
4. Terms of Use Service (`/identityGovernance/termsOfUse/agreements`)

**Status:** 🔲 Not Started

---

## Quick Reference

### Common Patterns Across All Waves

Each service follows these conventions:

1. **Interface + Implementation** in `src/Intune.Commander.Core/Services/`
2. **Constructor** accepts `GraphServiceClient`
3. **Async methods** with `CancellationToken cancellationToken = default`
4. **List methods** use manual `@odata.nextLink` pagination with `$top=999`
5. **CRUD methods** throw on null create/update responses
6. **Assignment pattern** where supported:
   - `GetAssignmentsAsync(id)`
   - `Assign...Async(id, List<TAssignment>)`

### Implementation Phases (per service)

- **Phase A — Scaffold:** Add interfaces, service classes, method signatures
- **Phase B — Functional Completion:** Implement special helpers, normalization
- **Phase C — Desktop Integration:** Add to `MainWindowViewModel`, UI wiring
- **Phase D — Export/Import:** Extend `ExportService`/`ImportService`
- **Phase E — Tests:** Add unit tests in `Intune.Commander.Core.Tests`

### Definition of Done (per service)

- ✅ Compiles without errors
- ✅ Unit tests added and passing
- ✅ Manual pagination on all list methods
- ✅ CancellationToken passed to all Graph calls
- ✅ No UI-thread sync blocking
- ✅ Consumed by desktop ViewModel
- ✅ Export/Import functional

## Progress Tracking

Update the status emoji in each wave section as work progresses:

- 🔲 Not Started
- 🚧 In Progress
- ✅ Complete
- ⏸️ Blocked/On Hold

## Notes

- Each wave document contains detailed method signatures and checklists
- Reference the main [SERVICE-IMPLEMENTATION-PLAN.md](../SERVICE-IMPLEMENTATION-PLAN.md) for architectural context
- Follow the conventions documented in [.github/copilot-instructions.md](../../.github/copilot-instructions.md)
- See [ARCHITECTURE.md](../ARCHITECTURE.md) for overall design decisions
