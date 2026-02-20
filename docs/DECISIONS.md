# Decision Log

This document records key architectural and technical decisions made during the IntuneManager project planning phase.

---

## Decision 001: Technology Stack

**Date:** 2025-02-14  
**Status:** Approved  
**Context:** Need to migrate from PowerShell/WPF to overcome threading, UI refresh, and caching limitations

**Options Considered:**
1. .NET + Avalonia UI
2. .NET + WPF (Windows-only)
3. Go + Fyne
4. Electron + TypeScript (web technologies)

**Decision:** .NET 10 + Avalonia UI

**Rationale:**
- Natural migration path from PowerShell (both .NET ecosystem)
- Avalonia XAML nearly identical to WPF (minimal XAML porting)
- Cross-platform native support (future Linux/Docker deployment)
- Microsoft Graph SDK is first-class in C#
- Compiled code solves PowerShell threading issues
- Strong typing eliminates runtime errors

**Consequences:**
- Requires learning C# for PowerShell developer
- Avalonia has smaller community than WPF
- Cross-platform adds complexity vs Windows-only WPF

**Alternatives Rejected:**
- WPF: Windows-only, doesn't support Docker requirement
- Go+Fyne: No XAML reuse, weaker Graph ecosystem
- Electron: Heavy runtime, not truly native

---

## Decision 002: Authentication Framework

**Date:** 2025-02-14  
**Status:** Approved  
**Context:** Need multi-cloud, multi-tenant authentication without direct MSAL dependency

**Decision:** Azure.Identity library

**Rationale:**
- Microsoft-recommended modern approach
- Built-in multi-cloud support (Commercial, GCC, GCC-High, DoD)
- Multiple credential types with automatic fallback
- No direct MSAL dependency (uses abstractions)
- Better token caching than manual MSAL implementation
- Supports Managed Identity for future Azure deployment

**Consequences:**
- Learning curve for Azure.Identity vs familiar MSAL
- Must configure cloud-specific endpoints
- Requires separate app registrations per cloud

**Alternatives Rejected:**
- MSAL directly: More manual configuration, less abstraction
- Custom auth: Reinventing the wheel, security risks

---

## Decision 003: Multi-Cloud Strategy

**Date:** 2025-02-14  
**Status:** Approved  
**Context:** Must support Commercial, GCC, GCC-High, and DoD clouds

**Decision:** Separate app registration per cloud with profile-based configuration

**Rationale:**
- GCC-High and DoD require separate Azure portals for app registration
- Isolates permissions between environments
- Simpler to manage cloud-specific configurations
- Avoids cross-cloud authentication errors
- Profiles allow easy tenant switching

**Consequences:**
- User must register app in each cloud separately
- More initial setup complexity
- Documentation must cover all four cloud registration processes

**Alternatives Rejected:**
- Single app across clouds: Not technically feasible for gov clouds
- Auto-detect cloud: Unreliable, requires undocumented APIs

---

## Decision 004: Backward Compatibility Scope

**Date:** 2025-02-14  
**Status:** Approved  
**Context:** Existing PowerShell version has established JSON export format

**Decision:** Read-only backward compatibility (import PowerShell exports only)

**Rationale:**
- Users may have existing PowerShell exports to migrate
- Proven format structure reduces design work
- One-way migration acceptable for new tool
- PowerShell version doesn't need .NET export support

**Consequences:**
- Must validate against PowerShell JSON schema
- Testing requires actual PowerShell exports
- .NET version can introduce new format features

**Alternatives Rejected:**
- Full bidirectional compatibility: Unnecessary complexity
- No compatibility: Breaks migration path for users
- New format only: Ignores existing user data

---

## Decision 005: Development Approach

**Date:** 2025-02-14  
**Status:** Approved  
**Context:** Hobby project with unlimited timeline, desire for iterative progress

**Decision:** Iterative MVP approach with 6 phases

**Rationale:**
- Delivers working software early (Phase 1: 2 weeks)
- Allows course correction between phases
- Reduces risk of scope creep
- Matches developer's preference for iteration
- Hobby project timeline allows methodical approach

**Consequences:**
- Some features delayed to later phases
- May need refactoring as architecture evolves
- Requires discipline to avoid mid-phase feature additions

**Alternatives Rejected:**
- Waterfall (full planning): Too rigid for hobby project
- Complete chaos: High risk of never finishing
- Feature-driven: Loses cohesion across object types

---

## Decision 006: UI Framework and Pattern

**Date:** 2025-02-14  
**Status:** Approved  
**Context:** Need maintainable, testable UI architecture

**Decision:** Avalonia + MVVM (CommunityToolkit.Mvvm)

**Rationale:**
- MVVM is industry standard for XAML-based UIs
- CommunityToolkit provides source generators (less boilerplate)
- Clean separation enables unit testing ViewModels
- Familiar pattern for WPF developers
- Avalonia's data binding works well with MVVM

**Consequences:**
- Learning curve for MVVM if new to pattern
- More classes/files than code-behind approach
- Requires understanding of INotifyPropertyChanged

