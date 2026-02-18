# Pull Request Status and Organization

*Last Updated: 2026-02-18*

## Overview

This document provides a comprehensive overview of all open pull requests, their priorities, relationships, and recommended merge order.

## Open PRs Summary

| PR# | Priority | Category | Status | Title |
|-----|----------|----------|--------|-------|
| #15 | P1 - Critical | Refactor/Auth | ✅ Ready | Remove deferred Certificate/ManagedIdentity auth options |
| #16 | P2 - Important | Documentation | 🔍 Needs Review | Reconcile README and ARCHITECTURE.md with implementation |
| #19 | P2 - Important | Documentation | 🔍 Needs Review | Fix 8 accuracy issues in README and ARCHITECTURE |
| #22 | P2 - Important | Documentation | 🔍 Needs Review | Correct dependency management section |
| #23 | P2 - Important | Documentation | 🔍 Needs Review | Align encryption decision with DataProtection |
| #17 | P3 - Enhancement | Architecture | ⏸️ Needs Decision | Add ICategoryLoader abstraction |
| #20 | P3 - Enhancement | Architecture | ⏸️ Blocked by #17 | Explain ICategoryLoader cache-read options |
| #21 | P3 - Enhancement | Architecture | ⏸️ Blocked by #17 | Add FormatError callback to CategoryLoadContext |
| #24 | Meta | Organization | 🚧 In Progress | Organize and label pull requests (this PR) |

## Status Legend
- ✅ **Ready**: Tested, reviewed, ready to merge
- 🔍 **Needs Review**: Awaiting review/approval
- ⏸️ **Blocked**: Waiting on another PR or decision
- 🚧 **In Progress**: Actively being worked on

## Priority Definitions

### P1 - Critical
**Must merge first.** These PRs fix bugs, security issues, or remove broken functionality that could mislead users.

- **#15**: Auth refactoring - removes non-functional auth methods (Certificate, ManagedIdentity) that silently fell back to Interactive mode, creating a false affordance

### P2 - Important  
**Should merge soon.** Documentation accuracy is critical for maintainability and onboarding.

- **#16, #19, #22, #23**: Documentation fixes - align docs with actual implementation
  - All address documentation drift where docs described aspirational or incorrect behavior
  - Can be merged independently in any order
  - No code changes, only documentation updates

### P3 - Enhancement
**Future improvements.** Architectural additions for future development.

- **#17**: Category loader abstraction - guardrails for future category implementations
- **#20**: Explains architectural options for #17
- **#21**: Improves error handling in #17
  - These three PRs form a cohesive set around category loading patterns
  - #20 and #21 are follow-ups to address #17 review feedback
  - All three can merge together or be held for future work

## PR Dependencies

```
#15 (P1) ─── No dependencies (can merge immediately)
│
├─ #16 (P2) ─── No dependencies (independent doc fix)
├─ #19 (P2) ─── No dependencies (independent doc fix)  
├─ #22 (P2) ─── No dependencies (independent doc fix)
└─ #23 (P2) ─── No dependencies (independent doc fix)

#17 (P3) ─── No dependencies but needs architectural decision
│
├─ #20 (P3) ─── Depends on #17 (explains cache-read options)
└─ #21 (P3) ─── Depends on #17 (adds FormatError callback)
```

## Recommended Merge Order

### Phase 1: Critical Fix
1. **Merge #15** - Auth refactoring (P1)
   - Removes broken auth options
   - Has tests (241 tests passing)
   - No dependencies

### Phase 2: Documentation Cleanup
Merge these in any order (all independent):
2. **Merge #16** - Main README/ARCHITECTURE reconciliation
3. **Merge #19** - Fix 8 specific inaccuracies  
4. **Merge #22** - Dependency management correction
5. **Merge #23** - Encryption strategy alignment

### Phase 3: Architecture Enhancements (Optional - Can be deferred)
Make architectural decision on #17 first, then:
6. **Decision needed on #17** - ICategoryLoader abstraction
   - Option A: Merge as-is (cache in helper)
   - Option B: Remove cache from helper (keep in VM setter only)
   - Option C: Close and defer until 31st category is needed
7. **Merge #20** - Documentation explaining the decision
8. **Merge #21** - Error handling improvement

### Phase 4: Cleanup
9. **Merge #24** - This meta PR (organization documentation)

## Detailed PR Analysis

### PR #15: Auth Refactoring [P1 - Critical]
**Status**: ✅ Ready to merge  
**Changes**: 
- Removes `Certificate` and `ManagedIdentity` from `AuthMethod` enum
- Removes unused `CertificateThumbprint` from `TenantProfile`
- Hardens error handling in `InteractiveBrowserAuthProvider`
- Adds 6 unit tests

**Why Critical**: The removed auth methods were displayed to users but didn't work, silently falling back to Interactive. This is misleading and creates a false affordance.

**Merge Risk**: Low - has tests, no known blockers

---

### PRs #16, #19, #22, #23: Documentation Fixes [P2 - Important]
**Status**: 🔍 Needs review  
**Common Theme**: All fix documentation drift where docs claimed things that don't match implementation

