namespace Intune.Commander.Core.Models;

/// <summary>
/// Result of comparing the permissions present in the current access token
/// against the set required by Intune Commander.
/// </summary>
public sealed class PermissionCheckResult
{
    /// <summary>All permissions required by Intune Commander.</summary>
    public IReadOnlyList<string> RequiredPermissions { get; init; } = [];

    /// <summary>Permissions that are both required AND present in the token.</summary>
    public IReadOnlyList<string> GrantedPermissions { get; init; } = [];

    /// <summary>Required permissions that are absent from the token.</summary>
    public IReadOnlyList<string> MissingPermissions { get; init; } = [];

    /// <summary>Permissions present in the token that are not in the required set.</summary>
    public IReadOnlyList<string> ExtraPermissions { get; init; } = [];

    /// <summary>Whether all required permissions are present in the token.</summary>
    public bool AllPermissionsGranted => MissingPermissions.Count == 0;

    /// <summary>
    /// How the permissions were sourced from the token.
    /// "roles" = application (client-credentials) token;
    /// "scp"   = delegated (interactive / device-code) token.
    /// </summary>
    public string ClaimSource { get; init; } = string.Empty;
}
