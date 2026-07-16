// PolicyCompare group — a symmetric "compare any two live Settings Catalog policies"
// (A vs B), the IntuneCommander policy-vs-policy diff. Reuses the same setting-value
// extraction as the baseline comparison (Core BaselineService.CompareTwoPolicies) and
// resolves each setting id to its human-readable name from the embedded Settings Catalog
// definition registry. 409 (Conflict) = signed out (auth.Graph null).
using Intune.Commander.Core.Models;
using Intune.Commander.Core.Services;
using Microsoft.Graph.Beta.Models;

namespace CmProjectX.Api;

public static class PolicyCompareEndpoints
{
    public static void MapPolicyCompare(this Microsoft.AspNetCore.Builder.WebApplication app)
    {
        // POST /policies/compare — diff two live SettingsCatalog policies' settings.
        app.MapPost("/policies/compare", async (PolicyCompareRequest req, AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            if (string.IsNullOrWhiteSpace(req.AId) || string.IsNullOrWhiteSpace(req.BId))
                return ApiResults.BadRequest("aId and bId are required");

            var sc = new SettingsCatalogService(g);
            IReadOnlyList<DeviceManagementConfigurationSetting> a, b;
            try
            {
                a = (await sc.GetPolicySettingsAsync(req.AId, ct)).ToList();
                b = (await sc.GetPolicySettingsAsync(req.BId, ct)).ToList();
            }
            catch (Exception ex) { return ApiResults.BadRequest("could not read policy settings: " + ex.Message); }

            var (nameA, nameB) = (await ResolveName(sc, req.AId, ct), await ResolveName(sc, req.BId, ct));
            var result = BaselineService.CompareTwoPolicies(a, b, nameA, nameB, req.AId, req.BId);

            return Results.Ok(new BaselineComparisonDto(
                result.BaselineName, result.TenantPolicyId, result.TenantPolicyName,
                ProjectComparisons(result.Matching), ProjectComparisons(result.Missing),
                ProjectComparisons(result.Drifted), ProjectComparisons(result.Extra)));
        });
    }

    // Best-effort friendly name for a policy id (falls back to the id on any error).
    private static async Task<string?> ResolveName(SettingsCatalogService sc, string id, CancellationToken ct)
    {
        try { return (await sc.GetSettingsCatalogPolicyAsync(id, ct))?.Name ?? id; }
        catch { return id; }
    }

    // Shared projection used by both /policies/compare and /baselines/compare: resolves
    // the human-readable setting name from the embedded Settings Catalog registry.
    public static IReadOnlyList<BaselineSettingComparisonDto> ProjectComparisons(
        IReadOnlyList<BaselineSettingComparison> xs) =>
        xs.Select(x => new BaselineSettingComparisonDto(
            x.SettingDefinitionId,
            SettingsCatalogDefinitionRegistry.ResolveDisplayName(x.SettingDefinitionId),
            x.BaselineValue, x.TenantValue)).ToList();
}
