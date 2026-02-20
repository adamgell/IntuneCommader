# Project Planning - Intune Commander .NET Remake

## Development Approach

**Strategy:** Iterative, feature-driven  
**Platform:** Windows-first desktop app (self-contained x64 executable)

---

## Current State (MVP Complete)

All core functionality is implemented and building cleanly. The original 6-phase plan has been fully executed:

| Area | Status |
|------|--------|
| Authentication (Interactive, Device Code, Client Secret) | ✅ Done |
| Multi-cloud support (Commercial, GCC, GCC-High, DoD) | ✅ Done |
| Tenant profile management (encrypted at rest) | ✅ Done |
| 26 Intune object types with list/view/export/import | ✅ Done |
| Bulk export/import with migration table | ✅ Done |
| Group lookup and assignment resolution | ✅ Done |
| Overview dashboard with charts | ✅ Done |
| Debug log window | ✅ Done |
| CI/CD (unit tests, integration tests, build release) | ✅ Done |
| Integration tests against live tenant | ✅ Done |

### Implemented Object Types

Device Configurations, Configuration Policies, Compliance Policies, Applications, Conditional Access, Named Locations, Auth Strengths, Auth Contexts, Assignment Filters, Policy Sets, Endpoint Security, Admin Templates, Enrollment Configurations, App Protection, Managed App Configs, Terms and Conditions, Scope Tags, Role Definitions, Intune Branding, Azure Branding, Terms of Use, Autopilot, Device Health Scripts, Mac Custom Attributes, Feature Updates, Groups

---

## Backlog / Future Enhancements

### Additional Object Types (not yet implemented)

| Priority | Type | Graph Endpoint |
|----------|------|----------------|
| High | PowerShell Scripts | `/deviceManagement/deviceManagementScripts` |
| High | Shell Scripts | `/deviceManagement/deviceShellScripts` |
| High | Compliance Scripts | `/deviceManagement/deviceComplianceScripts` |
| Medium | Notification Templates | `/deviceManagement/notificationMessageTemplates` |
| Medium | Reusable Policy Settings | `/deviceManagement/reusablePolicySettings` |
| Medium | ADMX Uploaded Definitions | `/deviceManagement/groupPolicyUploadedDefinitionFiles` |
| Low | Windows Quality Update Profiles | `/deviceManagement/windowsQualityUpdateProfiles` |
| Low | Windows Driver Update Profiles | `/deviceManagement/windowsDriverUpdateProfiles` |
| Low | Cloud PC Provisioning | `/virtualEndpoint/provisioningPolicies` |
| Low | Cloud PC User Settings | `/virtualEndpoint/userSettings` |

### Advanced Features

- **Object comparison** — diff object against exported JSON, highlight changes, bulk comparison reports
- **Documentation generation** — export policies to human-readable HTML/Markdown (mirrors PowerShell version feature)
- **Update/Replace import modes** — update existing objects instead of creating new ones; conflict resolution strategies
- **CLI / headless mode** — `--export` / `--import` flags for unattended scripted operations
- **Graph API batch requests** — use batch endpoint for bulk operations (significant speed improvement)
- **File-based structured logging** — deferred; DebugLogService currently provides in-app diagnostics
- **OS Credential Store** — move `ClientSecret` out of encrypted profile file into Windows Credential Manager / macOS Keychain

---

## Development Principles

1. **Iterative:** Ship working software at end of each increment
2. **Test early:** Write tests alongside features, not after
3. **Keep it simple:** Avoid over-engineering for future requirements
4. **User feedback:** Test with real Intune environments frequently
5. **Documentation:** Update docs as features are built
6. **Git discipline:** Meaningful commits, no direct main commits

