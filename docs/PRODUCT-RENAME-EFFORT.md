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
