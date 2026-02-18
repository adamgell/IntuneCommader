# Project Planning - Intune Commander .NET Remake

## Development Approach

**Strategy:** Iterative MVP  
**Platform:** Windows-first, containerization later  
**Timeline:** 12 week estimate for MVP completion

---

## Phase 1: Foundation (Weeks 1-2)

### Goal
Single tenant, single object type, working end-to-end

### Deliverables
1. **Project structure**
   - Core library (.NET Standard 2.0 or .NET 10)
   - Avalonia Desktop app (.NET 10)
   - Basic test project setup

2. **Authentication**
   - Interactive browser login (Azure.Identity)
   - Commercial cloud only
   - Token caching to local storage

3. **Single object CRUD**
   - Target: Device Configurations
   - List all configurations
   - View individual configuration details
   - Export to JSON
   - Import from JSON

4. **Basic UI**
   - Login screen with tenant ID input
   - Object list view (DataGrid)
   - Detail pane for selected object
   - Export/Import buttons

5. **Export/Import**
   - Read PowerShell JSON exports
   - Write compatible JSON format
   - Maintain migration table structure

### Success Criteria
- ✅ Can authenticate to a Commercial tenant
- ✅ Can list Device Configurations via Graph API
- ✅ Can export one Device Configuration to JSON
- ✅ Can import that JSON back to same or different tenant
- ✅ JSON format is compatible with PowerShell version

---

## Phase 2: Multi-Cloud + Profile System (Weeks 3-4)

### Goal
Support all government clouds with saved tenant profiles

### Deliverables
1. **Cloud selection**
   - Support Commercial, GCC, GCC-High, DoD
   - Cloud-specific Graph endpoints
   - Cloud-specific authority hosts

2. **Profile management**
   - Create/edit/delete tenant profiles
   - Profile model: Name, TenantId, Cloud, ClientId, AuthMethod
   - Profile validation

3. **Profile storage**
   - Encrypted local JSON file
   - Cross-platform storage location
   - Secure credential storage (certificate thumbprints only, not secrets)

4. **Profile switcher UI**
   - Profile dropdown in main window
   - Switch active profile without restart
   - Show current profile status

### Success Criteria
- ✅ Can save profile: "MyTenant-GCCHigh"
- ✅ Can switch between 3+ saved profiles
- ✅ Settings persist between app sessions
- ✅ Can authenticate to GCC-High tenant

---

## Phase 3: Expand Object Types (Weeks 5-6)

### Goal
CRUD operations for most common Intune objects

### Priority Order
1. **Device Configurations** *(already complete)*
2. **Compliance Policies**
   - List, view, export, import
3. **Configuration Policies** (Settings Catalog)
   - Handle settings catalog format
4. **Applications** (Win32 apps)
   - **Exclude** .intunewin file handling initially
   - Basic app metadata only
5. **Conditional Access Policies**
   - Read-only initially (sensitive)

### Implementation Pattern
- Reusable service pattern from Phase 1
- Generic object list view component
- Standardized export/import logic

### Success Criteria
- ✅ Each object type has list/view/export/import
- ✅ Backward compatible with PowerShell JSON exports
- ✅ UI accommodates different object schemas

---

## Phase 4: Bulk Operations (Weeks 7-8)

### Goal
Export/import multiple objects efficiently

### Deliverables
1. **Bulk export**
   - Multi-select objects in list
   - Export to folder structure (mimics PowerShell layout)
   - Progress indicator for large exports

2. **Bulk import**
   - Point to folder containing JSON files
   - Validate files before import
   - Import queue with status

3. **Migration table**
   - Track original ID → new ID mappings
   - Store in migration-table.json (PowerShell compatible)
   - Use for assignment imports

4. **Dependency handling**
   - Detect dependencies (e.g., Policy Sets → Policies)
   - Import in correct order
   - Update references during import

5. **Group handling**
   - Create missing groups during import
   - Preserve group metadata (name, description, type)
   - Dynamic group support

### Success Criteria
- ✅ Can export entire tenant's Device Configurations (50+ objects)
- ✅ Can import folder of configurations into different tenant
- ✅ Group assignments are created if missing
- ✅ Migration table format matches PowerShell version

---

## Phase 5: Auth Expansion (Weeks 9-10)

### Goal
Certificate and Managed Identity authentication support

### Deliverables
1. **Certificate authentication**
   - Read certificate from Windows cert store
   - Store certificate thumbprint in profile
   - Client certificate flow

2. **Managed Identity**
   - Detect managed identity availability
   - Auto-configure when running on Azure VM/Container App
   - Fallback to interactive if not available

3. **Profile auth types**
   - Profile stores auth method preference
   - UI selection: Interactive, Certificate, Managed Identity
   - Per-profile auth method