**#16 - Main reconciliation**
- Fixes: Graph SDK version, tech stack, auth methods, profile schema, DI lifetimes, views/viewmodels, logging, certificate handling
- Scope: Comprehensive README and ARCHITECTURE updates

**#19 - 8 specific fixes** (from #16 review)
- Fixes: Graph permissions wording, ViewModels structure, auth behavior, profile schema, encryption decision, DI method names, dependency management
- Scope: Targeted corrections based on review feedback

**#22 - Dependency management**
- Fixes: Incorrect claim about `Directory.Packages.props` central package management
- Reality: Versions are pinned per `.csproj`

**#23 - Encryption strategy**
- Fixes: ARCHITECTURE claimed platform-specific DPAPI/Keychain/libsecret
- Reality: Uses `Microsoft.AspNetCore.DataProtection` with file-system key storage

**Merge Risk**: Very low - documentation only, no code changes  
**Review Notes**: Check for any overlap between PRs to avoid duplicate fixes

---

### PR #17: ICategoryLoader Abstraction [P3 - Enhancement]
**Status**: ⏸️ Needs architectural decision  
**Changes**:
- Adds `ICategoryLoader` interface
- Adds `CategoryLoadContext` record
- Adds `CategoryLoadHelper` static class
- No existing code modified (purely additive)

**Purpose**: Provides guardrails for implementing the 31st+ category (currently have 30)

**Architectural Question**: Where should cache-read logic live?
- Current code: VM setter calls `TryLoadLazyCacheEntry` before calling `LoadXAsync`
- #17 implementation: `CategoryLoadHelper.ExecuteAsync` reads cache internally
- This creates potential double-check pattern

**Options**:
1. **Keep cache in helper** - simpler for new categories, breaks consistency
2. **Remove cache from helper** - consistent with existing 30 categories
3. **Defer until needed** - close PR, implement when 31st category is added

**Merge Risk**: Low - additive only, doesn't change existing code  
**Decision Needed**: Owner should choose architectural direction

---

### PR #20: Cache-Read Architectural Options [P3 - Enhancement]
**Status**: ⏸️ Blocked by #17  
**Purpose**: Documents the two architectural approaches for cache handling in response to #17 review

**Contents**:
- Option 1: Pure network-fetch wrapper (remove cache from ExecuteAsync)
- Option 2: Self-contained helper (keep cache in ExecuteAsync)
- Explains tradeoffs

**Merge Risk**: N/A - no code changes, pure documentation  
**Note**: Should merge immediately after #17 decision is made

---

### PR #21: FormatError Callback [P3 - Enhancement]
**Status**: ⏸️ Blocked by #17  
**Purpose**: Adds `FormatError` callback to `CategoryLoadContext` for consistent ODataError handling

**Changes**:
- Updates `CategoryLoadContext` to accept `Func<Exception, string> FormatError`
- Changes error handling from `ex.Message` to `ctx.FormatError(ex)`
- Maintains consistency with existing 30+ categories that use `FormatGraphError(ex)`

**Merge Risk**: Low - small additive change  
**Note**: Direct improvement to #17, should merge together

---

### PR #24: Organization [Meta]
**Status**: 🚧 In Progress  
**Purpose**: Creates this organizational documentation to help understand PR status

**Deliverables**:
- PR_STATUS.md (this file)
- Updated CHANGELOG.md

## Action Items for Repository Owner

### Immediate Actions
- [ ] Review and merge **PR #15** (P1 - Critical auth refactoring)
- [ ] Review documentation PRs **#16, #19, #22, #23** (P2 - can be done in parallel)

### Short-term Decisions Needed
- [ ] **Architectural Decision on PR #17**: Choose cache-read pattern
  - Review feedback in PR #17 and #20
  - Decide: merge as-is, modify cache behavior, or defer until 31st category
  - Document decision in #20
  - Merge #21 (error handling) along with final #17 decision

### Documentation Maintenance
- [ ] After merging documentation PRs, verify no remaining drift
- [ ] Update CHANGELOG.md with all merged changes
- [ ] Consider creating CONTRIBUTING.md with PR standards

## Notes for Future PRs

### PR Title Convention
- Prefix with type: `feat:`, `fix:`, `docs:`, `refactor:`, `test:`
- Include priority in brackets: `[P1]`, `[P2]`, `[P3]`
- Example: `feat(auth): add certificate support [P1]`

### PR Description Template
Every PR should include:
- **Summary**: What and why
- **Changes**: Bullet list of modifications
- **Test Plan**: How to verify (with checkboxes)
- **Breaking Changes**: If any (with migration guide)

### Label Recommendations
Consider creating these GitHub labels:
- `priority: critical` (P1)
- `priority: high` (P2)  
- `priority: medium` (P3)
- `type: documentation`
- `type: refactor`
- `type: feature`
- `type: bugfix`
- `status: needs-review`
- `status: needs-decision`
- `status: blocked`

## Wave Tracking Integration

The repository uses Wave tracking for service implementation:
- **Wave 1-5** documents in `docs/issues/`
- **Wave 1** (Issue #13) - Completed (Endpoint Security, Admin Templates, Enrollment)
- **Waves 2-5** - Not yet started

Open PRs are independent of Wave implementation and should be resolved first to provide clean baseline for future Wave work.
