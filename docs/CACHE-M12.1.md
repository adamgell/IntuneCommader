# M12.1 — Blob read-through cache (implementation plan)

Implements the **layer-one** of [CACHE.md](./CACHE.md): wire the already-built but unwired LiteDB
`CacheService` in front of the sidecar's LIST/DETAIL endpoints, warm it with the existing
`PrefetchAllToCacheAsync`, and invalidate on write. **Server-side change; the client gets one
status line.** Grounded + adversarially reviewed against the source (a 10-agent workflow); every
file:line below was verified in-repo.

## 1. Objective & Definition of Done

- **DoD #1 — offline browse.** After a warm, LIST/DETAIL screens render from cache while Graph is
  unreachable (sidecar up, signed in).
- **DoD #2 — no redundant round-trip.** A second LIST GET for an unchanged surface does **not** call
  Graph (served from cache).
- **Non-goals (later milestones):** the relationship graph (M12.2+), `$delta` (M12.2), background
  scheduled refresh + at-rest encryption of a *new* store (M12.5). M12.1 reuses the **existing**
  encrypted LiteDB cache as-is.

## 2. Design decisions (locked, from the planning workflow)

| Fork | Choice |
|---|---|
| **What to cache** | The **raw Graph `List<T>`** the Core services already return (the cache stores runtime Graph types, `CacheService.cs:362-377`). LIST caches `List<T>`; DETAIL caches the single Graph object. No projected-DTO cache — DTO projection stays per-request after the cache read. |
| **Seam** | A thin Api-side **`CachedReader`** helper called inline in each `MapGet`, *between* the `new XService(g).List/Get` call and the `ListItemDto`/`CrudJson.ToJson` projection. It wraps the fetch as a delegate, so it works for **all** surfaces regardless of whether the Core service has a cache ctor (only `AssignmentCheckerService` does). Rejected: injecting cache into every Core service (covers 1 of ~40); per-endpoint inline (56 hand-written copies). |
| **Warm** | Combination: **sign-in completion** (primary) + **`POST /sync`** (forceRefresh) + **lazy read-through** (cold-start backstop). All fire-and-forget, discovered via the existing 2 s `/health` poll. Warming reuses `PrefetchAllToCacheAsync` (`AssignmentCheckerService.cs:1183`) verbatim. |
| **Invalidate** | **One point at the endpoint write layer.** The MCP propose→approve **replay path and direct MCP writes both re-issue the same per-surface PATCH/POST/DELETE HTTP endpoints via `Loopback.Http`** (`Program.cs:307-360`), so invalidating in the handler covers human edits, approve-replay, and MCP writes at once — do **not** duplicate it in the pending-change machinery. |
| **Freshness UX** | One new server field `lastWarmedUtc` on `/health`, rendered in the existing status cluster. No per-row freshness (would force a contract + api-types churn). 24 h TTL stays as the staleness backstop only. |

## 3. Two new primitives

**`service/Api/Endpoints/CachedReader.cs`** (new) — next to `ListProjection.cs`:

```csharp
internal static class CachedReader
{
    // LIST read-through. Hit → cached List<T>; miss → fetch, cache, return.
    public static async Task<List<T>> ListAsync<T>(
        ICacheService cache, AuthSession auth, string cacheKey,
        Func<CancellationToken, Task<List<T>>> fetch, CancellationToken ct) where T : class
    {
        var tid = auth.ActiveProfile?.TenantId;
        if (cache.IsAvailable && tid is not null && cache.Get<T>(tid, cacheKey) is { } hit) return hit;
        var fresh = await fetch(ct);
        if (cache.IsAvailable && tid is not null) cache.Set(tid, cacheKey, fresh);
        return fresh;
    }

    // DETAIL read-through. Keyed {cacheKey}/{id}; LAZY populate only (see §9 gotcha 2).
    public static async Task<T?> GetAsync<T>(
        ICacheService cache, AuthSession auth, string cacheKey, string id,
        Func<CancellationToken, Task<T?>> fetch, CancellationToken ct) where T : class
    {
        var tid = auth.ActiveProfile?.TenantId; var dk = $"{cacheKey}/{id}";
        if (cache.IsAvailable && tid is not null && cache.GetSingle<T>(tid, dk) is { } hit) return hit;
        var fresh = await fetch(ct);
        if (fresh is not null && cache.IsAvailable && tid is not null) cache.SetSingle(tid, dk, fresh);
        return fresh;
    }
}
```

**`service/Api/Endpoints/CacheInvalidation.cs`** (new):

