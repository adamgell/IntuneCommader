# Plan: Main Window "Download All to Cache" with Streaming

## TL;DR
Add a "Download All to Cache" button to the main window toolbar that aggressively prefetches **all 32+ data types** (policies, groups, top-N users) from Graph API using parallel batches of ~5, streaming results into the VM's ObservableCollections live so the user can browse tabs while loading continues. If a user/device/group isn't found locally later, search falls through to Graph.

---

## Steps

### Phase 1 — ViewModel: `DownloadAllToCacheAsync` command

1. **Add `[RelayCommand] DownloadAllToCacheAsync` to `MainWindowViewModel`** (likely in `MainWindowViewModel.Loading.cs`).
   - Guard: `if (!IsConnected || _isDownloadingAll) return;`
   - New `[ObservableProperty] bool _isDownloadingAll` for UI binding.
   - New `[ObservableProperty] string _downloadProgress` for status text (e.g. "Downloading 5 of 32…").
   - Create linked `CancellationTokenSource`, wire to a cancel action.

2. **Build a task list of all 32 data types**, each as a `Func<CancellationToken, Task>` that:
   - Calls the existing service list method (e.g. `_configProfileService!.ListDeviceConfigurationsAsync(ct)`) — **all 29 services already have paginated `List*Async` methods with `@odata.nextLink` loops; no new fetch logic needed**
   - Dispatches results into the corresponding `ObservableCollection` on the UI thread
   - Sets the corresponding `_*Loaded = true` flag
   - Saves to cache via existing `_cacheService.Set(tenantId, cacheKey, items)`
   - Reports progress (increment counter, update `DownloadProgress`)

3. **Execute in parallel batches of ~5** using `SemaphoreSlim(5)` or chunked `Task.WhenAll`. Each completed task streams its results into the collection immediately. Update progress text after each batch/task completes.

4. **Include groups + users**:
   - Groups: call existing `_groupService.ListDynamicGroupsAsync` + `_groupService.ListAssignedGroupsAsync` (already paginated with `$top=200`)
   - Users: **only new method needed** — add `_userService.ListUsersAsync(CancellationToken)` with `$top=200` + `@odata.nextLink` pagination (same pattern as every other service). `SearchUsersAsync` already exists for on-demand search fallthrough.
   - These populate `DynamicGroupRows`, `AssignedGroupRows`, and a new user cache entry.

5. **On completion**: call `SaveToCache()` (existing method) for belt-and-suspenders. Set `IsDownloadingAll = false`. Update status to "All data downloaded and cached."

6. **On cancel/error**: standard pattern matching `PrefetchAllAsync` in AssignmentReportVM — catch `OperationCanceledException`, catch `Exception`, update status.

### Phase 2 — Main Window XAML: button + progress indicator

7. **Add button to left toolbar StackPanel** in `MainWindow.axaml`, after "Import":
   ```xml
   <Button Content="⬇ Download All to Cache"
           Command="{Binding DownloadAllToCacheCommand}"
           IsEnabled="{Binding IsConnected}"
           IsVisible="{Binding !IsDownloadingAll}" ... />
   <Button Content="⬛ Cancel Download"
           Command="{Binding CancelDownloadAllCommand}"
           IsVisible="{Binding IsDownloadingAll}" ... />
   ```

8. **Add a full-width determinate `ProgressBar`** below the toolbar `Border` (same `DockPanel.Dock="Top"` pattern as the Assignment Report window):
   - `IsIndeterminate="False"`, bind `Value` to `DownloadProgressPercent` (0–100 computed from `completedTasks / totalTasks * 100`)
   - `Maximum="100"`
   - `IsVisible="{Binding IsDownloadingAll}"`

9. **Show progress text** in the existing status bar or via `DownloadProgress` property bound to a `TextBlock` near the progress bar.

### Phase 3 — User/Group search fallthrough to Graph

