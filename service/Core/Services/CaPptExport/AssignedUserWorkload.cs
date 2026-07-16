using System.Text;
using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services.CaPptExport;

/// <summary>
/// Parses assigned users/workload from a CA policy into include/exclude text.
/// Accepts an optional name lookup dictionary to resolve GUIDs to display names.
/// </summary>
public class AssignedUserWorkload
{
    private readonly IReadOnlyDictionary<string, string> _nameLookup;

    public string? Name { get; private set; }
    public string? IncludeExclude { get; private set; }
    public bool IsWorkload { get; private set; }
    public bool HasIncludeRoles { get; private set; }
    public bool HasIncludeExternalUser { get; private set; }
    public bool HasIncludeExternalTenant { get; private set; }
    public bool HasData => !string.IsNullOrEmpty(IncludeExclude);

    /// <summary>
    /// Creates a new <see cref="AssignedUserWorkload"/> instance.
    /// </summary>
    /// <param name="policy">The Conditional Access policy to parse.</param>
    /// <param name="nameLookup">
    /// Optional dictionary mapping directory object GUIDs to display names.
    /// When provided, user, group, role, and service principal GUIDs are resolved to readable names.
    /// </param>
    public AssignedUserWorkload(
        ConditionalAccessPolicy policy,
        IReadOnlyDictionary<string, string>? nameLookup = null)
    {
        _nameLookup = nameLookup ?? new Dictionary<string, string>();

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

    private string GetWorkloadIncludeExclude(ConditionalAccessConditionSet conditions)
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
                    sb.AppendLine($"  - {ResolveName(sp)}");
        }
        if (apps.ExcludeServicePrincipals?.Count > 0)
        {
            sb.AppendLine("🚫 Exclude:");
            foreach (var sp in apps.ExcludeServicePrincipals)
                sb.AppendLine($"  - {ResolveName(sp)}");
        }
        return sb.ToString();
    }

    private string GetUserIncludeExclude(ConditionalAccessConditionSet conditions)
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
                    sb.AppendLine($"    - {ResolveName(id)}");
            }
            if (users.IncludeGroups?.Count > 0)
            {
                sb.AppendLine("  Groups");
                foreach (var id in users.IncludeGroups)
                    sb.AppendLine($"    - {ResolveName(id)}");
            }
            if (users.IncludeUsers?.Count > 0)
            {
                sb.AppendLine("  Users");
                foreach (var id in users.IncludeUsers)
                    sb.AppendLine($"    - {FormatUserId(ResolveName(id))}");
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
                    sb.AppendLine($"    - {ResolveName(id)}");
            }
            if (users.ExcludeGroups?.Count > 0)
            {
                sb.AppendLine("  Groups");
                foreach (var id in users.ExcludeGroups)
                    sb.AppendLine($"    - {ResolveName(id)}");
            }
            if (users.ExcludeUsers?.Count > 0)
            {
                sb.AppendLine("  Users");
                foreach (var id in users.ExcludeUsers)
                    sb.AppendLine($"    - {FormatUserId(ResolveName(id))}");
            }
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

    private static string FormatUserId(string id) => id switch
    {
        "All" => "All users",
        "None" => "None",
        "GuestsOrExternalUsers" => "Guest or external users",
        _ => id
    };
}
