namespace Intune.Commander.Core.Services;

/// <summary>
/// Resolves directory object GUIDs (users, groups, roles, service principals, applications)
/// to their display names via the Graph API.
/// </summary>
public interface IDirectoryObjectResolver
{
    /// <summary>
    /// Resolves a batch of directory object IDs to their display names.
    /// Uses POST /directoryObjects/getByIds to resolve up to 1000 IDs per call.
    /// Unresolvable IDs are returned with the original GUID as the value.
    /// </summary>
    /// <param name="ids">Object IDs to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary mapping each ID to its resolved display name.</returns>
    Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default);
}
