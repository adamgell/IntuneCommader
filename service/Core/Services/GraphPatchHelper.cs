namespace Intune.Commander.Core.Services;

/// <summary>
/// Shared helper for Graph API PATCH operations.
/// Some Graph endpoints return 204 No Content on PATCH, causing the SDK to
/// return null. This helper falls back to a GET request to retrieve the
/// updated resource.
/// </summary>
internal static class GraphPatchHelper
{
    /// <summary>
    /// Returns the PATCH result if non-null, otherwise falls back to GET.
    /// Throws <see cref="InvalidOperationException"/> if both return null.
    /// </summary>
    internal static async Task<T> PatchWithGetFallbackAsync<T>(
        T? patchResult,
        Func<Task<T?>> getFallback,
        string entityName,
        CancellationToken cancellationToken = default) where T : class
    {
        if (patchResult is not null)
            return patchResult;

        var fallback = await getFallback();
        return fallback ?? throw new InvalidOperationException($"Failed to update {entityName}");
    }
}
