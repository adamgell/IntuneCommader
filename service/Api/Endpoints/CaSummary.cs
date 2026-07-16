using Microsoft.Graph.Beta.Models;

namespace CmProjectX.Api;

// Readable summaries of a Conditional Access policy's deeply-nested conditions and
// controls — ported from IntuneCommander's ConditionalAccessBridgeService. The list
// shows counts (fast, no Graph calls); the detail summary resolves user/group/app
// GUIDs to display names via a name map the caller batches (DirectoryObjectResolver).
internal static class CaSummary
{
    // All directory-object GUIDs a policy references (users/groups/apps), for one
    // batched resolution. Sentinels ("All", "GuestsOrExternalUsers") are filtered by
    // the resolver, so we pass everything through.
    public static IEnumerable<string> DirectoryIds(ConditionalAccessPolicy p)
    {
        var c = p.Conditions;
        if (c is null) yield break;
        foreach (var s in Flatten(
            c.Users?.IncludeUsers, c.Users?.ExcludeUsers,
            c.Users?.IncludeGroups, c.Users?.ExcludeGroups,
            c.Applications?.IncludeApplications, c.Applications?.ExcludeApplications))
        {
            if (!string.IsNullOrWhiteSpace(s)) yield return s;
        }
    }

    // One-line list subtitle: "All users · 3 app(s) · grant: MFA, compliant device".
    public static string ListSubtitle(ConditionalAccessPolicy p)
    {
        var parts = new List<string>
        {
            SummarizeUsers(p.Conditions?.Users, NoNames),
            SummarizeApplications(p.Conditions?.Applications, NoNames),
        };
        var grant = string.Join(", ", GrantControls(p.GrantControls));
        if (grant.Length > 0) parts.Add("grant: " + grant);
        return string.Join(" · ", parts.Where(x => x.Length > 0));
    }

    // Structured, GUID-resolved summary for the detail card.
    public static CaSummaryDto Detail(ConditionalAccessPolicy p, IReadOnlyDictionary<string, string> names)
    {
        var c = p.Conditions;
        var cond = new CaCondDto(
            Users: SummarizeUsers(c?.Users, names),
            Applications: SummarizeApplications(c?.Applications, names),
            Platforms: SummarizePlatforms(c?.Platforms),
            Locations: SummarizeLocations(c?.Locations),
            ClientApps: Join(c?.ClientAppTypes?.Select(x => x?.ToString())) is { Length: > 0 } ca ? ca : "All",
            SignInRisk: Join(c?.SignInRiskLevels?.Select(x => x?.ToString())) is { Length: > 0 } sr ? sr : "None",
            UserRisk: Join(c?.UserRiskLevels?.Select(x => x?.ToString())) is { Length: > 0 } ur ? ur : "None");

        return new CaSummaryDto(
            State: p.State?.ToString() ?? "disabled",
            Conditions: cond,
            GrantOperator: p.GrantControls?.Operator ?? "",
            GrantControls: GrantControls(p.GrantControls).ToList(),
            SessionControls: SessionControls(p.SessionControls).ToList());
    }

    // Rich list row for the dedicated CA grid — summary strings (counts), no
    // resolution (fast: one Graph call for the whole list, no per-policy lookups).
    public static CaPolicyListItemDto ListItem(ConditionalAccessPolicy p) => new(
        p.Id ?? "", p.DisplayName ?? "(unnamed policy)", p.Description, p.State?.ToString() ?? "disabled",
        SummarizeUsers(p.Conditions?.Users, NoNames),
        SummarizeApplications(p.Conditions?.Applications, NoNames),
        SummarizePlatforms(p.Conditions?.Platforms),
        GrantControls(p.GrantControls).ToList(),
        p.CreatedDateTime?.ToString("o") ?? "", p.ModifiedDateTime?.ToString("o") ?? "");

    // Full detail with include/exclude arrays resolved to display names.
    public static CaDetailDto DetailFull(ConditionalAccessPolicy p, IReadOnlyDictionary<string, string> names)
    {
        var c = p.Conditions;
        var cond = new CaCondDetailDto(
            ResolveUsers(c?.Users?.IncludeUsers, names), ResolveUsers(c?.Users?.ExcludeUsers, names),
            ResolveList(c?.Users?.IncludeGroups, names), ResolveList(c?.Users?.ExcludeGroups, names),
            ResolveApps(c?.Applications?.IncludeApplications, names), ResolveApps(c?.Applications?.ExcludeApplications, names),
            EnumList(c?.Platforms?.IncludePlatforms), EnumList(c?.Platforms?.ExcludePlatforms),
            ResolveList(c?.Locations?.IncludeLocations, names), ResolveList(c?.Locations?.ExcludeLocations, names),
            EnumList(c?.ClientAppTypes), EnumList(c?.SignInRiskLevels), EnumList(c?.UserRiskLevels));
        var grant = new CaGrantDto(
            p.GrantControls?.Operator ?? "",
            GrantControls(p.GrantControls).ToList(),
            p.GrantControls?.AuthenticationStrength?.DisplayName);
        var sf = p.SessionControls?.SignInFrequency;
        var pb = p.SessionControls?.PersistentBrowser;
        var sess = new CaSessionDto(
            sf?.IsEnabled == true ? $"{sf.Value} {sf.Type}".Trim() : null,
            pb?.IsEnabled == true ? pb.Mode?.ToString() : null,
            p.SessionControls?.ApplicationEnforcedRestrictions?.IsEnabled == true,
            p.SessionControls?.CloudAppSecurity?.IsEnabled == true);
        return new CaDetailDto(
            p.Id ?? "", p.DisplayName ?? "", p.Description, p.State?.ToString() ?? "disabled",
            p.CreatedDateTime?.ToString("o") ?? "", p.ModifiedDateTime?.ToString("o") ?? "",
            cond, grant, sess);
    }