```csharp
internal static class CacheInvalidation
{
    // Call AFTER a successful write on a cacheable surface.
    public static void OnWrite(ICacheService cache, AuthSession auth, string cacheKey, string? id = null)
    {
        var tid = auth.ActiveProfile?.TenantId;
        if (!cache.IsAvailable || tid is null) return;
        cache.Invalidate(tid, cacheKey);                               // drop the LIST (mandatory)
        if (id is not null) cache.Invalidate(tid, $"{cacheKey}/{id}");  // drop the DETAIL (mandatory on PATCH/DELETE)
    }
}
```

Both resolve `tenantId` from `auth.ActiveProfile?.TenantId` (`AuthSession.cs:38`), gate on
`cache.IsAvailable` (so `NullCacheService` degrades to clean pass-through), and the signed-out `409`
check stays **above** the helper in each handler (`if (g is null) return Results.Conflict();`), so
the cache is never read while signed out.

`ICacheService cache` is added as a parameter to each touched handler lambda — it resolves from DI
(`AddSingleton<ICacheService>` in `AddIntuneCommanderCore`, `ServiceCollectionExtensions.cs:69-81`,
registered at `Program.cs:32` before `MapDevices()` at `:230`). No DI change needed; **drop** the
synth plan's "if keyed/scoped, add a shim" hedge — the registration is plain.

## 4. Surface coverage (corrected & verified)

The single source of cache keys is a new optional trailing field on the `Surface` record
(`Surfaces.cs`): `string? CacheKey = null` (trailing + defaulted so the existing 41 rows compile
unchanged — see §9 gotcha 4). For surfaces whose `CacheKey` equals a `PrefetchAllToCacheAsync`
constant, warm-ahead applies; the rest fill lazily on first online load.