4. **Credential abstraction**
   - Interface-based auth provider
   - Easy to add new auth types
   - Clean separation from business logic

### Success Criteria
- ✅ Can create profile using certificate thumbprint
- ✅ Can authenticate with certificate (no browser popup)
- ✅ Can authenticate with MI when running on Azure VM
- ✅ Desktop app gracefully handles all auth flows

---

## Phase 6: Polish & Docker (Weeks 11-12)

### Goal
Production-ready features and containerization

### Deliverables
1. **Error handling**
   - User-friendly error messages
   - Graph API error translation
   - Retry logic for transient failures

2. **Logging**
   - Structured logging (Serilog)
   - Log to file and console
   - Log levels configurable

3. **CLI mode**
   - Headless export: `--export --profile X --output ./backup`
   - Headless import: `--import --profile X --input ./backup`
   - Non-interactive mode

4. **Dockerfile**
   - Multi-stage build
   - Minimal runtime image
   - Supports device code auth flow

5. **Progress indicators**
   - Bulk operations show progress bar
   - Cancellable long-running operations
   - Estimated time remaining

6. **Graph API optimizations**
   - Respect throttling headers
   - Exponential backoff retry
   - Batch API usage where possible

### Success Criteria
- ✅ Can run headless: `IntuneManager.Cli export --profile MyProfile --output ./backup`
- ✅ Docker image runs on Linux
- ✅ Desktop app handles Graph API rate limiting gracefully
- ✅ Error messages are actionable

---

## Future Enhancements (Post-MVP)

These features are **out of scope** for initial MVP but may be added iteratively:

### Additional Object Types
- Endpoint Security policies (all types)
- PowerShell scripts
- Shell scripts
- Feature Updates
- Quality Updates
- Autopilot profiles
- Enrollment restrictions
- Apple enrollment types
- Android OEM Config
- Terms and Conditions

### Advanced Features
- **Object comparison**
  - Compare objects with exported JSON
  - Highlight differences
  - Bulk comparison reports

- **Documentation generation**
  - Export policies to human-readable docs
  - HTML/Markdown output
  - Mirror PowerShell version documentation feature

- **Update/Replace modes**
  - Update existing objects instead of import
  - Replace existing objects (risky)
  - Conflict resolution strategies

- **Advanced filtering and search**
  - Filter objects by type, name, date
  - Full-text search in object properties
  - Saved search queries

- **Batch API usage**
  - Use Graph batch endpoint for performance
  - Process 20 requests per batch
  - Significant speed improvement for bulk operations

---

## Risk Management

### Identified Risks

**Risk:** Graph API breaking changes in beta endpoint  
**Impact:** High - Could break existing functionality  
**Mitigation:** Use v1.0 endpoint where possible, isolate beta usage to specific services  
**Contingency:** Pin to specific API versions, test before updates

**Risk:** Avalonia learning curve slows development  
**Impact:** Medium - Phase 1-2 delays  
**Mitigation:** Start with simple layouts, leverage WPF XAML similarity  
**Contingency:** Consider Windows-only WPF if Avalonia proves too complex

**Risk:** JSON format incompatibility with PowerShell version  
**Impact:** High - Breaks backward compatibility goal  
**Mitigation:** Early validation testing with actual PowerShell exports  
**Contingency:** Conversion utility to translate formats

**Risk:** Scope creep during development  
**Impact:** Medium - Timeline slip  
**Mitigation:** Strict phase boundaries, resist mid-phase feature additions  
**Contingency:** Push features to "Future Enhancements"

**Risk:** Multi-cloud authentication complexity  
**Impact:** Medium - Phase 2 delays  
**Mitigation:** Research Microsoft docs thoroughly, test early  
**Contingency:** Ship Phase 1 separately if Phase 2 blocked

**Risk:** Graph API rate limiting in production  
**Impact:** Low-Medium - User experience degradation  
**Mitigation:** Implement retry logic early, respect throttling headers  
**Contingency:** Add exponential backoff, queue-based processing

---

## Success Metrics

### Phase 1 Success
- Time to first working import/export: < 10 minutes from app launch
- Can successfully import 10 PowerShell-exported JSON files

### Phase 4 Success
- Bulk export of 100 objects completes in < 5 minutes
- Bulk import success rate > 95%

### Phase 6 Success
- Docker container size < 250MB
- CLI export completes without user interaction
- Zero unhandled exceptions in normal operation

---

## Development Principles

1. **Iterative:** Ship working software at end of each phase
2. **Test early:** Write tests alongside features, not after
3. **Keep it simple:** Avoid over-engineering for future requirements
4. **User feedback:** Test with real Intune environments frequently
5. **Documentation:** Update docs as features are built
6. **Git discipline:** Meaningful commits, feature branches, no direct main commits