**Alternatives Rejected:**
- Code-behind: Not testable, tight coupling
- MVC: Poor fit for desktop applications
- Custom pattern: Reinventing established patterns

---

## Decision 007: Object Model Strategy

**Date:** 2025-02-14  
**Status:** Approved  
**Context:** Need to represent Intune objects from Graph API

**Decision:** Use Microsoft.Graph SDK models directly where possible

**Rationale:**
- Strongly typed, compile-time safety
- Automatic updates when SDK updated
- Built-in serialization support
- No manual property mapping
- Official Microsoft models

**Consequences:**
- Tied to SDK release schedule
- Some models include non-serializable properties
- May need custom DTOs for export/import edge cases

**Alternatives Rejected:**
- Custom models: High maintenance, error-prone mapping
- Dynamic objects: Lose type safety
- DTOs everywhere: Unnecessary duplication

---

## Decision 008: Initial Platform Target

**Date:** 2025-02-14  
**Status:** Approved  
**Context:** Avalonia supports Windows, Linux, macOS; limited development resources

**Decision:** Windows-only for Phases 1-5, Linux Docker in Phase 6

**Rationale:**
- Developer's primary platform is Windows
- Intune management typically Windows-centric
- Reduces testing burden initially
- Docker/Linux adds value later for automation
- Avalonia makes cross-platform easy when ready

**Consequences:**
- macOS/Linux desktop users wait until post-MVP
- Can't test Linux deployment early
- May discover platform-specific issues late

**Alternatives Rejected:**
- All platforms from start: Too much testing overhead
- Linux-first: Not developer's platform
- Skip cross-platform: Loses Avalonia value proposition

---

## Decision 009: Logging Framework

**Date:** 2025-02-14  
**Status:** Superseded — see implementation note  
**Context:** Need structured logging for debugging and audit trail

**Original Decision:** Serilog (deferred to Phase 6)

**Implementation Note (2026-02-16):** Serilog was not adopted. A custom `DebugLogService` singleton was implemented instead:
- In-memory `ObservableCollection<string>` capped at 2 000 entries
- All writes dispatched to the UI thread
- Exposed via `DebugLogWindow` in the desktop app
- Use `DebugLog.Log(category, message)` / `DebugLog.LogError(...)` throughout ViewModels

File-based structured logging remains deferred. If added in the future, Microsoft.Extensions.Logging with a file sink is the preferred approach.

---

## Decision 010: Testing Strategy

**Date:** 2025-02-14  
**Status:** Approved  
**Context:** Need quality assurance without slowing development

**Decision:** Unit tests for Core library (40% line coverage threshold enforced in CI), manual testing for UI

**Rationale:**
- Core business logic is most critical
- UI testing in Avalonia is immature/complex
- Manual testing acceptable for hobby project
- xUnit is .NET standard
- Mocking Graph API is straightforward

**Consequences:**
- UI bugs may not be caught early
- No automated regression testing for UI
- Manual test effort increases with features

**Alternatives Rejected:**
- Full UI automation: Too much effort for MVP
- No tests: Risky for refactoring
- Only integration tests: Slow, fragile

---

## Decision 011: Version Control Strategy

**Date:** 2025-02-14  
**Status:** Approved  
**Context:** Solo developer, private repository

**Decision:** Git with feature branches, private GitHub repository

**Rationale:**
- Industry standard version control
- GitHub provides free private repos
- Feature branches enable clean history
- Can add collaborators later if needed
- GitHub Actions available for CI/CD future

**Consequences:**
- Overhead of branching for solo dev
- Must maintain branch discipline
- Merge conflicts unlikely but possible

**Alternatives Rejected:**
- Trunk-based: Too risky without CI
- No VCS: Unacceptable for software project
- GitLab/Bitbucket: GitHub more familiar

---

## Decision 012: Dependency Management

**Date:** 2025-02-14  
**Status:** Superseded — see implementation note  
**Context:** Need to manage NuGet package versions across projects

**Original Decision:** Central Package Management (Directory.Packages.props)

**Implementation Note (2026-02-16):** Central Package Management was not implemented. Package versions are pinned directly in each `.csproj` file. `Directory.Packages.props` does not exist in the repository. This keeps the project structure simpler and avoids the additional tooling overhead for a two-project solution.

---

## Decision 013: Export/Import Scope (Phase 1)

**Date:** 2025-02-14  
**Status:** Approved  
**Context:** Phase 1 targets Device Configurations only

**Decision:** Device Configurations as first object type

**Rationale:**
- Most commonly used Intune object type
- Simpler than Applications (no .intunewin files)
- Well-documented Graph API
- Proves export/import pattern for other types
- PowerShell version handles this well (reference implementation)

**Consequences:**
- Other object types wait until Phase 3
- Can't test cross-object dependencies yet
- Users can't fully migrate tenant in Phase 1

**Alternatives Rejected:**
- Applications first: Too complex with app packages
- All objects at once: Too ambitious for Phase 1

---

## Decision 014: Profile Storage Encryption

