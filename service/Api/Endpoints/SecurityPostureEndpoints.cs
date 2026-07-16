using Microsoft.Graph.Beta.Models;
using Intune.Commander.Core.Services;
// Microsoft.Graph.Beta.Models also defines a `WebApplication` type; alias the host
// type so the extension method resolves (see DevicesEndpoints for the rationale).
using WebApplication = Microsoft.AspNetCore.Builder.WebApplication;

namespace CmProjectX.Api;

// Security posture score — a near-verbatim port of IntuneCommander's
// SecurityPostureBridgeService.ComputeSecurityScore, over the SAME forked Core
// services. Returns a 0-100 score across five weighted categories plus a
// severity-ranked gaps list and the headline stats, for the posture dashboard.
public static class SecurityPostureEndpoints
{
    public static void MapSecurityPosture(this WebApplication app)
    {
        // GET /security-posture/summary — the scored posture. 409 when signed out.
        app.MapGet("/security-posture/summary", async (AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var posture = await GatherAsync(g, ct);
            return Results.Ok(posture);
        });
    }

    // Shared fan-out + score (reused by /security-posture/summary AND the M19
    // /posture/* endpoints in PostureEndpoints.cs, so the benchmark/trend/POA&M
    // views never recompute or redefine the score — they decorate THIS result).
    internal static async Task<SecurityPostureDto> GatherAsync(
        Microsoft.Graph.Beta.GraphServiceClient g, CancellationToken ct)
    {
        // Fan out across the six surfaces in parallel.
        var caTask = new ConditionalAccessPolicyService(g).ListPoliciesAsync(ct);
        var compTask = new CompliancePolicyService(g).ListCompliancePoliciesAsync(ct);
        var endpTask = new EndpointSecurityService(g).ListEndpointSecurityIntentsAsync(ct);
        var appTask = new AppProtectionPolicyService(g).ListAppProtectionPoliciesAsync(ct);
        var authTask = new AuthenticationStrengthService(g).ListAuthenticationStrengthPoliciesAsync(ct);
        var locTask = new NamedLocationService(g).ListNamedLocationsAsync(ct);
        await Task.WhenAll(caTask, compTask, endpTask, appTask, authTask, locTask);

        var ca = await caTask;
        var comp = await compTask;
        var endp = await endpTask;
        var appp = await appTask;
        var auth2 = await authTask;
        var loc = await locTask;

        var (score, breakdown, gaps) = ComputeScore(ca, comp, endp, appp, auth2, loc);

        var caEnabled = ca.Count(p => p.State == ConditionalAccessPolicyState.Enabled);
        var caReport = ca.Count(p => p.State == ConditionalAccessPolicyState.EnabledForReportingButNotEnforced);
        var caDisabled = ca.Count(p => p.State == ConditionalAccessPolicyState.Disabled);
        var platforms = comp.Select(DetectPlatform).Where(p => p != "Unknown").Distinct().OrderBy(p => p).ToList();

        var stats = new PostureStatsDto(
            CaTotal: ca.Count, CaEnabled: caEnabled, CaReportOnly: caReport, CaDisabled: caDisabled,
            CompliancePolicies: comp.Count, CompliancePlatforms: platforms,
            EndpointSecurityIntents: endp.Count, AppProtectionPolicies: appp.Count,
            AuthStrengthPolicies: auth2.Count, NamedLocations: loc.Count);

        var caRows = ca.OrderBy(p => p.DisplayName)
            .Select(p => new CaListRowDto(p.Id ?? "", p.DisplayName ?? "(unnamed)", p.State?.ToString() ?? "disabled"))
            .ToList();
        var compRows = comp.OrderBy(p => p.DisplayName)
            .Select(p => new ComplianceRowDto(p.Id ?? "", p.DisplayName ?? "(unnamed)", DetectPlatform(p)))
            .ToList();

        return new SecurityPostureDto(score, breakdown, gaps, stats, caRows, compRows);
    }

