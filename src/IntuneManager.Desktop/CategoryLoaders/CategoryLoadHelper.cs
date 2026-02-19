using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IntuneManager.Desktop.CategoryLoaders;

/// <summary>
/// Executes any <see cref="ICategoryLoader{T}"/> with standard boilerplate:
/// busy state, status messages, cache write, error reporting, and filter refresh.
///
/// This is a pure network-fetch wrapper. Cache reads must be done separately
/// via <c>TryLoadLazyCacheEntry</c> in the <c>SelectedNavCategory</c> setter,
/// maintaining consistency with all existing 30+ categories.
///
/// Usage in MainWindowViewModel:
/// <code>
/// // In SelectedNavCategory setter:
/// if (!TryLoadLazyCacheEntry&lt;T&gt;(cacheKey, rows =&gt; { ... }))
/// {
///     _ = LoadXAsync();
/// }
///
/// // In LoadXAsync:
/// var items = await CategoryLoadHelper.ExecuteAsync(loader, _loadCtx, cancellationToken);
/// if (items != null)
/// {
///     MyCollection = new ObservableCollection&lt;T&gt;(items);
///     _myLoaded = true;
/// }
/// </code>
/// </summary>
public static class CategoryLoadHelper
{
    /// <summary>
    /// Runs the loader, applying cache write and VM state management.
    /// Does NOT check cache — caller must use <c>TryLoadLazyCacheEntry</c> first.
    /// Returns <c>null</c> if the load fails; callers must not set the
    /// <c>_*Loaded</c> flag in that case.
    /// </summary>
    public static async Task<IReadOnlyList<T>?> ExecuteAsync<T>(
        ICategoryLoader<T> loader,
        CategoryLoadContext ctx,
        CancellationToken cancellationToken = default)
    {
        ctx.SetBusy(true);
        ctx.SetStatus($"Loading {loader.CategoryName}...");

        try
        {
            // --- Network fetch ---
            var items = await loader.FetchAsync(cancellationToken);

            // --- Cache write ---
            if (ctx.TenantId != null)
                ctx.CacheService.Set(ctx.TenantId, loader.CacheKey, [.. items]);

            ctx.ApplyFilter();
            ctx.SetStatus($"Loaded {items.Count} {loader.CategoryName.ToLowerInvariant()}(s)");
            return items;
        }
        catch (Exception ex)
        {
            ctx.SetError($"Failed to load {loader.CategoryName}: {ctx.FormatError(ex)}");
            ctx.SetStatus($"Error loading {loader.CategoryName}");
            return null;
        }
        finally
        {
            ctx.SetBusy(false);
        }
    }
}