10. **Ensure existing search methods in AssignmentReportViewModel/GroupLookupViewModel** still hit Graph live if local cache misses. This is already the case — `SearchUserAsync`, `SearchGroupAsync` call the service directly (not cache). **No change needed** — just verify behavior.

### Phase 4 — Wire up AssignmentCheckerService

11. **Expand `PrefetchAllToCacheAsync`** in `AssignmentCheckerService` to cover the same 32 types so the Assignment Report window's "Download All to Cache" button also benefits from the broader cache. Both the main window and assignment report window share the same `ICacheService` + tenant ID + cache keys, so whichever button the user clicks first warms the cache for both.

### Phase 5 — Tests

12. **Unit tests** in `tests/Intune.Commander.Core.Tests/`:
    - Test that the new user listing method returns paginated results with CancellationToken
    - Verify service contract conformance for any new/changed interfaces (existing reflection-based pattern)

---

## Relevant Files

- `src/Intune.Commander.Desktop/ViewModels/MainWindowViewModel.cs` — add `IsDownloadingAll`, `DownloadProgress`, `DownloadProgressPercent` properties
- `src/Intune.Commander.Desktop/ViewModels/MainWindowViewModel.Loading.cs` — add `DownloadAllToCacheAsync` command, reuse `LoadCollectionAsync<T>` and `SaveToCache` patterns
- `src/Intune.Commander.Desktop/Views/MainWindow.axaml` — add button to toolbar, add progress bar below toolbar
- `src/Intune.Commander.Core/Services/IUserService.cs` — add `ListUsersAsync(CancellationToken)` (only new service method in this plan)
- `src/Intune.Commander.Core/Services/UserService.cs` — implement `ListUsersAsync` using `$top=200` + `@odata.nextLink` loop (same pattern as all other services)
- `src/Intune.Commander.Core/Services/AssignmentCheckerService.cs` — expand `PrefetchAllToCacheAsync` from 11 to 32 types (add fetch methods for missing types, reuse `$top=200` page size)
- `tests/Intune.Commander.Core.Tests/` — add tests for new service methods

## Verification

1. `dotnet build` — no compile errors
2. `dotnet test --filter "Category!=Integration"` — all unit tests pass, coverage ≥ 40%
3. Manual test: connect to tenant → click "Download All" → observe all tabs populating progressively → cancel mid-download works → navigate to tabs, data already there
4. Manual test: in Assignment Report, search for a user not in cache → still finds via live Graph query
5. Check `DebugLog` entries show per-type download progress

## Decisions

- **Parallel batch size = 5**: balances speed vs. Graph throttling. Could make configurable later.
- **Pagination: `$top=200` in services, `$top=999` in AssignmentCheckerService**: The 29 standard services already use `$top=200` with `@odata.nextLink` pagination. `AssignmentCheckerService`'s private `Fetch*Async` methods use `$top=999`. Not all Graph Beta endpoints support 999, so the services' `$top=200` is the safer default. The only new list method needed is `UserService.ListUsersAsync` (also `$top=200`). Search methods (`SearchUsersAsync`, `SearchGroupsAsync`) remain single-page for on-demand lookups.
- **Groups = Dynamic + Assigned**: same scope as today's lazy-load. `SearchGroupsAsync` falls through to Graph for misses.
- **New command lives in MainWindowViewModel, not a service**: it needs access to all 30+ service fields + ObservableCollections + cache keys. Pushing this into a service would require massive parameter passing.
- **AssignmentCheckerService expanded to 32 types**: Both the main window and assignment report "Download All" buttons prefetch the same full set of types. Shared cache keys mean whichever fires first warms the cache for the other.
- **Scope boundaries**: This plan does NOT add new Graph permissions, change the cache TTL, or modify the DI registration pattern.
- **Progress bar**: Determinate (0–100%) — bind `Value` to `DownloadProgressPercent` computed as `completedTasks / totalTasks * 100`.
- **No "last prefetched" timestamp**: Cache entries already carry a TTL/timestamp via `CacheService`, so cache date = last prefetched date by design.
