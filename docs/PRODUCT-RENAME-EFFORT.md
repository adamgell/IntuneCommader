# Product Rename Effort Assessment

This note estimates the effort to fully rename the product from the current `IntuneManager.*` identity to `Intune.Commander.*` (for example `Intune.Commander.Core`).

## What a "full rename" touches

Based on the current codebase:

- **~189 tracked file paths** include `IntuneManager` (project folders, file names, solution references).
- **~169 C# files** declare `namespace IntuneManager...`.
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

- [ ] Rename solution/projects/folders from `IntuneManager*` to `Intune.Commander*`.
- [ ] Rename namespaces (`IntuneManager.*` → `Intune.Commander.*`) and fix all compile references.
- [ ] Update CI/workflows/scripts/docs for renamed paths and project names.
- [ ] Implement runtime migration to preserve existing local profile/cache readability.
- [ ] Validate with `dotnet build` and `dotnet test --filter "Category!=Integration"`.

### Acceptance criteria for Option B

1. `dotnet build` succeeds with renamed solution/projects.
2. Unit tests pass with `--filter "Category!=Integration"`.
3. Existing users with `%LOCALAPPDATA%/IntuneManager/*` data can still load profiles/cache after upgrade.
4. No hard-coded `IntuneManager` references remain in active source/build paths, except explicitly documented legacy-compatibility constants.

## Option B split into issues and pull requests

### Issue 1: Rename solution/projects and namespaces
- **Scope:** rename `IntuneManager.sln`, project names/folders, namespaces/usings, and test project references.
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