**Warmed (CacheKey == a prefetch constant — verified type matches the endpoint's Core call):**

| Surface | CacheKey | Graph type | Verified |
|---|---|---|---|
| /device-configs | `DeviceConfigurations` | `DeviceConfiguration` | DevicesEndpoints.cs:26 |
| /compliance-policies | `CompliancePolicies` | `DeviceCompliancePolicy` | :71 |
| /settings-catalog | `SettingsCatalog` | `DeviceManagementConfigurationPolicy` | :117 |
| /admin-templates | `AdministrativeTemplates` | `GroupPolicyConfiguration` | :173 |
| **/endpoint-security** | **`EndpointSecurityIntents`** | **`DeviceManagementIntent`** | :218 — **NOT `SettingsCatalog`** (corrected) |
| /remediation-scripts | `DeviceHealthScripts` | `DeviceHealthScript` | DeviceScriptsEndpoints.cs:24 |
| /compliance-scripts | `ComplianceScripts` | `DeviceComplianceScript` | :59 |
| /platform-scripts | `DeviceManagementScripts` | `DeviceManagementScript` | :94 (1:1, no shared key) |
| /shell-scripts | `DeviceShellScripts` | `DeviceShellScript` | :129 |
| /feature-updates | `FeatureUpdateProfiles` | `WindowsFeatureUpdateProfile` | :199 (cacheable — corrected) |
| /apps | `Applications` | `MobileApp` | AppsEndpoints.cs:31 |
| /app-protection | `AppProtectionPolicies` | `ManagedAppPolicy` | :55 |
| /app-configs | `ManagedDeviceAppConfigurations` | `ManagedDeviceMobileAppConfiguration` | AppsEndpoints |
| /enrollment-configs | `EnrollmentConfigurations` | `DeviceEnrollmentConfiguration` | EnrollmentEndpoints |
| /autopilot | `AutopilotProfiles` | `WindowsAutopilotDeploymentProfile` | EnrollmentEndpoints |
| /conditional-access | `ConditionalAccessPolicies` | `ConditionalAccessPolicy` | IdentityEndpoints (plain LIST only) |
| /named-locations | `NamedLocations` | `NamedLocation` | IdentityEndpoints |
| /auth-strengths | `AuthenticationStrengths` | `AuthenticationStrengthPolicy` | IdentityEndpoints |
| /auth-contexts | `AuthenticationContexts` | `AuthenticationContextClassReference` | IdentityEndpoints |
| /terms-of-use | `TermsOfUseAgreements` | `Agreement` | IdentityEndpoints |
| /scope-tags | `ScopeTags` | `RoleScopeTag` | TenantAdminEndpoints |
| /role-definitions | `RoleDefinitions` | `RoleDefinition` | TenantAdminEndpoints |
| /intune-branding | `IntuneBrandingProfiles` | `IntuneBrandingProfile` | TenantAdminEndpoints |
| /azure-branding | `AzureBrandingLocalizations` | `OrganizationalBrandingLocalization` | TenantAdminEndpoints |
| /terms-conditions | `TermsAndConditions` | `TermsAndConditions` | TenantAdminEndpoints |
| /policy-sets | `PolicySets` | `PolicySet` | TenantAdminEndpoints |
| /assignment-filters | `AssignmentFilters` | `DeviceAndAppManagementAssignmentFilter` | TenantAdminEndpoints |

**Lazy-only (plain `List<T>` surface, no prefetch key → cached on first online GET, no warm-ahead):**
`/device-categories`, `/quality-updates`, `/driver-updates`, `/managed-devices` (list-only),
`/vpp-tokens`, `/apple-dep`, `/cloudpc-provisioning`, `/cloudpc-user-settings`, `/admx-files`,
`/reusable-settings`, `/notification-templates`. Give each a unique `CacheKey` so read-through
caches it; they simply aren't pre-warmed.

**Live / pass-through this milestone (aggregations or enriched projections, not plain `List<T>`):**
`/dashboard`, `/security-posture`, `/assignment-explorer`, `/permission-check`, `/baselines`, the
**Conditional Access enriched** endpoints (`/conditional-access/list|{id}/summary|{id}/detail` —
they do `DirectoryObjectResolver` name resolution, `IdentityEndpoints.cs:43-74`), the
**settings-catalog settings tree** (`/settings-catalog/{id}/settings`, `DevicesEndpoints.cs:161`),
and all per-surface **`/{id}/assignments` GETs**. Document these as uncached; they need no
invalidation. `/groups` (merges assigned+dynamic, `GroupsEndpoints.cs:19-31`) is **deferred to
M12.2**, which rebuilds groups from the relationship store.

## 5. Step-by-step implementation

1. **Contract first** — `contract/openapi.yaml`: add `lastWarmedUtc` (nullable `date-time`) to the
   `SyncStatus` schema (`openapi.yaml:1310-1321`); leave `required: [healthy, authState]` intact.
2. **DTO mirror** — `service/Api/Contracts.cs`: add `string? LastWarmedUtc` to `SyncStatusDto`
   (record at `:8-16`). `crates/api-types/src/lib.rs`: add `last_warmed_utc: Option<String>` to the
   `SyncStatus` DTO.
3. **`Surface.CacheKey`** — `service/Api/Surfaces.cs`: add trailing `string? CacheKey = null` to the
   `Surface` record; set it on the cacheable rows per §4. (Existing 41 positional rows compile
   unchanged.)
4. **`CachedReader.cs` + `CacheInvalidation.cs`** — new files per §3.
5. **Wire LIST/DETAIL read-through** — in each endpoint module, wrap the existing call:
   `var items = await CachedReader.ListAsync(cache, auth, "EndpointSecurityIntents", ct => new EndpointSecurityService(g).ListEndpointSecurityIntentsAsync(ct), ct);`
   and DETAIL `var obj = await CachedReader.GetAsync(cache, auth, key, id, ct => new XService(g).GetAsync(id, ct), ct);`.
   Files: `DevicesEndpoints.cs`, `DeviceScriptsEndpoints.cs`, `AppsEndpoints.cs`,
   `EnrollmentEndpoints.cs`, `IdentityEndpoints.cs`, `TenantAdminEndpoints.cs`. Mechanical, one line
   per handler + a `ICacheService cache` lambda param.
6. **Wire write-invalidation** — in every `POST` (create → `OnWrite(cache, auth, key)`),
   `PATCH`/`DELETE` (→ `OnWrite(cache, auth, key, id)` — **detail-key drop is mandatory, not
   optional**), and `/{id}/assignments POST` (→ `OnWrite(cache, auth, key)` — the LIST `assigned`
   badge depends on `IsAssigned`) handler, **after** the Core write succeeds, **before** returning.
   No change to `/pending-changes/{id}/approve` (it replays through these same endpoints).
7. **Warm hook** — add `public Func<GraphServiceClient, string, Task>? CacheWarm { get; set; }` and
   `public DateTime? LastWarmedUtc { get; set; }` to `AuthSession`. At the sign-in success block
   (`AuthSession.cs:163-170`, right after `Graph = client`), fire-and-forget:
   `if (CacheWarm is not null && ActiveProfile?.TenantId is { } tid) _ = CacheWarm(client, tid);`.
   `Program.cs` (startup) assigns the hook:
   `auth.CacheWarm = (g, tid) => Task.Run(async () => { try { await new AssignmentCheckerService(g, cache, tid).PrefetchAllToCacheAsync(); StampWarmed(auth); } catch (Exception ex) { log.LogWarning(ex, "warm failed"); } });`
   where `StampWarmed` sets `LastWarmedUtc = Max(existing, UtcNow)` **only on success** (§9 gotcha 3).
8. **Warm on `/sync`** — in the `/sync` background task (`Program.cs:120-135`), after `sync.RunAsync`
   succeeds, also `await new AssignmentCheckerService(graph, cache, tid).PrefetchAllToCacheAsync(forceRefresh: true);`
   then `StampWarmed`. `AllFetchersAreCached` makes a redundant warm cheap.
9. **Surface `lastWarmedUtc`** — `/health` handler (`Program.cs:55-68`): include `auth.LastWarmedUtc`
   in the `SyncStatusDto`.
10. **Profile-switch fix (required — see §9 gotcha 1)** — make `AuthSession.Activate` clear the
    session (`Graph = null`, state → SignedOut, `LastWarmedUtc = null`) so switching tenants forces a
    re-sign-in. A token for tenant A is not valid for tenant B; this is correct independent of
    caching, and it prevents writing tenant A's data under tenant B's cache key.
11. **Client status line** — `app/src/main.rs` `status_text` (~`:351-357`): render `last_warmed_utc`
    next to `last sync` (e.g. "data ready · 2m ago"). Reuse the existing 2 s `/health` poll; no new
    endpoint, no per-row field.

## 6. Contract & client changes (minimal)

- **Contract:** one field (`lastWarmedUtc`) on `SyncStatus` — contract-first, before the DTOs.
- **Client:** one render line in `main.rs`. `api_client.rs` is untouched (read-through is transparent
  to `get_list`/`get_detail`; `409→empty` and `service_err` behaviors are unchanged).

## 7. Test plan

- **DoD #2 (no re-fetch) — deterministic, no live timing:** in `Api.Tests`, after one LIST GET assert
  `ICacheService.GetMetadata(tid, key)` has a `CachedAt`; issue a second GET and assert `CachedAt`
  **did not advance** (proves the second call was served from cache, not Graph). This needs the
  fixture signed in (`SidecarFixture` reuses the live sidecar — note the live-tenant caveat).
- **DoD #1 (offline render):** a unit-style test at the `CachedReader` boundary with a
  **Graph-throwing fetch delegate** + a populated `CacheService` → assert it returns the cached list
  (not the throw). Plus a supplementary manual `RUNBOOK` step (warm → kill network → browse).
- **Invalidation:** write → assert `GetMetadata` for the LIST key is gone (and the `{key}/{id}` detail
  key on PATCH/DELETE).
- **Key lockstep:** a contract test asserting every `Surfaces.cs` `CacheKey` that matches a
  `PrefetchAllToCacheAsync` constant is type-correct and unique (catches a future `endpoint-security`
  regression). Extend the existing `Surfaces.cs ↔ routes` lockstep test for the new record field.
- **NullCacheService:** with cache unavailable, LIST/DETAIL still return live data (clean pass-through).

## 8. Sequencing

- **Serializable spine:** Steps 1→2→3→4 (contract, DTOs, CacheKey, helpers) must land first.
- **Parallelizable (subagent batch, one per endpoint module):** Step 5+6 across `DevicesEndpoints`,
  `DeviceScriptsEndpoints`, `AppsEndpoints`, `EnrollmentEndpoints`, `IdentityEndpoints`,
  `TenantAdminEndpoints` — each module is independent and mechanical.
- **Then:** Steps 7-10 (warm hook, `/sync` warm, `/health`, Activate fix) in `AuthSession.cs` +
  `Program.cs`; Step 11 client; Step 7 tests.
- Build gate: one `dotnet build service/CmProjectX.slnx` then `cargo build` (never two concurrent
  `dotnet build` of the same csproj).

## 9. Risks, invariants & the sharp gotchas

**Invariants honored:** `service/Store/` (append-only) is **untouched** — all changes are in
Core(reuse)/Api/contract/app/api-types. Contract-first (Step 1 before DTOs). `app/` edition-2024 gets
one render line. Single `slnx` build (no concurrent dotnet builds). Sidecar single-instance mutex on
5099 is relied on, not changed (approve-replay + MCP hit the same in-process loopback). No new native
deps (LiteDB + DataProtection already framework-dependent) → ARM64 clean.

**Three gotchas that will bite if missed (all verified):**

1. **Profile-switch cross-tenant leak.** `Activate` (`AuthSession.cs:44`) does **not** clear `Graph`,
   so `ActiveProfile.TenantId` can be tenant B while `Graph` is tenant A → read-through caches A's
   data under B's key. **Fix in Step 10** (Activate clears the session). Must be in M12.1, not
   deferred — caching makes this latent bug data-corrupting.
2. **DETAIL is lazy-only; warm does not cover it.** `PrefetchAllToCacheAsync` only fills LIST keys; it
   never `SetSingle`s. Offline DETAIL works **only for objects opened at least once online**. Do
   **not** try to derive detail from list elements — several LIST Core methods return `$select`-shaped
   partial objects (e.g. settings-catalog list carries `SettingCount`/`IsAssigned`, not the full
   policy), so seeding detail from them would serve **truncated** JSON. Accept lazy-only this
   milestone; document it.
3. **`lastWarmedUtc` honesty.** Stamp it **only inside the `try`, after `PrefetchAllToCacheAsync`
   returns without throwing**, and take `Max(existing, now)` so an out-of-order slow warm can't regress
   it. A partial/throttled warm leaves the prior stamp; the lazy read-through self-heals the gap.
4. **Record-arity.** `CacheKey` must be the **trailing defaulted positional** field on `Surface` so the
   41 existing rows compile without edits; update the `Surfaces.cs ↔ routes` contract test if it pins
   arity.

**Backstop:** 24 h TTL (`CacheService.cs:28`) catches any missed invalidation within a day.

## 10. Implementation status (landed)

All of §5 is implemented and **builds clean with the stack stopped** (verified 2026-06-12, port 5099
free): `dotnet build service/Api/Api.csproj --configuration Release` → **0 errors** (only pre-existing
NU1903 vulnerability + CrudJson obsolete-API warnings), and `cargo build --workspace` → clean (only the
pre-existing `post_text` dead-code warning). The earlier "blocked link" was solely the running local
stack holding the output binaries; with it stopped the code links.

- **Helpers:** `service/Api/Endpoints/CachedReader.cs`, `CacheInvalidation.cs` (new).
- **Catalog:** `Surfaces.cs` — `CacheKey` field + per-surface keys (warmed keys = `PrefetchAllToCacheAsync`
  constants; `/endpoint-security` = `EndpointSecurityIntents`; lazy keys for the prefetch-less plain
  lists; `null` for aggregations/`/groups`).
- **Read-through + invalidation:** wired across all 6 endpoint modules (`Devices`, `DeviceScripts`,
  `Apps`, `Enrollment`, `Identity`, `TenantAdmin`) — every LIST/DETAIL handler reads through; every
  POST/PATCH/DELETE/assign invalidates (PATCH/DELETE drop the detail key too). Enriched/aggregation
  routes (CA `/list|/summary|/detail`, settings-tree, assignments GETs) left live, as designed.
- **Warm + freshness:** `AuthSession` gains `CacheWarm`/`LastWarmedUtc`/`MarkWarmed`; `Program.cs`
  wires the warm at sign-in and on `/sync` (forceRefresh) and exposes `lastWarmedUtc` on `/health`;
  the client renders a "cache warm" hint. Contract-first field added to `openapi.yaml` →
  `Contracts.cs` → `api-types`.
- **Cross-tenant fix:** `AuthSession.Activate` now clears the session on a tenant switch.

**Tests:** `service/Api.Tests/CacheTests.cs` — `/health` carries `lastWarmedUtc`; sign-in warms it;
read-through is stable across two reads. **6/6 green** against the live sidecar.

**Runtime smoke found + fixed two real bugs** (compile-clean code that misbehaved at runtime):
1. **`PrefetchAllToCacheAsync` was all-or-nothing** — one type the app registration can't read
   (or a bad query) threw and aborted the entire warm, so `lastWarmedUtc` never stamped. Fixed in
   `AssignmentCheckerService.cs` with **per-fetcher isolation** (`SafeFetch`): the warm is now
   best-effort — it caches every permitted type, skips the rest, and completes. A cache warm must be
   resilient because a real app registration rarely holds all ~32 permissions.
2. **`FetchAuthenticationStrengthPoliciesAsync` sent `$top`** to the `authenticationStrength/policies`
   endpoint, which rejects it (`ODataError: Query option 'Top' is not allowed`). Removed the `$top`.

After the fixes, sign-in warm completes in ~2 s and `lastWarmedUtc` stamps. **Deferred:** `/groups`
caching → M12.2; a true Graph-unreachable offline test (needs network manipulation).