    private static List<string> ResolveUsers(List<string>? ids, IReadOnlyDictionary<string, string> names) =>
        (ids ?? new()).Select(id => id switch
        {
            "All" => "All users",
            "GuestsOrExternalUsers" => "Guests/External",
            "None" => "None",
            _ => names.TryGetValue(id, out var n) ? n : id,
        }).ToList();

    private static List<string> ResolveApps(List<string>? ids, IReadOnlyDictionary<string, string> names) =>
        (ids ?? new()).Select(id => id switch
        {
            "All" => "All apps",
            "Office365" => "Office 365",
            "None" => "None",
            _ => names.TryGetValue(id, out var n) ? n : id,
        }).ToList();

    private static List<string> ResolveList(List<string>? ids, IReadOnlyDictionary<string, string> names) =>
        (ids ?? new()).Select(id => names.TryGetValue(id, out var n) ? n : id).ToList();

    private static List<string> EnumList<T>(List<T?>? items) where T : struct =>
        (items ?? new()).Where(x => x.HasValue).Select(x => x!.Value.ToString() ?? "").ToList();

    // ── summarizers ───────────────────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<string, string> NoNames =
        new Dictionary<string, string>();

    private static string SummarizeUsers(ConditionalAccessUsers? u, IReadOnlyDictionary<string, string> names)
    {
        if (u is null) return "no users";
        var inc = u.IncludeUsers ?? new();
        if (inc.Contains("All")) return "All users";
        if (inc.Contains("GuestsOrExternalUsers")) return "Guests/External";
        var groups = u.IncludeGroups ?? new();
        var ids = inc.Concat(groups).Where(id => !string.IsNullOrWhiteSpace(id) && id != "None").ToList();
        if (ids.Count == 0) return "no users";
        var named = ids.Select(id => names.TryGetValue(id, out var n) ? n : null)
            .Where(n => n is not null).Take(3).ToList();
        if (named.Count > 0)
            return named.Count < ids.Count ? $"{string.Join(", ", named)} +{ids.Count - named.Count}" : string.Join(", ", named);
        return $"{ids.Count} user(s)/group(s)";
    }

    private static string SummarizeApplications(ConditionalAccessApplications? a, IReadOnlyDictionary<string, string> names)
    {
        if (a is null) return "no apps";
        var inc = a.IncludeApplications ?? new();
        if (inc.Contains("All")) return "All apps";
        if (inc.Contains("Office365")) return "Office 365";
        var ids = inc.Where(id => !string.IsNullOrWhiteSpace(id) && id != "None").ToList();
        if (ids.Count == 0) return "no apps";
        var named = ids.Select(id => names.TryGetValue(id, out var n) ? n : null)
            .Where(n => n is not null).Take(2).ToList();
        if (named.Count > 0)
            return named.Count < ids.Count ? $"{string.Join(", ", named)} +{ids.Count - named.Count}" : string.Join(", ", named);
        return $"{ids.Count} app(s)";
    }

    private static string SummarizePlatforms(ConditionalAccessPlatforms? p)
    {
        var inc = p?.IncludePlatforms;
        if (inc is null || inc.Count == 0) return "Any";
        if (inc.Any(x => x?.ToString() == "All")) return "All platforms";
        return Join(inc.Select(x => x?.ToString()));
    }

    private static string SummarizeLocations(ConditionalAccessLocations? l)
    {
        var inc = l?.IncludeLocations;
        if (inc is null || inc.Count == 0) return "Any";
        if (inc.Contains("All")) return "All locations";
        return $"{inc.Count} location(s)";
    }

    private static IEnumerable<string> GrantControls(ConditionalAccessGrantControls? g)
    {
        if (g is null) yield break;
        foreach (var c in g.BuiltInControls ?? new())
        {
            var pretty = Pretty(c?.ToString());
            if (pretty.Length > 0) yield return pretty;
        }
        if (g.AuthenticationStrength is not null) yield return "auth strength";
    }

    private static IEnumerable<string> SessionControls(ConditionalAccessSessionControls? s)
    {
        if (s is null) yield break;
        if (s.ApplicationEnforcedRestrictions?.IsEnabled == true) yield return "App-enforced restrictions";
        if (s.CloudAppSecurity?.IsEnabled == true) yield return "Cloud app security";
        if (s.SignInFrequency?.IsEnabled == true) yield return "Sign-in frequency";
        if (s.PersistentBrowser?.IsEnabled == true) yield return "Persistent browser";
    }

    private static string Pretty(string? c) => c switch
    {
        "Mfa" => "MFA",
        "CompliantDevice" => "compliant device",
        "DomainJoinedDevice" => "domain-joined",
        "ApprovedApplication" => "approved app",
        "CompliantApplication" => "app protection",
        "PasswordChange" => "password change",
        "Block" => "block",
        null => "",
        _ => c,
    };

    private static string Join(IEnumerable<string?>? items) =>
        items is null ? "" : string.Join(", ", items.Where(x => !string.IsNullOrWhiteSpace(x)));

    private static IEnumerable<string> Flatten(params List<string>?[] lists) =>
        lists.Where(l => l is not null).SelectMany(l => l!);
}
