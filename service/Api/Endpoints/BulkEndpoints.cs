using System.IO.Compression;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Intune.Commander.Core.Models;
using Intune.Commander.Core.Services;
// Microsoft.Graph.Beta.Models also defines a `WebApplication` type; alias to the
// ASP.NET host so the extension method resolves (see DevicesEndpoints for why).
using WebApplication = Microsoft.AspNetCore.Builder.WebApplication;

namespace CmProjectX.Api;

// M8 Bulk & lifecycle — surfaces the forked Core's Export / Import / Baseline
// engines over HTTP. Export now covers the full backup set (all types the Core
// ExportService supports); restore is a LIVE write to the tenant, gated behind an
// explicit dryRun=false and confirmed in the client. Baselines lists the embedded
// OIB/CIS catalog (no Graph).
public static class BulkEndpoints
{
    // Empty typed assignment lists for the tuple-shaped bulk export methods. We
    // back up the objects themselves; per-item assignment fetch (N extra Graph
    // calls per type) is a follow-up — assignments re-apply on restore separately.
    private static readonly IReadOnlyList<DeviceCompliancePolicyAssignment> NoCompAsg = Array.Empty<DeviceCompliancePolicyAssignment>();
    private static readonly IReadOnlyList<MobileAppAssignment> NoAppAsg = Array.Empty<MobileAppAssignment>();
    private static readonly IReadOnlyList<DeviceManagementIntentAssignment> NoIntentAsg = Array.Empty<DeviceManagementIntentAssignment>();
    private static readonly IReadOnlyList<GroupPolicyConfigurationAssignment> NoTemplateAsg = Array.Empty<GroupPolicyConfigurationAssignment>();
    private static readonly IReadOnlyList<DeviceManagementConfigurationPolicyAssignment> NoScAsg = Array.Empty<DeviceManagementConfigurationPolicyAssignment>();

