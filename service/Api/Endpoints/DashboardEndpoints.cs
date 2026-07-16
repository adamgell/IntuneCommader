using Microsoft.Graph.Beta.Models;
using Intune.Commander.Core.Services;
// Microsoft.Graph.Beta.Models also defines a `WebApplication` type, which collides
// with the ASP.NET host type used by the extension method signature (CS0104). Alias
// the host type so `this WebApplication app` resolves unambiguously in this module.
using WebApplication = Microsoft.AspNetCore.Builder.WebApplication;

namespace CmProjectX.Api;

// Dashboard (read-only aggregates) — two summary surfaces that fan out across the
// forked Core services and project a handful of count/value metrics into the shared
// normalized {id,title,subtitle,badge} ListItemDto shape, so the client can render
// each metric as a tile via the generic list workspace.
//
//   /dashboard         tenant inventory counts (Applications, policies, devices, …)
//   /security-posture  compliance & security summary (device compliance breakdown)
//
// Each metric is gathered inside its own try/catch returning Badge="—" on failure,
// so one permission-denied / failing Graph surface never breaks the whole dashboard.
// Inventory counts run concurrently (Task.WhenAll). Both 409 (Conflict) when signed
// out (auth.Graph is null) so the client shows a "sign in" empty state.
public static class DashboardEndpoints
{
    // Em-dash sentinel shown in a tile's Badge when that metric's gather failed.
    private const string Failed = "—";

    public static void MapDashboard(this WebApplication app)
    {
        // ─── /dashboard — tenant inventory counts ───────────────────────────────────
        // One tile per surface (Title=metric, Subtitle=description, Badge=count). Each
        // count is gathered in its own guarded task so a single failing surface only
        // degrades its own tile (Badge="—") instead of failing the whole response.
        app.MapGet("/dashboard", async (AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();

            var apps = Tile(
                "Applications", "Mobile apps in the tenant",
                async () => (await new ApplicationService(g).ListApplicationsAsync(ct)).Count);

            var compliance = Tile(
                "Compliance Policies", "Device compliance policies",
                async () => (await new CompliancePolicyService(g).ListCompliancePoliciesAsync(ct)).Count);

            var configs = Tile(
                "Configuration Profiles", "Device configuration profiles",
                async () => (await new ConfigurationProfileService(g).ListDeviceConfigurationsAsync(ct)).Count);

            var settingsCatalog = Tile(
                "Settings Catalog", "Settings catalog policies",
                async () => (await new SettingsCatalogService(g).ListSettingsCatalogPoliciesAsync(ct)).Count);

            var conditionalAccess = Tile(
                "Conditional Access", "Conditional access policies",
                async () => (await new ConditionalAccessPolicyService(g).ListPoliciesAsync(ct)).Count);

            var groups = Tile(
                "Groups", "Entra ID groups (assigned + dynamic)",
                async () =>
                {
                    var svc = new GroupService(g);
                    var assigned = await svc.ListAssignedGroupsAsync(ct);
                    var dynamic = await svc.ListDynamicGroupsAsync(ct);
                    return assigned.Count + dynamic.Count;
                });

            var devices = Tile(
                "Managed Devices", "Enrolled managed devices",
                async () => (await new ManagedDeviceService(g).ListManagedDevicesAsync(ct)).Count);

            var scopeTags = Tile(
                "Scope Tags", "Role scope tags",
                async () => (await new ScopeTagService(g).ListScopeTagsAsync(ct)).Count);

            var tiles = await Task.WhenAll(
                apps, compliance, configs, settingsCatalog,
                conditionalAccess, groups, devices, scopeTags);

            return Results.Ok(tiles.AsEnumerable());
        });

        // ─── /security-posture — compliance & security summary ──────────────────────
        // Buckets the managed-device list by ComplianceState (compliant / noncompliant
        // / unknown-or-other) and reports CA + compliance policy counts. The device
        // breakdown is one guarded gather (all three device tiles share its result, or
        // all degrade together); the policy counts are independent guarded gathers.
        app.MapGet("/security-posture", async (AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();

            // Gather the device breakdown once, guarded. Null = the gather failed, in
            // which case the three device tiles all render Badge="—".
            (int total, int compliant, int noncompliant)? breakdown;
            try
            {
                var managed = await new ManagedDeviceService(g).ListManagedDevicesAsync(ct);
                var compliant = managed.Count(d => d.ComplianceState == ComplianceState.Compliant);
                var noncompliant = managed.Count(d => d.ComplianceState == ComplianceState.Noncompliant);
                breakdown = (managed.Count, compliant, noncompliant);
            }
            catch
            {
                breakdown = null;
            }

            var caPolicies = Tile(
                "Conditional Access Policies", "Conditional access policies",
                async () => (await new ConditionalAccessPolicyService(g).ListPoliciesAsync(ct)).Count);

            var compliancePolicies = Tile(
                "Compliance Policies", "Device compliance policies",
                async () => (await new CompliancePolicyService(g).ListCompliancePoliciesAsync(ct)).Count);

            var policyTiles = await Task.WhenAll(caPolicies, compliancePolicies);

            var items = new List<ListItemDto>
            {
                new("total-devices", "Total Devices", "All enrolled managed devices",
                    breakdown is { } b0 ? b0.total.ToString() : Failed),
                new("compliant", "Compliant", "Devices reporting compliant",
                    breakdown is { } b1 ? b1.compliant.ToString() : Failed),
                new("noncompliant", "Noncompliant", "Devices reporting noncompliant",
                    breakdown is { } b2 ? b2.noncompliant.ToString() : Failed),
            };
            items.AddRange(policyTiles);

            return Results.Ok(items.AsEnumerable());
        });
    }

    // Builds one metric tile, evaluating its count inside a guard so a failing /
    // permission-denied Graph surface degrades to Badge="—" instead of throwing.
    private static async Task<ListItemDto> Tile(string title, string subtitle, Func<Task<int>> count)
    {
        string badge;
        try { badge = (await count()).ToString(); }
        catch { badge = Failed; }
        return new ListItemDto(Slug(title), title, subtitle, badge);
    }

    // Stable lowercase-dashed id from the metric title (e.g. "Compliance Policies"
    // → "compliance-policies"), so each tile carries a deterministic ListItemDto.Id.
    private static string Slug(string title) =>
        title.ToLowerInvariant().Replace(' ', '-');
}