**Date:** 2025-02-14  
**Status:** Approved  
**Context:** Profile file stores sensitive data (tenant IDs, auth config)

**Decision:** Platform-native encryption (DPAPI on Windows)

**Rationale:**
- OS-level security guarantees
- No custom encryption keys to manage
- Per-user encryption (profile not readable by other users)
- .NET has built-in DPAPI support

**Consequences:**
- Profiles not portable between users
- Can't easily share profiles across team
- Must implement different encryption per OS (future)

**Alternatives Rejected:**
- No encryption: Security risk
- Custom encryption: Prone to implementation errors
- Azure Key Vault: Overkill, requires connectivity

---

## Decision 015: Feature Scope Boundaries

**Date:** 2025-02-14  
**Status:** Approved  
**Context:** Original PowerShell version has many features; must prioritize

**Decisions:**
- **Included in MVP:** CRUD, export/import, multi-cloud, profiles, bulk operations
- **Excluded from MVP:** ADMX import, object comparison, documentation generation, update/replace modes

**Rationale:**
- Focus on core workflow (CRUD + export/import)
- ADMX tooling is niche, complex
- Comparison/docs can be added iteratively
- Update/replace modes are risky (test well before adding)

**Consequences:**
- Users migrating from PowerShell lose some features
- May need to run PowerShell version for excluded features
- Post-MVP roadmap already defined

**Alternatives Rejected:**
- Feature parity: Would delay MVP by months
- No exclusions: Scope creep risk

---

## Future Decisions Needed

These decisions are deferred to implementation phases:

1. **Error Message Library:** Comprehensive user-facing error messages (Phase 6)
2. **CLI Argument Syntax:** Command-line interface design (Phase 6)
3. **Docker Base Image:** Specific Linux distro for container (Phase 6)
4. **Telemetry/Analytics:** Whether to collect usage data (TBD)
5. **Update Mechanism:** Auto-update vs manual download (Post-MVP)
6. **Localization:** Support for non-English languages (Post-MVP)
7. **Theme Support:** Dark mode, color customization (Post-MVP)

---

## Decision Review Schedule

- **After Phase 1:** Review authentication and object model decisions
- **After Phase 3:** Review UI architecture and object type priorities
- **After Phase 6:** Review entire architecture before post-MVP features

Decisions can be revisited if new information emerges or requirements change.

---

## Decision 016: Profile Encryption — ASP.NET DataProtection API

**Date:** 2026-02-16
**Status:** Approved
**Context:** Phase 2 requires encrypting saved profile data at rest. Options: Windows DPAPI, ASP.NET DataProtection API, or skip.

**Decision:** ASP.NET DataProtection API (`Microsoft.AspNetCore.DataProtection`)

**Rationale:**
- Cross-platform (Windows, macOS, Linux) — aligns with Avalonia's cross-platform story
- Keys stored in `%LOCALAPPDATA%\Intune.Commander\keys\`
- Automatic key rotation and management built-in
- Transparent migration from plaintext (existing profiles auto-encrypt on next save)
- Graceful handling of corrupted/migrated data (falls back to empty store)

**Consequences:**
- Adds `Microsoft.AspNetCore.DataProtection` NuGet dependency to Core project
- Profile file prefixed with `INTUNEMANAGER_ENC:` marker to distinguish encrypted from plaintext

---

## Decision 017: Profile Switch — Confirmation Dialog

**Date:** 2026-02-16
**Status:** Approved
**Context:** Users need to switch between tenant profiles while connected. Options: auto-reconnect, confirm dialog, or manual disconnect-first.

**Decision:** Confirm dialog before switching

**Rationale:**
- Prevents accidental disconnection from active tenant
- Uses `MessageBox.Avalonia` for native-feeling dialog
- ViewModel raises event, View handles dialog — clean MVVM separation

**Consequences:**
- Adds `MessageBox.Avalonia` NuGet dependency to Desktop project
- Slightly more steps than auto-reconnect, but safer

---

## Decision 018: Profile Validation — Basic GUID Format Checks

**Date:** 2026-02-16
**Status:** Approved
**Context:** Need input validation for tenant profiles. Options: minimal (non-empty), format checks (GUID validation), or format + live connection test.

**Decision:** Basic format validation — GUID format for Tenant ID and Client ID, non-empty required fields

**Rationale:**
- Catches typos immediately without requiring network access
- Inline error messages below each field
- Validation state gates Save and Connect buttons via `CanExecute`
- No live connection test — avoids delays and false negatives

---

## Decision 019: Secret Storage — Deferred

**Date:** 2026-02-16
**Status:** Deferred
**Context:** `ClientSecret` is currently stored in profile JSON. Options: remove from storage, encrypt in file, or use OS credential store.

**Decision:** Deferred — no changes to secret handling in Phase 2

**Rationale:**
- Profile file is now encrypted at rest via DataProtection, providing baseline protection
- OS credential store integration (Windows Credential Manager, macOS Keychain) planned for a future phase
- Current approach works for development; production hardening will follow
