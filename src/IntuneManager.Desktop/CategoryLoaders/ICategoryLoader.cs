using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IntuneManager.Desktop.CategoryLoaders;

/// <summary>
/// Contract for a per-category data loader.
///
/// Every Intune object-type category must implement this interface so that
/// loading, status reporting, error handling, and cache write are handled
/// uniformly by <see cref="CategoryLoadHelper.ExecuteAsync{T}"/>.
///
/// Cache reads are handled separately by <c>TryLoadLazyCacheEntry</c> in the
/// <c>SelectedNavCategory</c> setter before calling <c>ExecuteAsync</c>.
///
/// Implement the interface for a new category, pass the instance to
/// <see cref="CategoryLoadHelper.ExecuteAsync{T}"/>, and use the returned
/// list to populate the corresponding <c>ObservableCollection</c>.
/// </summary>
/// <typeparam name="T">The Microsoft Graph model type for this category.</typeparam>
public interface ICategoryLoader<T>
{
    /// <summary>
    /// The navigation-category name this loader is responsible for.
    /// Must match the <c>NavCategory.Name</c> string exactly.
    /// </summary>
    string CategoryName { get; }

    /// <summary>
    /// The cache key used to store and retrieve items via
    /// <see cref="IntuneManager.Core.Services.ICacheService"/>.
    /// Use a stable, unique string (e.g. <c>"ConditionalAccessPolicies"</c>).
    /// </summary>
    string CacheKey { get; }

    /// <summary>
    /// Fetches items from the Graph API.
    /// Implementations must <b>not</b> manipulate busy/status state or the
    /// cache — that is the responsibility of the helper.
    /// </summary>
    Task<IReadOnlyList<T>> FetchAsync(CancellationToken cancellationToken = default);
}