    public static void MapBulk(this WebApplication app)
    {
        // GET /export[?types=A,B,...] — back up the tenant to a downloadable .zip.
        // Default (no `types`) exports every supported surface; `types` narrows it to
        // a comma-separated set of folder keys. Live Graph; 409 when signed out.
        app.MapGet("/export", async (AuthSession auth, string? types, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var only = ParseTypes(types);

            var work = Path.Combine(Path.GetTempPath(), "cmprojectx-export-" + Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(work);
            var zipPath = work + ".zip";
            try
            {
                await RunExportAsync(g, work, only, ct);
                ZipFile.CreateFromDirectory(work, zipPath);
                var bytes = await File.ReadAllBytesAsync(zipPath, ct);
                return Results.File(bytes, "application/zip",
                    $"cmprojectx-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip");
            }
            finally
            {
                try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { /* best-effort */ }
                try { Directory.Delete(work, recursive: true); } catch { /* best-effort */ }
            }
        });

        // POST /import — inspect (dryRun, the DEFAULT) or RESTORE (dryRun=false) a
        // backup .zip. Dry-run lists the bundle's contents without writing. Restore
        // is a LIVE tenant write: each object is created fresh (ids cleared) via the
        // Core ImportService; the client gates this behind an explicit confirm.
        app.MapPost("/import", async (HttpRequest req, AuthSession auth, bool? dryRun, CancellationToken ct) =>
        {
            using var ms = new MemoryStream();
            await req.Body.CopyToAsync(ms, ct);
            if (ms.Length == 0) return ApiResults.BadRequest("empty body — POST a backup .zip");

            // Preview (always computed — the dry-run response and the restore summary both use it).
            var items = new List<ImportItemDto>();
            try
            {
                ms.Position = 0;
                using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
                foreach (var e in zip.Entries)
                {
                    if (e.Length == 0 || !e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                    if (e.Name.Equals("migration-table.json", StringComparison.OrdinalIgnoreCase)) continue;
                    var type = e.FullName.Contains('/') ? e.FullName.Split('/')[0] : "(root)";
                    items.Add(new ImportItemDto(type, Path.GetFileNameWithoutExtension(e.Name)));
                }
            }
            catch (InvalidDataException)
            {
                return ApiResults.BadRequest("not a valid .zip backup");
            }

            var groups = items
                .GroupBy(i => i.Type)
                .OrderBy(grp => grp.Key)
                .Select(grp => new ImportPreviewGroupDto(grp.Key, grp.Count(), grp.Select(x => x.Name).Take(50).ToList()))
                .ToList();
            var preview = new ImportPreviewDto(items.Count, groups);

            if (dryRun != false)
                return Results.Ok(preview);

            // ── Live restore ──────────────────────────────────────────────────
            var g = auth.Graph; if (g is null) return Results.Conflict();

            var work = Path.Combine(Path.GetTempPath(), "cmprojectx-restore-" + Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(work);
            try
            {
                ms.Position = 0;
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
                    zip.ExtractToDirectory(work, overwriteFiles: true);

                var results = await RunRestoreAsync(g, work, ct);
                return Results.Ok(new ImportRestoreResultDto(
                    Applied: true,
                    TotalCreated: results.Sum(r => r.Created),
                    TotalFailed: results.Sum(r => r.Failed),
                    Groups: results));
            }
            finally
            {
                try { Directory.Delete(work, recursive: true); } catch { /* best-effort */ }
            }
        });

        // GET /baselines — embedded OIB/CIS security baselines (sync, no Graph), as
        // normalized list rows for the Tiles screen.
        app.MapGet("/baselines", () =>
        {
            var baselines = new BaselineService().GetAllBaselines();
            return Results.Ok(baselines.Select(b => new ListItemDto(
                b.FileName,
                b.Name,
                $"{b.PolicyType} · {b.Category}",
                b.PolicyType.ToString())));
        });

        // POST /baselines/compare — a SettingsCatalog baseline (by FileName) vs a live
        // tenant SettingsCatalog policy's settings. Uses BaselineService.CompareSettingsCatalog
        // (only SettingsCatalog baselines are comparable). Volatile — goes straight to Graph.
        app.MapPost("/baselines/compare", async (BaselineCompareRequest req, AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            if (string.IsNullOrWhiteSpace(req.BaselineId) || string.IsNullOrWhiteSpace(req.PolicyId))
                return ApiResults.BadRequest("baselineId and policyId are required");

            var svc = new BaselineService();
            // Resolve the baseline by FileName (the id the /baselines projection emits).
            var baseline = svc
                .GetBaselinesByType(Intune.Commander.Core.Models.BaselinePolicyType.SettingsCatalog)
                .FirstOrDefault(b => string.Equals(b.FileName, req.BaselineId, StringComparison.OrdinalIgnoreCase));
            if (baseline is null)
                return ApiResults.NotFound("unknown SettingsCatalog baseline (compare by FileName)");

            IReadOnlyList<DeviceManagementConfigurationSetting> settings;
            try { settings = (await new SettingsCatalogService(g).GetPolicySettingsAsync(req.PolicyId, ct)).ToList(); }
            catch (Exception ex) { return ApiResults.BadRequest("could not read tenant policy settings: " + ex.Message); }

            var result = svc.CompareSettingsCatalog(baseline, settings, req.PolicyId, null);

            // Shared projection resolves each setting id to its human-readable name.
            var Proj = PolicyCompareEndpoints.ProjectComparisons;

            return Results.Ok(new BaselineComparisonDto(
                result.BaselineName, result.TenantPolicyId, result.TenantPolicyName,
                Proj(result.Matching), Proj(result.Missing), Proj(result.Drifted), Proj(result.Extra)));
        });
    }

    private static HashSet<string>? ParseTypes(string? types) =>
        string.IsNullOrWhiteSpace(types)
            ? null
            : types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .ToHashSet(StringComparer.OrdinalIgnoreCase);

    // Fetch + export each requested surface. Each type is isolated in its own
    // try/catch so one surface failing (a permission gap, a beta-API hiccup) never
    // aborts the whole backup. Folder keys match the ExportService subfolders so a
    // round-trip restore lines up.
    private static async Task RunExportAsync(GraphServiceClient g, string work, HashSet<string>? only, CancellationToken ct)
    {
        var export = new ExportService();
        bool Want(string k) => only is null || only.Contains(k);
        async Task Try(string key, Func<Task> run) { if (Want(key)) { try { await run(); } catch { /* skip surface */ } } }

        await Try("DeviceConfigurations", async () =>
            await export.ExportDeviceConfigurationsAsync(await new ConfigurationProfileService(g).ListDeviceConfigurationsAsync(ct), work, ct));
        await Try("CompliancePolicies", async () =>
            await export.ExportCompliancePoliciesAsync((await new CompliancePolicyService(g).ListCompliancePoliciesAsync(ct)).Select(p => (p, NoCompAsg)), work, ct));
        await Try("SettingsCatalog", async () =>
        {
            var sc = new SettingsCatalogService(g);
            var pols = await sc.ListSettingsCatalogPoliciesAsync(ct);
            var tuples = new List<(DeviceManagementConfigurationPolicy, IReadOnlyList<DeviceManagementConfigurationSetting>, IReadOnlyList<DeviceManagementConfigurationPolicyAssignment>)>();
            foreach (var p in pols)
            {
                IReadOnlyList<DeviceManagementConfigurationSetting> s;
                try { s = await sc.GetPolicySettingsAsync(p.Id ?? "", ct); } catch { s = Array.Empty<DeviceManagementConfigurationSetting>(); }
                tuples.Add((p, s, NoScAsg));
            }
            await export.ExportSettingsCatalogPoliciesAsync(tuples, work, ct);
        });
        await Try("AdministrativeTemplates", async () =>
            await export.ExportAdministrativeTemplatesAsync((await new AdministrativeTemplateService(g).ListAdministrativeTemplatesAsync(ct)).Select(t => (t, NoTemplateAsg)), work, ct));
        await Try("EndpointSecurity", async () =>
            await export.ExportEndpointSecurityIntentsAsync((await new EndpointSecurityService(g).ListEndpointSecurityIntentsAsync(ct)).Select(i => (i, NoIntentAsg)), work, ct));
        await Try("Applications", async () =>
            await export.ExportApplicationsAsync((await new ApplicationService(g).ListApplicationsAsync(ct)).Select(a => (a, NoAppAsg)), work, ct));
        await Try("AppProtectionPolicies", async () =>
            await export.ExportAppProtectionPoliciesAsync(await new AppProtectionPolicyService(g).ListAppProtectionPoliciesAsync(ct), work, ct));
        await Try("ManagedDeviceAppConfigurations", async () =>
            await export.ExportManagedDeviceAppConfigurationsAsync(await new ManagedAppConfigurationService(g).ListManagedDeviceAppConfigurationsAsync(ct), work, ct));
        await Try("TargetedManagedAppConfigurations", async () =>
            await export.ExportTargetedManagedAppConfigurationsAsync(await new ManagedAppConfigurationService(g).ListTargetedManagedAppConfigurationsAsync(ct), work, ct));
        await Try("EnrollmentConfigurations", async () =>
            await export.ExportEnrollmentConfigurationsAsync(await new EnrollmentConfigurationService(g).ListEnrollmentConfigurationsAsync(ct), work, ct));
        await Try("AutopilotProfiles", async () =>
            await export.ExportAutopilotProfilesAsync(await new AutopilotService(g).ListAutopilotProfilesAsync(ct), work, ct));
        await Try("ScopeTags", async () =>
            await export.ExportScopeTagsAsync(await new ScopeTagService(g).ListScopeTagsAsync(ct), work, ct));
        await Try("RoleDefinitions", async () =>
            await export.ExportRoleDefinitionsAsync(await new RoleDefinitionService(g).ListRoleDefinitionsAsync(ct), work, ct));
        await Try("NamedLocations", async () =>
            await export.ExportNamedLocationsAsync(await new NamedLocationService(g).ListNamedLocationsAsync(ct), work, ct));
        await Try("AuthenticationStrengths", async () =>
            await export.ExportAuthenticationStrengthPoliciesAsync(await new AuthenticationStrengthService(g).ListAuthenticationStrengthPoliciesAsync(ct), work, ct));
        await Try("AuthenticationContexts", async () =>
            await export.ExportAuthenticationContextsAsync(await new AuthenticationContextService(g).ListAuthenticationContextsAsync(ct), work, ct));
        await Try("ConditionalAccessPolicies", async () =>
            await export.ExportConditionalAccessPoliciesAsync(await new ConditionalAccessPolicyService(g).ListPoliciesAsync(ct), work, ct));
        await Try("TermsOfUse", async () =>
            await export.ExportTermsOfUseAgreementsAsync(await new TermsOfUseService(g).ListTermsOfUseAgreementsAsync(ct), work, ct));
        await Try("TermsAndConditions", async () =>
            await export.ExportTermsAndConditionsCollectionAsync(await new TermsAndConditionsService(g).ListTermsAndConditionsAsync(ct), work, ct));
        await Try("DeviceHealthScripts", async () =>
            await export.ExportDeviceHealthScriptsAsync(await new DeviceHealthScriptService(g).ListDeviceHealthScriptsAsync(ct), work, ct));
        await Try("DeviceManagementScripts", async () =>
            await export.ExportDeviceManagementScriptsAsync(await new DeviceManagementScriptService(g).ListDeviceManagementScriptsAsync(ct), work, ct));
        await Try("DeviceShellScripts", async () =>
            await export.ExportDeviceShellScriptsAsync(await new DeviceShellScriptService(g).ListDeviceShellScriptsAsync(ct), work, ct));
        await Try("ComplianceScripts", async () =>
            await export.ExportComplianceScriptsAsync(await new ComplianceScriptService(g).ListComplianceScriptsAsync(ct), work, ct));
        await Try("MacCustomAttributes", async () =>
            await export.ExportMacCustomAttributesAsync(await new MacCustomAttributeService(g).ListMacCustomAttributesAsync(ct), work, ct));
        await Try("FeatureUpdates", async () =>
            await export.ExportFeatureUpdateProfilesAsync(await new FeatureUpdateProfileService(g).ListFeatureUpdateProfilesAsync(ct), work, ct));
        await Try("QualityUpdates", async () =>
            await export.ExportQualityUpdateProfilesAsync(await new QualityUpdateProfileService(g).ListQualityUpdateProfilesAsync(ct), work, ct));
        await Try("DriverUpdates", async () =>
            await export.ExportDriverUpdateProfilesAsync(await new DriverUpdateProfileService(g).ListDriverUpdateProfilesAsync(ct), work, ct));
        await Try("IntuneBrandingProfiles", async () =>
            await export.ExportIntuneBrandingProfilesAsync(await new IntuneBrandingService(g).ListIntuneBrandingProfilesAsync(ct), work, ct));
        await Try("AzureBrandingLocalizations", async () =>
            await export.ExportAzureBrandingLocalizationsAsync(await new AzureBrandingService(g).ListBrandingLocalizationsAsync(ct), work, ct));
        await Try("ReusablePolicySettings", async () =>
            await export.ExportReusablePolicySettingsAsync(await new ReusablePolicySettingService(g).ListReusablePolicySettingsAsync(ct), work, ct));
        await Try("NotificationTemplates", async () =>
            await export.ExportNotificationTemplatesAsync(await new NotificationTemplateService(g).ListNotificationTemplatesAsync(ct), work, ct));
        await Try("AdmxFiles", async () =>
            await export.ExportAdmxFilesAsync(await new AdmxFileService(g).ListAdmxFilesAsync(ct), work, ct));
    }

    private static ImportService BuildImportService(GraphServiceClient g) => new(
        new ConfigurationProfileService(g),
        new CompliancePolicyService(g),
        new EndpointSecurityService(g),
        new AdministrativeTemplateService(g),
        new EnrollmentConfigurationService(g),
        new AppProtectionPolicyService(g),
        new ManagedAppConfigurationService(g),
        new TermsAndConditionsService(g),
        new ScopeTagService(g),
        new RoleDefinitionService(g),
        new IntuneBrandingService(g),
        new AzureBrandingService(g),
        new AutopilotService(g),
        new DeviceHealthScriptService(g),
        new MacCustomAttributeService(g),
        new FeatureUpdateProfileService(g),
        new NamedLocationService(g),
        new AuthenticationStrengthService(g),
        new AuthenticationContextService(g),
        new TermsOfUseService(g),
        new DeviceManagementScriptService(g),
        new DeviceShellScriptService(g),
        new ComplianceScriptService(g),
        new QualityUpdateProfileService(g),
        new DriverUpdateProfileService(g),
        new SettingsCatalogService(g));

    // Restore each type folder present in the bundle back into the live tenant.
    // Every object is created fresh (ids cleared by the Core ImportService), tracked
    // per-item so a single bad object is reported rather than aborting its type.
    private static async Task<List<ImportRestoreGroupDto>> RunRestoreAsync(GraphServiceClient g, string root, CancellationToken ct)
    {
        var import = BuildImportService(g);
        MigrationTable migration;
        try { migration = await import.ReadMigrationTableAsync(root, ct); } catch { migration = new MigrationTable(); }

        var groups = new List<ImportRestoreGroupDto>();

        async Task Do<T>(string key, Func<Task<List<T>>> read, Func<T, Task> importOne)
        {
            List<T> items;
            try { items = await read(); } catch { return; }
            if (items.Count == 0) return;
            int ok = 0, fail = 0; var errs = new List<string>();
            foreach (var it in items)
            {
                try { await importOne(it); ok++; }
                catch (Exception ex) { fail++; if (errs.Count < 5) errs.Add(ex.Message); }
            }
            groups.Add(new ImportRestoreGroupDto(key, ok, fail, errs));
        }

        await Do("DeviceConfigurations", () => import.ReadDeviceConfigurationsFromFolderAsync(root, ct), x => import.ImportDeviceConfigurationAsync(x, migration, ct));
        await Do("CompliancePolicies", () => import.ReadCompliancePoliciesFromFolderAsync(root, ct), x => import.ImportCompliancePolicyAsync(x, migration, ct));
        await Do("SettingsCatalog", () => import.ReadSettingsCatalogPoliciesFromFolderAsync(root, ct), x => import.ImportSettingsCatalogPolicyAsync(x, migration, ct));
        await Do("AdministrativeTemplates", () => import.ReadAdministrativeTemplatesFromFolderAsync(root, ct), x => import.ImportAdministrativeTemplateAsync(x, migration, ct));
        await Do("EndpointSecurity", () => import.ReadEndpointSecurityIntentsFromFolderAsync(root, ct), x => import.ImportEndpointSecurityIntentAsync(x, migration, ct));
        await Do("EnrollmentConfigurations", () => import.ReadEnrollmentConfigurationsFromFolderAsync(root, ct), x => import.ImportEnrollmentConfigurationAsync(x, migration, ct));
        await Do("AppProtectionPolicies", () => import.ReadAppProtectionPoliciesFromFolderAsync(root, ct), x => import.ImportAppProtectionPolicyAsync(x, migration, ct));
        await Do("ManagedDeviceAppConfigurations", () => import.ReadManagedDeviceAppConfigurationsFromFolderAsync(root, ct), x => import.ImportManagedDeviceAppConfigurationAsync(x, migration, ct));
        await Do("TargetedManagedAppConfigurations", () => import.ReadTargetedManagedAppConfigurationsFromFolderAsync(root, ct), x => import.ImportTargetedManagedAppConfigurationAsync(x, migration, ct));
        await Do("AutopilotProfiles", () => import.ReadAutopilotProfilesFromFolderAsync(root, ct), x => import.ImportAutopilotProfileAsync(x, migration, ct));
        await Do("ScopeTags", () => import.ReadScopeTagsFromFolderAsync(root, ct), x => import.ImportScopeTagAsync(x, migration, ct));
        await Do("RoleDefinitions", () => import.ReadRoleDefinitionsFromFolderAsync(root, ct), x => import.ImportRoleDefinitionAsync(x, migration, ct));
        await Do("NamedLocations", () => import.ReadNamedLocationsFromFolderAsync(root, ct), x => import.ImportNamedLocationAsync(x, migration, ct));
        await Do("AuthenticationStrengths", () => import.ReadAuthenticationStrengthPoliciesFromFolderAsync(root, ct), x => import.ImportAuthenticationStrengthPolicyAsync(x, migration, ct));
        await Do("AuthenticationContexts", () => import.ReadAuthenticationContextsFromFolderAsync(root, ct), x => import.ImportAuthenticationContextAsync(x, migration, ct));
        await Do("TermsOfUse", () => import.ReadTermsOfUseAgreementsFromFolderAsync(root, ct), x => import.ImportTermsOfUseAgreementAsync(x, migration, ct));
        await Do("TermsAndConditions", () => import.ReadTermsAndConditionsFromFolderAsync(root, ct), x => import.ImportTermsAndConditionsAsync(x, migration, ct));
        await Do("DeviceHealthScripts", () => import.ReadDeviceHealthScriptsFromFolderAsync(root, ct), x => import.ImportDeviceHealthScriptAsync(x, migration, ct));
        await Do("DeviceManagementScripts", () => import.ReadDeviceManagementScriptsFromFolderAsync(root, ct), x => import.ImportDeviceManagementScriptAsync(x, migration, ct));
        await Do("DeviceShellScripts", () => import.ReadDeviceShellScriptsFromFolderAsync(root, ct), x => import.ImportDeviceShellScriptAsync(x, migration, ct));
        await Do("ComplianceScripts", () => import.ReadComplianceScriptsFromFolderAsync(root, ct), x => import.ImportComplianceScriptAsync(x, migration, ct));
        await Do("MacCustomAttributes", () => import.ReadMacCustomAttributesFromFolderAsync(root, ct), x => import.ImportMacCustomAttributeAsync(x, migration, ct));
        await Do("FeatureUpdates", () => import.ReadFeatureUpdateProfilesFromFolderAsync(root, ct), x => import.ImportFeatureUpdateProfileAsync(x, migration, ct));
        await Do("QualityUpdates", () => import.ReadQualityUpdateProfilesFromFolderAsync(root, ct), x => import.ImportQualityUpdateProfileAsync(x, migration, ct));
        await Do("DriverUpdates", () => import.ReadDriverUpdateProfilesFromFolderAsync(root, ct), x => import.ImportDriverUpdateProfileAsync(x, migration, ct));
        await Do("IntuneBrandingProfiles", () => import.ReadIntuneBrandingProfilesFromFolderAsync(root, ct), x => import.ImportIntuneBrandingProfileAsync(x, migration, ct));
        await Do("AzureBrandingLocalizations", () => import.ReadAzureBrandingLocalizationsFromFolderAsync(root, ct), x => import.ImportAzureBrandingLocalizationAsync(x, migration, ct));

        return groups;
    }
}