    private static (int Score, List<ScoreCategoryDto> Breakdown, List<SecurityGapDto> Gaps) ComputeScore(
        List<ConditionalAccessPolicy> ca,
        List<DeviceCompliancePolicy> comp,
        List<DeviceManagementIntent> endp,
        List<ManagedAppPolicy> app,
        List<AuthenticationStrengthPolicy> auth,
        List<NamedLocation> loc)
    {
        var cats = new List<ScoreCategoryDto>();
        var gaps = new List<SecurityGapDto>();

        // Conditional Access (max 30)
        var caEnabled = ca.Count(p => p.State == ConditionalAccessPolicyState.Enabled);
        var caReport = ca.Count(p => p.State == ConditionalAccessPolicyState.EnabledForReportingButNotEnforced);
        var caScore = Math.Min(30, caEnabled * 5 + caReport * 2);
        var caItems = new List<string>();
        if (caEnabled > 0) caItems.Add($"{caEnabled} enabled");
        if (caReport > 0) caItems.Add($"{caReport} report-only");
        if (caItems.Count == 0) caItems.Add("none enabled");
        cats.Add(new ScoreCategoryDto("Conditional Access", caScore, 30, caItems));
        if (caEnabled == 0)
            gaps.Add(new SecurityGapDto("high", "Conditional Access", "No Conditional Access policies are enabled"));
        if (!ca.Any(p => p.Conditions?.Users?.IncludeUsers?.Contains("All") == true))
            gaps.Add(new SecurityGapDto("medium", "Conditional Access", "No CA policy targets all users"));

        // Compliance (max 25)
        var platforms = comp.Select(DetectPlatform).Where(p => p != "Unknown").Distinct().ToList();
        var compScore = Math.Min(25, comp.Count * 4 + platforms.Count * 3);
        cats.Add(new ScoreCategoryDto("Compliance", compScore, 25,
            new[] { $"{comp.Count} policies", $"{platforms.Count} platforms covered" }));
        if (comp.Count == 0)
            gaps.Add(new SecurityGapDto("high", "Compliance", "No compliance policies configured"));
        else if (!platforms.Contains("Windows"))
            gaps.Add(new SecurityGapDto("medium", "Compliance", "No Windows compliance policy detected"));

        // Endpoint Security (max 20)
        var endpScore = Math.Min(20, endp.Count * 5);
        cats.Add(new ScoreCategoryDto("Endpoint Security", endpScore, 20,
            new[] { $"{endp.Count} intents configured" }));
        if (endp.Count == 0)
            gaps.Add(new SecurityGapDto("medium", "Endpoint Security", "No endpoint security intents configured"));

        // App Protection (max 15)
        var appScore = Math.Min(15, app.Count * 5);
        cats.Add(new ScoreCategoryDto("App Protection", appScore, 15, new[] { $"{app.Count} policies" }));
        if (app.Count == 0)
            gaps.Add(new SecurityGapDto("medium", "App Protection", "No app protection policies — mobile data is unprotected"));

        // Auth Strength + Named Locations (max 10)
        var miscScore = Math.Min(10, auth.Count * 3 + loc.Count * 2);
        cats.Add(new ScoreCategoryDto("Auth & Locations", miscScore, 10,
            new[] { $"{auth.Count} auth strengths", $"{loc.Count} named locations" }));
        if (loc.Count == 0)
            gaps.Add(new SecurityGapDto("low", "Named Locations", "No named locations — consider adding trusted locations"));

        var total = cats.Sum(c => c.Score);
        // High → medium → low.
        var order = new Dictionary<string, int> { ["high"] = 0, ["medium"] = 1, ["low"] = 2 };
        gaps = gaps.OrderBy(x => order.GetValueOrDefault(x.Severity, 3)).ToList();
        return (total, cats, gaps);
    }

    // Platform from the Graph compliance-policy subtype name (matches IntuneCommander).
    private static string DetectPlatform(DeviceCompliancePolicy policy)
    {
        var t = policy.GetType().Name;
        if (t.Contains("Windows")) return "Windows";
        if (t.Contains("Ios") || t.Contains("IOS")) return "iOS";
        if (t.Contains("Android")) return "Android";
        if (t.Contains("MacOS") || t.Contains("Macos")) return "macOS";
        return "Unknown";
    }
}
