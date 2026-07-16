using System.Text;
using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services.CaPptExport;

public enum AppAccessType
{
    AppsNone,
    AppsAll,
    AppsSelected,
    UserActionsRegSecInfo,
    UserActionsRegDevice,
    AuthenticationContext,
    Unknown
}

/// <summary>
/// Parses cloud app/action assignments from a CA policy.
/// Accepts an optional name lookup dictionary to resolve application GUIDs to display names.
/// </summary>
public class AssignedCloudAppAction
{
    private readonly IReadOnlyDictionary<string, string> _nameLookup;

    public string? Name { get; private set; }
    public string? IncludeExclude { get; private set; }
    public AppAccessType AccessType { get; private set; }
    public bool IsSelectedAppO365Only { get; private set; }
    public bool IsSelectedMicrosoftAdminPortalsOnly { get; private set; }
    public bool HasData => !string.IsNullOrEmpty(IncludeExclude);

    /// <summary>
    /// Creates a new <see cref="AssignedCloudAppAction"/> instance.
    /// </summary>
    /// <param name="policy">The Conditional Access policy to parse.</param>
    /// <param name="nameLookup">
    /// Optional dictionary mapping application/service principal GUIDs to display names.
    /// When provided, application GUIDs are resolved to readable names.
    /// </param>
    public AssignedCloudAppAction(
        ConditionalAccessPolicy policy,
        IReadOnlyDictionary<string, string>? nameLookup = null)
    {
        _nameLookup = nameLookup ?? new Dictionary<string, string>();
        var conditions = policy.Conditions;
        if (conditions?.Applications == null) return;

        AccessType = GetAccessType(conditions);
        IsSelectedAppO365Only = false;
        IsSelectedMicrosoftAdminPortalsOnly = false;

        switch (AccessType)
        {
            case AppAccessType.AppsNone:
                Name = "Microsoft Entra";
                IncludeExclude = string.Empty;
                break;
            case AppAccessType.AppsAll:
                Name = "All cloud apps";
                IncludeExclude = GetCloudAppIncludeExclude(conditions);
                break;
            case AppAccessType.AppsSelected:
                Name = "Selected cloud apps";
                IncludeExclude = GetCloudAppIncludeExclude(conditions);
                break;
            case AppAccessType.UserActionsRegDevice:
                Name = "Register or join devices";
                break;
            case AppAccessType.UserActionsRegSecInfo:
                Name = "Register security information";
                break;
            case AppAccessType.AuthenticationContext:
                Name = "Authentication context";
                IncludeExclude = GetAuthContext(conditions);
                break;
            default:
                Name = "Unknown";
                IncludeExclude = "Unknown";
                break;
        }
    }

    private static AppAccessType GetAccessType(ConditionalAccessConditionSet conditions)
    {
        var apps = conditions.Applications;
        if (apps == null) return AppAccessType.Unknown;

        if (apps.IncludeApplications?.Count > 0)
        {
            if (apps.IncludeApplications.Contains("None")) return AppAccessType.AppsNone;
            if (apps.IncludeApplications.Contains("All")) return AppAccessType.AppsAll;
            return AppAccessType.AppsSelected;
        }
        if (apps.IncludeUserActions?.Count > 0)
        {
            if (apps.IncludeUserActions.Contains("urn:user:registersecurityinfo"))
                return AppAccessType.UserActionsRegSecInfo;
            if (apps.IncludeUserActions.Contains("urn:user:registerdevice"))
                return AppAccessType.UserActionsRegDevice;
        }
        if (apps.IncludeAuthenticationContextClassReferences?.Count > 0)
            return AppAccessType.AuthenticationContext;

        return AppAccessType.Unknown;
    }

    private string GetCloudAppIncludeExclude(ConditionalAccessConditionSet conditions)
    {
        var apps = conditions.Applications!;
        var sb = new StringBuilder();

        if (apps.IncludeApplications?.Count > 0)
        {
            var appCount = apps.IncludeApplications.Count;
            sb.AppendLine("✅ Include:");
            foreach (var val in apps.IncludeApplications)
            {
                if (val == "Office365")
                {
                    if (appCount == 1)
                    {
                        IsSelectedAppO365Only = true;
                        Name = "Office 365";
                    }
                    else
                        sb.AppendLine(" - Office 365");
                }
                else if (val == "MicrosoftAdminPortals")
                {
                    if (appCount == 1)
                    {
                        IsSelectedMicrosoftAdminPortalsOnly = true;
                        Name = "Microsoft Admin Portals";
                    }
                    else
                        sb.AppendLine(" - Microsoft Admin Portals");
                }
                else if (val == "All")
                    sb.AppendLine(" - All cloud apps");
                else if (val == "None")
                    sb.AppendLine(" - None");
                else
                    sb.AppendLine($" - {ResolveName(val)}");
            }
        }

        if (apps.ExcludeApplications?.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("🚫 Exclude:");
            foreach (var val in apps.ExcludeApplications)
            {
                if (val == "Office365")
                    sb.AppendLine(" - Office 365");
                else if (val == "MicrosoftAdminPortals")
                    sb.AppendLine(" - Microsoft Admin Portals");
                else
                    sb.AppendLine($" - {ResolveName(val)}");
            }
        }

        return sb.ToString();
    }

    private string GetAuthContext(ConditionalAccessConditionSet conditions)
    {
        var sb = new StringBuilder();
        var refs = conditions.Applications?.IncludeAuthenticationContextClassReferences;
        if (refs?.Count > 0)
        {
            foreach (var val in refs)
                sb.AppendLine($" - {ResolveName(val)}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Resolves an ID to a display name using the lookup dictionary.
    /// Falls back to the raw ID if not found.
    /// </summary>
    private string ResolveName(string id)
    {
        if (string.IsNullOrEmpty(id)) return id;
        return _nameLookup.TryGetValue(id, out var name) ? name : id;
    }
}
