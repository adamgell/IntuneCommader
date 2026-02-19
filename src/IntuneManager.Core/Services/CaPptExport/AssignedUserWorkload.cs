using System.Text;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Services.CaPptExport;

/// <summary>
/// Parses assigned users/workload from a CA policy into include/exclude text.
/// </summary>
public class AssignedUserWorkload
{
    public string? Name { get; private set; }
    public string? IncludeExclude { get; private set; }
    public bool IsWorkload { get; private set; }
    public bool HasIncludeRoles { get; private set; }
    public bool HasIncludeExternalUser { get; private set; }
    public bool HasIncludeExternalTenant { get; private set; }
    public bool HasData => !string.IsNullOrEmpty(IncludeExclude);

    public AssignedUserWorkload(ConditionalAccessPolicy policy)
    {
        var conditions = policy.Conditions;
        if (conditions == null) return;

        IsWorkload = conditions.ClientApplications?.IncludeServicePrincipals?.Count > 0;

        if (IsWorkload)
        {
            Name = "Workload identity";
            IncludeExclude = GetWorkloadIncludeExclude(conditions);
        }
        else
        {
            Name = "Users";
            UpdateUserFlags(conditions);
            IncludeExclude = GetUserIncludeExclude(conditions);
        }
    }

    private void UpdateUserFlags(ConditionalAccessConditionSet conditions)
    {
        var users = conditions.Users;
        if (users == null) return;

        HasIncludeRoles = users.IncludeRoles?.Count > 0;
        HasIncludeExternalUser = users.IncludeGuestsOrExternalUsers != null;
        HasIncludeExternalTenant = users.IncludeGuestsOrExternalUsers?.ExternalTenants != null;
    }

    private static string GetWorkloadIncludeExclude(ConditionalAccessConditionSet conditions)
    {
        var apps = conditions.ClientApplications;
        if (apps == null) return string.Empty;

        var sb = new StringBuilder();
        if (apps.IncludeServicePrincipals?.Count > 0)
        {
            sb.AppendLine("✅ Include:");
            if (apps.IncludeServicePrincipals.Contains("ServicePrincipalsInMyTenant"))
                sb.AppendLine("  - All owned service principals");
            else
                foreach (var sp in apps.IncludeServicePrincipals)
                    sb.AppendLine($"  - {sp}");
        }
        if (apps.ExcludeServicePrincipals?.Count > 0)
        {
            sb.AppendLine("🚫 Exclude:");
            foreach (var sp in apps.ExcludeServicePrincipals)
                sb.AppendLine($"  - {sp}");
        }
        return sb.ToString();
    }

    private static string GetUserIncludeExclude(ConditionalAccessConditionSet conditions)
    {
        var users = conditions.Users;
        if (users == null) return string.Empty;

        var sb = new StringBuilder();
        var hasInclude = (users.IncludeUsers?.Count > 0) ||
                         (users.IncludeGroups?.Count > 0) ||
                         (users.IncludeRoles?.Count > 0) ||
                         users.IncludeGuestsOrExternalUsers != null;

        if (hasInclude)
        {
            sb.AppendLine("✅ Include:");
            if (users.IncludeGuestsOrExternalUsers != null)
                sb.AppendLine("  Guest or external users");
            if (users.IncludeRoles?.Count > 0)
            {
                sb.AppendLine("  Directory roles");
                foreach (var id in users.IncludeRoles)
                    sb.AppendLine($"    - {id}");
            }
            if (users.IncludeGroups?.Count > 0)
            {
                sb.AppendLine("  Groups");
                foreach (var id in users.IncludeGroups)
                    sb.AppendLine($"    - {id}");
            }
            if (users.IncludeUsers?.Count > 0)
            {
                sb.AppendLine("  Users");
                foreach (var id in users.IncludeUsers)
                    sb.AppendLine($"    - {FormatUserId(id)}");
            }
            sb.AppendLine();
        }

        var hasExclude = (users.ExcludeUsers?.Count > 0) ||
                         (users.ExcludeGroups?.Count > 0) ||
                         (users.ExcludeRoles?.Count > 0) ||
                         users.ExcludeGuestsOrExternalUsers != null;

        if (hasExclude)
        {
            sb.AppendLine("🚫 Exclude:");
            if (users.ExcludeGuestsOrExternalUsers != null)
                sb.AppendLine("  Guest or external users");
            if (users.ExcludeRoles?.Count > 0)
            {
                sb.AppendLine("  Directory roles");
                foreach (var id in users.ExcludeRoles)
                    sb.AppendLine($"    - {id}");
            }
            if (users.ExcludeGroups?.Count > 0)
            {
                sb.AppendLine("  Groups");
                foreach (var id in users.ExcludeGroups)
                    sb.AppendLine($"    - {id}");
            }
            if (users.ExcludeUsers?.Count > 0)
            {
                sb.AppendLine("  Users");
                foreach (var id in users.ExcludeUsers)
                    sb.AppendLine($"    - {FormatUserId(id)}");
            }
        }
        return sb.ToString();
    }

    private static string FormatUserId(string id) => id switch
    {
        "All" => "All users",
        "None" => "None",
        "GuestsOrExternalUsers" => "Guest or external users",
        _ => id
    };
}
