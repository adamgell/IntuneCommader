using System.Text;
using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services.CaPptExport;

/// <summary>
/// Parses condition sections from a CA policy: platforms, client app types, locations, risks, device filters.
/// </summary>
public class ConditionPlatforms
{
    public string? IncludeExclude { get; private set; }
    public bool HasData => !string.IsNullOrEmpty(IncludeExclude);

    public ConditionPlatforms(ConditionalAccessPolicy policy)
    {
        if (policy.Conditions?.Platforms == null) return;
        IncludeExclude = GetIncludes(policy.Conditions.Platforms);
    }

    private static string GetIncludes(ConditionalAccessPlatforms platforms)
    {
        var sb = new StringBuilder();
        if (platforms.IncludePlatforms?.Count > 0)
        {
            sb.AppendLine("✅ Include");
            AppendPlatforms(sb, platforms.IncludePlatforms);
            sb.AppendLine();
        }
        if (platforms.ExcludePlatforms?.Count > 0)
        {
            sb.AppendLine("🚫 Exclude");
            AppendPlatforms(sb, platforms.ExcludePlatforms);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static void AppendPlatforms(StringBuilder sb, List<ConditionalAccessDevicePlatform?> platforms)
    {
        foreach (var platform in platforms)
        {
            var text = platform switch
            {
                ConditionalAccessDevicePlatform.All => " - All",
                ConditionalAccessDevicePlatform.Android => " - 📱Android",
                ConditionalAccessDevicePlatform.IOS => " - 📱 iOS",
                ConditionalAccessDevicePlatform.Linux => " - 💻 Linux",
                ConditionalAccessDevicePlatform.MacOS => " - 💻 macOS",
                ConditionalAccessDevicePlatform.Windows => " - 💻 Windows",
                ConditionalAccessDevicePlatform.WindowsPhone => " - ☎️ Windows Phone",
                _ => $" - {platform}"
            };
            sb.AppendLine(text);
        }
    }
}

public class ConditionClientAppTypes
{
    public string? IncludeExclude { get; private set; }
    public bool HasData => !string.IsNullOrEmpty(IncludeExclude);

    public ConditionClientAppTypes(ConditionalAccessPolicy policy)
    {
        if (policy.Conditions?.ClientAppTypes == null) return;
        IncludeExclude = GetIncludes(policy.Conditions.ClientAppTypes);
    }

    private static string GetIncludes(List<ConditionalAccessClientApp?> clientAppTypes)
    {
        var sb = new StringBuilder();
        foreach (var clientAppType in clientAppTypes)
        {
            if (clientAppType == ConditionalAccessClientApp.All) return string.Empty; // "All" means not configured
            if (clientAppType == ConditionalAccessClientApp.Browser) sb.AppendLine(" - Browser");
            if (clientAppType == ConditionalAccessClientApp.MobileAppsAndDesktopClients)
                sb.AppendLine(" - Mobile app and desktop clients");
            if (clientAppType is ConditionalAccessClientApp.ExchangeActiveSync or ConditionalAccessClientApp.EasSupported)
                sb.AppendLine(" - Exchange ActiveSync clients");
            if (clientAppType == ConditionalAccessClientApp.Other) sb.AppendLine(" - Other legacy clients");
        }
        return sb.ToString();
    }
}

public class ConditionLocations
{
    public string? IncludeExclude { get; private set; }
    public bool HasData => !string.IsNullOrEmpty(IncludeExclude);

    /// <summary>
    /// Creates a new <see cref="ConditionLocations"/> instance.
    /// </summary>
    /// <param name="policy">The Conditional Access policy to parse.</param>
    /// <param name="nameLookup">
    /// Optional dictionary mapping named location GUIDs to display names.
    /// </param>
    public ConditionLocations(
        ConditionalAccessPolicy policy,
        IReadOnlyDictionary<string, string>? nameLookup = null)
    {
        if (policy.Conditions?.Locations == null) return;
        IncludeExclude = GetIncludes(policy.Conditions.Locations, nameLookup ?? new Dictionary<string, string>());
    }

    private static string GetIncludes(
        ConditionalAccessLocations locations,
        IReadOnlyDictionary<string, string> nameLookup)
    {
        var sb = new StringBuilder();
        AppendLocations(sb, locations.IncludeLocations, "✅ Include", nameLookup);
        AppendLocations(sb, locations.ExcludeLocations, "🚫 Exclude", nameLookup);
        return sb.ToString();
    }

    private static void AppendLocations(
        StringBuilder sb,
        List<string>? locations,
        string title,
        IReadOnlyDictionary<string, string> nameLookup)
    {
        if (locations?.Count > 0)
        {
            sb.AppendLine(title);
            foreach (var loc in locations)
            {
                var name = loc switch
                {
                    "All" => "Any location",
                    "AllTrusted" => "All trusted locations",
                    "00000000-0000-0000-0000-000000000000" => "MFA Trusted IPs",
                    _ => nameLookup.TryGetValue(loc, out var resolved) ? resolved : loc
                };
                sb.AppendLine($" - {name}");
            }
            sb.AppendLine();
        }
    }
}

public class ConditionRisks
{
    public string? IncludeExclude { get; private set; }
    public bool HasData => !string.IsNullOrEmpty(IncludeExclude);

    public ConditionRisks(ConditionalAccessPolicy policy)
    {
        if (policy.Conditions == null) return;
        IncludeExclude = GetIncludes(policy.Conditions);
    }

    private static string GetIncludes(ConditionalAccessConditionSet conditions)
    {
        var sb = new StringBuilder();
        AppendRisk(sb, conditions.UserRiskLevels, "User risk:");
        AppendRisk(sb, conditions.SignInRiskLevels, "Sign-in risk:");
        AppendRisk(sb, conditions.ServicePrincipalRiskLevels, "Service principal risk:");
        return sb.ToString();
    }

    private static void AppendRisk(StringBuilder sb, List<RiskLevel?>? riskLevels, string title)
    {
        if (riskLevels?.Count > 0)
        {
            sb.AppendLine(title);
            foreach (var risk in riskLevels)
            {
                var text = risk switch
                {
                    RiskLevel.Hidden => " - Hidden",
                    RiskLevel.High => " - High",
                    RiskLevel.Low => " - Low",
                    RiskLevel.Medium => " - Medium",
                    RiskLevel.None => " - No risk",
                    _ => $" - {risk}"
                };
                sb.AppendLine(text);
            }
            sb.AppendLine();
        }
    }
}

public class ConditionDeviceFilters
{
    public string? IncludeExclude { get; private set; }
    public bool HasData => !string.IsNullOrEmpty(IncludeExclude);

    public ConditionDeviceFilters(ConditionalAccessPolicy policy)
    {
        if (policy.Conditions?.Devices?.DeviceFilter == null) return;
        IncludeExclude = GetIncludes(policy.Conditions.Devices.DeviceFilter);
    }

    private static string GetIncludes(ConditionalAccessFilter filter)
    {
        var sb = new StringBuilder();
        var mode = filter.Mode == FilterMode.Include ? "Include when" : "Exclude when";
        sb.AppendLine(mode);
        sb.AppendLine(filter.Rule);
        return sb.ToString();
    }
}
