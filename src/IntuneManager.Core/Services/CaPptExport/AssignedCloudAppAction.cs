using System.Text;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Services.CaPptExport;

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
/// </summary>
public class AssignedCloudAppAction
{
    public string? Name { get; private set; }
    public string? IncludeExclude { get; private set; }
    public AppAccessType AccessType { get; private set; }
    public bool IsSelectedAppO365Only { get; private set; }
    public bool IsSelectedMicrosoftAdminPortalsOnly { get; private set; }
    public bool HasData => !string.IsNullOrEmpty(IncludeExclude);

    public AssignedCloudAppAction(ConditionalAccessPolicy policy)
    {
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
                    sb.AppendLine($" - {val}");
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
                    sb.AppendLine($" - {val}");
            }
        }

        return sb.ToString();
    }

    private static string GetAuthContext(ConditionalAccessConditionSet conditions)
    {
        var sb = new StringBuilder();
        var refs = conditions.Applications?.IncludeAuthenticationContextClassReferences;
        if (refs?.Count > 0)
        {
            foreach (var val in refs)
                sb.AppendLine($" - {val}");
        }
        return sb.ToString();
    }
}
