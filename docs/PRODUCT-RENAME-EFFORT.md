# Product Rename Effort Assessment

This note estimates the effort to fully rename the product from the current `IntuneManager.*` identity to `Intune.Commander.*` (for example `Intune.Commander.Core`).

## What a "full rename" touches

Based on the current codebase:

- **~189 tracked file paths** include `IntuneManager` (project folders, file names, solution references).
- **~169 C# files** declare `namespace Intune.Commander...`.
- **~124 docs/workflow/readme mentions** reference `IntuneManager` or `Intune Commander`.

In addition to source files, there are product identity values that affect persisted user data:

- Profile data path currently uses `%LOCALAPPDATA%/IntuneManager/profiles.json`.
- Cache data path currently uses `%LOCALAPPDATA%/IntuneManager/cache.db`.
- Encryption and protection markers include `INTUNEMANAGER_ENC:`, `IntuneManager.Profiles.v1`, and `IntuneManager.Cache.Password.v1`.

## Estimated effort

### Option A - Display-only rename (low risk)
Change user-facing naming only (window title, README/docs text, app metadata), while leaving technical names (`IntuneManager.*` namespaces/paths) unchanged.

- **Estimate:** ~0.5 to 1 day
- **Risk:** Low
- **Impact:** Branding looks updated; internals remain old names.

### Option B - Full technical rename (medium/high risk)
Rename solution/project names, folders, namespaces, references, workflows/scripts/docs, and add data migration for existing local profile/cache/encryption artifacts.

- **Estimate:** ~3 to 5 engineering days
- **Risk:** Medium/High without staged migration
- **Impact:** Clean, consistent product identity across code, build, and runtime storage.

## Suggested phased approach for full rename

1. **Code identity pass**
   - Rename solution/projects/folders and namespaces.
   - Update `using` references and test project references.
2. **Build/CI pass**
   - Update workflow paths, build/release scripts, and any hard-coded project names.
3. **Runtime data compatibility pass**
   - Support loading legacy `%LOCALAPPDATA%/IntuneManager/*` and migrating to new path/name.
   - Preserve compatibility for existing encrypted profile/cache data markers.
4. **Docs/verification pass**
   - Update README/docs and validate build + unit tests + basic app startup flows.

## Recommendation

If the priority is branding speed, do Option A now and plan Option B as a separate tracked migration task.  
If the priority is long-term consistency, execute Option B with explicit backward-compatibility handling for local stored data.

## Selected path: Option B

Per PR feedback, proceed with **Option B (full technical rename)**.

### Option B delivery checklist

- [x] Rename solution/projects/folders from `IntuneManager*` to `Intune.Commander*`. *(Phase 1 — PR #46)*
- [x] Rename namespaces (`IntuneManager.*` → `Intune.Commander.*`) and fix all compile references. *(Phase 1 — PR #46)*
- [x] Update CI/workflows/scripts/docs for renamed paths and project names. *(Phase 2 — PR #48)*
- [x] Implement runtime migration to preserve existing local profile/cache readability. *(Phase 3 — this PR)*
- [ ] Validate with `dotnet build` and `dotnet test --filter "Category!=Integration"`. *(Phase 4)*

### Acceptance criteria for Option B

1. `dotnet build` succeeds with renamed solution/projects.
2. Unit tests pass with `--filter "Category!=Integration"`.
3. Existing users with `%LOCALAPPDATA%/IntuneManager/*` data can still load profiles/cache after upgrade.
4. No hard-coded `IntuneManager` references remain in active source/build paths, except explicitly documented legacy-compatibility constants.

## Option B split into issues and pull requests

### Issue 1: Rename solution/projects and namespaces
- **Scope:** rename solution file from `IntuneManager.sln` to `Intune.Commander.sln`, and update project names/folders, namespaces/usings, and test project references.
- **PR:** `rename/option-b-01-solution-project-namespace`
- **Exit criteria:** `dotnet build` and `dotnet test --filter "Category!=Integration"` pass.

### Issue 2: Update CI/workflows/scripts/docs for renamed paths
- **Scope:** workflow path updates, release/build scripts, references in README/docs/CONTRIBUTING/CLAUDE.
- **PR:** `rename/option-b-02-ci-scripts-docs`
- **Exit criteria:** CI workflows resolve correct project paths and docs reflect new naming.

### Issue 3: Runtime storage migration and compatibility
- **Scope:** migrate local paths and preserve compatibility for existing profile/cache/protection markers.
- **PR:** `rename/option-b-03-runtime-migration`
- **Exit criteria:** app loads legacy `%LOCALAPPDATA%/IntuneManager/*` data and writes to new location.

### Issue 4: Cleanup and verification sweep
- **Scope:** remove remaining active `IntuneManager` references (except documented legacy constants), final docs and verification.
- **PR:** `rename/option-b-04-cleanup-verification`
- **Exit criteria:** no active hard-coded old-name references in source/build paths; build + unit tests pass.

## Claude Code prompt (issue + PR execution)

Use the prompt below in Claude Code to execute Option B as separate issues and PRs using this repo's existing files.

```text
You are implementing Option B (full technical rename) in adamgell/IntuneCommader.

Use this file as source of truth:
- /home/runner/work/IntuneCommader/IntuneCommader/docs/PRODUCT-RENAME-EFFORT.md

Follow the split plan already defined there:
1) rename/option-b-01-solution-project-namespace
2) rename/option-b-02-ci-scripts-docs
3) rename/option-b-03-runtime-migration
4) rename/option-b-04-cleanup-verification

Execution rules:
- Do one phase per PR only; do not mix phases.
- Keep changes minimal and surgical.
- Reuse existing repo patterns and architecture (see CLAUDE.md and .github/copilot-instructions.md).
- Preserve async-first UI constraints and existing behavior.
- Do not break backward compatibility for local profile/cache data.
- Run validation for every PR:
  - dotnet build
  - dotnet test --filter "Category!=Integration"

For each phase:
1. Create/update one issue in this format:
   - Title: Option B Phase N - <phase name>
   - Scope: exact files expected to change
   - Out of scope: all other phases
   - Acceptance criteria: copy from PRODUCT-RENAME-EFFORT.md
2. Implement only that phase.
3. Update docs impacted by the phase.
4. Open PR with:
   - Summary
   - Files changed
   - Validation output
   - Risk + rollback notes

Phase-specific guidance:
- Phase 1: rename solution/project/folder/namespace/usings/test references only.
- Phase 2: update workflow paths, scripts, README/docs/CONTRIBUTING/CLAUDE references.
- Phase 3: add runtime migration so legacy %LOCALAPPDATA%/IntuneManager/* remains readable; keep compatibility markers working.
- Phase 4: remove remaining active IntuneManager references except explicitly documented legacy compatibility constants; final verification sweep.

Completion gate:
- After each phase PR, ensure CI paths are correct and tests/build pass before moving to next phase.
```
