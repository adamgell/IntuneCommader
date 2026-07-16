using System.Text;
using System.Text.Json;
using Azure.Core;
using Intune.Commander.Core.Models;

namespace Intune.Commander.Core.Services;

/// <summary>
/// Acquires a Graph access token via the supplied <see cref="TokenCredential"/>,
/// decodes the JWT payload without signature verification (the token was just
/// issued by Azure AD — we trust its contents), and compares the granted
/// permission claims against the full set required by Intune Commander.
///
/// Supported token types:
///   - Application (client-credentials): permissions in the "roles" claim (string array).
///   - Delegated (interactive/device-code): permissions in the "scp" claim (space-separated).
/// </summary>
public sealed class PermissionCheckService : IPermissionCheckService
{
    private readonly TokenCredential _credential;
    private readonly string[] _scopes;

    /// <summary>
    /// All permissions required by Intune Commander across all features.
    /// Kept in sync with <c>docs/GRAPH-PERMISSIONS.md</c>.
    /// </summary>
    public static readonly IReadOnlyList<string> RequiredPermissions =
    [
        // Intune — Device Management
        "DeviceManagementConfiguration.ReadWrite.All",
        "DeviceManagementApps.ReadWrite.All",
        "DeviceManagementServiceConfig.ReadWrite.All",
        "DeviceManagementRBAC.ReadWrite.All",
        "DeviceManagementManagedDevices.Read.All",
        "DeviceManagementScripts.ReadWrite.All",

        // Windows 365 — Cloud PC
        "CloudPC.ReadWrite.All",

        // Entra ID — Conditional Access & Identity
        "Policy.ReadWrite.ConditionalAccess",
        "Policy.Read.All",

        // Entra ID — Terms of Use
        "Agreement.ReadWrite.All",

        // Entra ID — Organization & Branding
        "Organization.Read.All",
        "OrganizationalBranding.ReadWrite.All",

        // Entra ID — Groups
        "Group.Read.All",
        "GroupMember.Read.All",
    ];

    public PermissionCheckService(TokenCredential credential, string[] scopes)
    {
        _credential = credential;
        _scopes = scopes;
    }

    public async Task<PermissionCheckResult> CheckPermissionsAsync(CancellationToken cancellationToken = default)
    {
        var tokenRequestContext = new TokenRequestContext(_scopes);
        var accessToken = await _credential.GetTokenAsync(tokenRequestContext, cancellationToken);

        var (granted, claimSource) = ExtractPermissionsFromJwt(accessToken.Token);

        var required = RequiredPermissions;
        var grantedSet = new HashSet<string>(granted, StringComparer.OrdinalIgnoreCase);
        var requiredSet = new HashSet<string>(required, StringComparer.OrdinalIgnoreCase);

        return new PermissionCheckResult
        {
            RequiredPermissions = [.. required],
            GrantedPermissions  = [.. required.Where(p => grantedSet.Contains(p)).OrderBy(p => p)],
            MissingPermissions  = [.. required.Where(p => !grantedSet.Contains(p)).OrderBy(p => p)],
            ExtraPermissions    = [.. granted.Where(p => !requiredSet.Contains(p)).OrderBy(p => p)],
            ClaimSource         = claimSource,
        };
    }

    /// <summary>
    /// Decodes the JWT payload and returns (permissions, claimSource).
    /// Handles both "roles" (application) and "scp" (delegated) token types.
    /// Does NOT verify the signature — the caller must trust the token source.
    /// </summary>
    internal static (List<string> Permissions, string ClaimSource) ExtractPermissionsFromJwt(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2)
            return ([], "unknown");

        // Base64url decode (no padding, '-' → '+', '_' → '/')
        var payload = parts[1];
        payload = payload.Replace('-', '+').Replace('_', '/');
        switch (payload.Length % 4)
        {
            case 2: payload += "=="; break;
            case 3: payload += "=";  break;
        }

        JsonDocument doc;
        try
        {
            var bytes = Convert.FromBase64String(payload);
            doc = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));
        }
        catch
        {
            return ([], "parse-error");
        }

        using (doc)
        {
            var root = doc.RootElement;

            // Application token: "roles" is a JSON array of strings
            if (root.TryGetProperty("roles", out var rolesEl) &&
                rolesEl.ValueKind == JsonValueKind.Array)
            {
                var perms = rolesEl.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .ToList();
                return (perms, "roles");
            }

            // Delegated token: "scp" is a space-separated string
            if (root.TryGetProperty("scp", out var scpEl) &&
                scpEl.ValueKind == JsonValueKind.String)
            {
                var perms = (scpEl.GetString() ?? string.Empty)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .ToList();
                return (perms, "scp");
            }

            return ([], "no-permission-claim");
        }
    }
}
