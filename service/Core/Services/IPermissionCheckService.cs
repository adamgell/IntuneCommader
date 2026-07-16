using Intune.Commander.Core.Models;

namespace Intune.Commander.Core.Services;

/// <summary>
/// Checks the permissions present in the current access token against the set
/// required by Intune Commander and reports what is granted or missing.
/// </summary>
public interface IPermissionCheckService
{
    /// <summary>
    /// Acquires a fresh access token for the Graph API and decodes it to extract
    /// the granted permissions, then compares them against the required set.
    /// </summary>
    Task<PermissionCheckResult> CheckPermissionsAsync(CancellationToken cancellationToken = default);
}
