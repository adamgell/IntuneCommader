using System.Text.Json;
using CmProjectX.Store;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Serialization.Json;
using Intune.Commander.Core.Models;
using Intune.Commander.Core.Services;
// Microsoft.Graph.Beta.Models also defines a `WebApplication` type; alias to the
// ASP.NET host so the extension method resolves (see DevicesEndpoints for why).
using WebApplication = Microsoft.AspNetCore.Builder.WebApplication;
// GraphServiceClient is fully-qualified at every use (no `using Microsoft.Graph.Beta`)
// so it never shadows IResult / breaks Minimal API overload resolution.

namespace CmProjectX.Api;

// M15 Policy-as-Code / GitOps — pull → plan → (gate) → apply over the existing
// Export / Import / Drift engines (the M15 thesis: this is wiring, not building).
//
//   pull   = ExportService(live) → normalized tree + manifest        (POST /gitops/pull)
//   plan   = re-pull live → JsonDrift.Diff(repo, live)               (POST /gitops/plan)
//   apply  = confirm + planId → ImportService / Core update + snapshot (POST /gitops/apply)
//   status = manifest vs live drift summary                          (GET  /gitops/status)
//
// Both sides of every diff go through the SAME pipeline GraphDeltaSync uses for the
// snapshot store (System.Text.Json runtime-type serialize + ExportNormalizer), so a
// freshly-pulled tree diffs to zero and the local mirror matches the time-machine.
public static class GitOpsEndpoints
{
    public static void MapGitOps(this WebApplication app)
    {
        // POST /gitops/pull — export the live tenant to a normalized repo tree.
        app.MapPost("/gitops/pull", async (GitOpsPullRequest req, AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            if (string.IsNullOrWhiteSpace(req.OutputPath))
                return ApiResults.BadRequest("outputPath is required");

            var surfaces = GitOpsSurfaces.Resolve(req.Surfaces);
            Directory.CreateDirectory(req.OutputPath);
            await PullAsync(g, req.OutputPath, surfaces, ct);

            var normalize = Normalizer();
            var files = GitOpsTree.ReadTree(req.OutputPath, surfaces, normalize);
            var counts = files.GroupBy(f => f.Surface.Folder).ToDictionary(grp => grp.Key, grp => grp.Count());
            var profile = auth.ActiveProfile;
            var manifest = new GitOpsManifest(
                GitOpsTree.SchemaVersion,
                profile?.TenantId,
                profile?.Name,
                DateTime.UtcNow.ToString("o"),
                profile?.Name,
                GitOpsTree.CmpxVersion,
                counts,
                files.Count,
                "sha256:" + GitOpsTree.RollupHash(files));

            await File.WriteAllTextAsync(
                Path.Combine(req.OutputPath, "manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }),
                ct);
            return Results.Ok(manifest);
        });

        // POST /gitops/plan — read-only diff of a repo tree against the live tenant.
        app.MapPost("/gitops/plan", async (GitOpsPlanRequest req, AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            if (string.IsNullOrWhiteSpace(req.RepoPath))
                return ApiResults.BadRequest("repoPath is required");
            if (!File.Exists(Path.Combine(req.RepoPath, "manifest.json")))
                return ApiResults.BadRequest("no manifest.json in repoPath — pull first");

            var activeTenant = auth.ActiveProfile?.TenantId;
            var manifest = await ReadManifestMetadataAsync(req.RepoPath, ct);
            if (TenantMismatch(manifest.TenantId, activeTenant))
                return TenantConflict("repo manifest", manifest.TenantId, activeTenant);

            var surfaces = GitOpsSurfaces.Resolve(req.Surfaces);
            var normalize = Normalizer();
            var temp = TempDir("plan");
            try
            {
                await PullAsync(g, temp, surfaces, ct);
                var repoFiles = GitOpsTree.ReadTree(req.RepoPath, surfaces, normalize);
                var liveFiles = GitOpsTree.ReadTree(temp, surfaces, normalize);
                var (rows, summary, liveHashes) = GitOpsTree.Diff(repoFiles, liveFiles);

                var generatedUtc = DateTime.UtcNow.ToString("o");
                var planId = "p_" + GitOpsTree.Sha256(
                    req.RepoPath + "|" + generatedUtc + "|" +
                    string.Join(",", liveHashes.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                                               .Select(kv => $"{kv.Key}={kv.Value}")))[..12];

                var stored = new GitOpsStoredPlan(
                    planId, req.RepoPath, manifest.TenantId ?? activeTenant,
                    surfaces.Select(s => s.Key).ToList(), liveHashes,
                    rows.ToDictionary(
                        r => $"{r.Surface.Key}|{r.FileName}",
                        r => new GitOpsStoredObject(r.Surface.Key, r.FileName, r.ObjectName, r.Verdict)));
                await GitOpsPlanStore.SaveAsync(stored, ct);

                var objects = rows
                    .Select(r => new GitOpsPlanObject(r.Surface.Key, r.Surface.ObjectType, r.ObjectName, r.Verdict, r.Changes))
                    .ToList();
                return Results.Ok(new GitOpsPlan(planId, req.RepoPath, generatedUtc, summary, objects));
            }
            finally { TryDelete(temp); }
        });

        // POST /gitops/apply — gated tree-scope write. confirm:true + planId (M6 rail).
        app.MapPost("/gitops/apply", async (GitOpsApplyRequest req, AuthSession auth, ISnapshotStore store, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            if (!req.Confirm)
                return ApiResults.BadRequest("confirm:true is required (M6 gate)");
            if (string.IsNullOrWhiteSpace(req.RepoPath) || string.IsNullOrWhiteSpace(req.PlanId))
                return ApiResults.BadRequest("repoPath and planId are required");

            var stored = await GitOpsPlanStore.LoadAsync(req.PlanId, ct);
            if (stored is null)
                return ApiResults.NotFound("unknown or expired planId — re-run plan");
            if (!string.Equals(stored.RepoPath, req.RepoPath, StringComparison.OrdinalIgnoreCase))
                return ApiResults.BadRequest("repoPath does not match the reviewed plan");

            var activeTenant = auth.ActiveProfile?.TenantId;
            if (TenantMismatch(stored.TenantId, activeTenant))
                return TenantConflict("reviewed plan", stored.TenantId, activeTenant);
            var manifest = await ReadManifestMetadataAsync(req.RepoPath, ct);
            if (TenantMismatch(manifest.TenantId, activeTenant))
                return TenantConflict("repo manifest", manifest.TenantId, activeTenant);
            if (TenantMismatch(manifest.TenantId, stored.TenantId))
                return Results.Conflict(new
                {
                    error = $"repo manifest targets tenant '{manifest.TenantId}', but the reviewed plan targets tenant '{stored.TenantId}'"
                });

            var surfaces = GitOpsSurfaces.Resolve(stored.Surfaces);
            var normalize = Normalizer();
            var actor = auth.ActiveProfile?.Name ?? "gitops";
            var idMaps = new Dictionary<string, Dictionary<string, string>>();
            var results = new List<GitOpsApplyObject>();
            int applied = 0, skipped = 0, conflict = 0;

            var temp = TempDir("apply");
            try
            {
                // Re-pull live for fresh per-object hashes → per-object conflict check.
                await PullAsync(g, temp, surfaces, ct);
                var liveFiles = GitOpsTree.ReadTree(temp, surfaces, normalize)
                    .ToDictionary(f => $"{f.Surface.Key}|{f.FileName}", f => f);

                foreach (var (key, obj) in stored.Objects)
                {
                    var def = GitOpsSurfaces.Find(obj.Surface);
                    if (def is null) { skipped++; results.Add(new(obj.ObjectName, "skipped", Reason: "unknown surface")); continue; }

                    // Conflict: has the live body for this object changed since plan?
                    stored.LiveHashes.TryGetValue(key, out var planHash);
                    var curHash = liveFiles.TryGetValue(key, out var lf) ? GitOpsTree.Sha256(lf.NormalizedBody) : null;
                    if (curHash != planHash)
                    {
                        conflict++;
                        results.Add(new(obj.ObjectName, "conflict", Reason: "live body changed since plan (hash mismatch)"));
                        continue;
                    }

                    if (!def.Writable)
                    {
                        skipped++;
                        results.Add(new(obj.ObjectName, "skipped", Reason: $"{def.Key} is read-only (locked guarantee)"));
                        continue;
                    }

                    var filePath = Path.Combine(req.RepoPath, def.Folder, obj.FileName);
                    try
                    {
                        switch (obj.Verdict)
                        {
                            case "Added":
                            {
                                var (newId, _) = await CreateAsync(g, def, filePath, ct);
                                var snapId = await SnapshotAsync(store, def, newId, obj.ObjectName, normalize(File.ReadAllText(filePath)), activeTenant, ct);
                                CacheInvalidation.OnWrite(cache, auth, def.CacheKey);
                                await AuditAsync(store, actor, "Create", def, newId, obj.ObjectName, activeTenant);
                                applied++;
                                results.Add(new(obj.ObjectName, "applied", SnapshotId: snapId, NewId: newId));
                                break;
                            }
                            case "Modified":
                            {
                                var liveId = await ResolveLiveIdAsync(g, def, obj.ObjectName, idMaps, ct);
                                if (liveId is null) { skipped++; results.Add(new(obj.ObjectName, "skipped", Reason: "live object not found for update")); break; }
                                var repoBody = normalize(File.ReadAllText(filePath));
                                var liveBody = lf?.NormalizedBody ?? "{}"; // lf from the conflict check above
                                await UpdateAsync(g, def, repoBody, liveBody, liveId, ct);
                                var snapId = await SnapshotAsync(store, def, liveId, obj.ObjectName, repoBody, activeTenant, ct);
                                CacheInvalidation.OnWrite(cache, auth, def.CacheKey, liveId);
                                await AuditAsync(store, actor, "Update", def, liveId, obj.ObjectName, activeTenant);
                                applied++;
                                results.Add(new(obj.ObjectName, "applied", SnapshotId: snapId, NewId: liveId));
                                break;
                            }
                            case "Removed":
                            {
                                if (!req.ConfirmDestroy) { skipped++; results.Add(new(obj.ObjectName, "skipped", Reason: "destroy not enabled (confirmDestroy)")); break; }
                                var liveId = await ResolveLiveIdAsync(g, def, obj.ObjectName, idMaps, ct);
                                if (liveId is null) { skipped++; results.Add(new(obj.ObjectName, "skipped", Reason: "live object not found for delete")); break; }
                                await DeleteAsync(g, def, liveId, ct);
                                CacheInvalidation.OnWrite(cache, auth, def.CacheKey, liveId);
                                await AuditAsync(store, actor, "Delete", def, liveId, obj.ObjectName, activeTenant);
                                applied++;
                                results.Add(new(obj.ObjectName, "applied", NewId: liveId));
                                break;
                            }
                            default:
                                skipped++;
                                results.Add(new(obj.ObjectName, "skipped", Reason: $"unknown verdict '{obj.Verdict}'"));
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        skipped++;
                        results.Add(new(obj.ObjectName, "skipped", Reason: "apply failed: " + ex.Message));
                    }
                }
            }
            finally { TryDelete(temp); }

            // One-shot: a consumed plan can't be replayed (forces a fresh plan).
            GitOpsPlanStore.Delete(req.PlanId);
            return Results.Ok(new GitOpsApplyResult(
                req.PlanId, DateTime.UtcNow.ToString("o"), results,
                new GitOpsApplySummary(applied, skipped, conflict)));
        });

        // GET /gitops/status — manifest vs live drift summary (in sync?).
        app.MapGet("/gitops/status", async (string? repoPath, AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            if (string.IsNullOrWhiteSpace(repoPath))
                return ApiResults.BadRequest("repoPath is required");
            var manifestPath = Path.Combine(repoPath, "manifest.json");
            if (!File.Exists(manifestPath))
                return ApiResults.BadRequest("no manifest.json in repoPath — pull first");

            // Scope status to the surfaces the manifest actually pulled — diffing a
            // device-configs-only repo against ALL surfaces would report every
            // compliance/CA/settings object as a phantom "destroy".
            var manifest = await ReadManifestMetadataAsync(repoPath, ct);
            var activeTenant = auth.ActiveProfile?.TenantId;
            if (TenantMismatch(manifest.TenantId, activeTenant))
                return TenantConflict("repo manifest", manifest.TenantId, activeTenant);

            var surfaces = manifest.Folders.Count > 0
                ? GitOpsSurfaces.All.Where(x => manifest.Folders.Contains(x.Folder)).ToList()
                : GitOpsSurfaces.All;
            var normalize = Normalizer();
            var temp = TempDir("status");
            try
            {
                await PullAsync(g, temp, surfaces, ct);
                var repoFiles = GitOpsTree.ReadTree(repoPath, surfaces, normalize);
                var liveFiles = GitOpsTree.ReadTree(temp, surfaces, normalize);
                var (_, summary, _) = GitOpsTree.Diff(repoFiles, liveFiles);
                var inSync = summary.Add == 0 && summary.Change == 0 && summary.Destroy == 0;
                return Results.Ok(new GitOpsStatus(repoPath, manifest.TenantId, inSync, summary));
            }
            finally { TryDelete(temp); }
        });
    }

    // ─── Graph wiring ─────────────────────────────────────────────────────────

    private static Func<string, string> Normalizer()
    {
        var norm = new ExportNormalizer();
        return norm.NormalizeJson;
    }

    private static string TempDir(string kind)
    {
        var d = Path.Combine(Path.GetTempPath(), $"cmpx-gitops-{kind}-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }

    private sealed record ManifestMetadata(string? TenantId, HashSet<string> Folders);

private static async Task<ManifestMetadata> ReadManifestMetadataAsync(string repoPath, CancellationToken ct)
{
    var manifestPath = Path.Combine(repoPath, "manifest.json");
    try
    {
        using var d = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, ct));
        string? tenantId = null;
        var folders = new HashSet<string>(StringComparer.Ordinal);
        if (d.RootElement.TryGetProperty("tenantId", out var t) && t.ValueKind == JsonValueKind.String)
            tenantId = t.GetString();
        if (d.RootElement.TryGetProperty("surfaces", out var s) && s.ValueKind == JsonValueKind.Object)
            foreach (var p in s.EnumerateObject()) folders.Add(p.Name);
        return new ManifestMetadata(tenantId, folders);
    }
    catch
    {
        // Block gitops operations when manifest.json exists but is unreadable/invalid.
        return new ManifestMetadata("manifest_unreadable", new HashSet<string>(StringComparer.Ordinal));
    }
}

    private static bool TenantMismatch(string? expectedTenant, string? activeTenant) =>
        !string.IsNullOrWhiteSpace(expectedTenant) &&
        !string.IsNullOrWhiteSpace(activeTenant) &&
        !string.Equals(expectedTenant, activeTenant, StringComparison.OrdinalIgnoreCase);

    private static IResult TenantConflict(string source, string? expectedTenant, string? activeTenant) =>
        Results.Conflict(new
        {
            error = $"{source} targets tenant '{expectedTenant}', but the active tenant is '{activeTenant}'"
        });

    // Export the selected surfaces to a normalized tree + migration-table.json.
    // One ExportService instance for the whole pull so filename de-dup is consistent.
    private static async Task PullAsync(
        Microsoft.Graph.Beta.GraphServiceClient g, string outRoot,
        IReadOnlyList<GitOpsSurfaceDef> surfaces, CancellationToken ct)
    {
        Directory.CreateDirectory(outRoot);
        var export = new ExportService();
        var mt = new MigrationTable();
        foreach (var def in surfaces)
            await PullSurfaceAsync(g, def, export, outRoot, mt, ct);
        await new ExportNormalizer().NormalizeDirectoryAsync(outRoot, ct);
        await export.SaveMigrationTableAsync(mt, outRoot, ct);
    }

    private static async Task PullSurfaceAsync(
        Microsoft.Graph.Beta.GraphServiceClient g, GitOpsSurfaceDef def,
        ExportService export, string outRoot, MigrationTable mt, CancellationToken ct)
    {
        switch (def.Key)
        {
            case "device-configs":
                foreach (var c in await new ConfigurationProfileService(g).ListDeviceConfigurationsAsync(ct))
                    await export.ExportDeviceConfigurationAsync(c, outRoot, mt, ct);
                break;
            case "compliance-policies":
                foreach (var p in await new CompliancePolicyService(g).ListCompliancePoliciesAsync(ct))
                    await export.ExportCompliancePolicyAsync(p, Array.Empty<DeviceCompliancePolicyAssignment>(), outRoot, mt, ct);
                break;
            case "settings-catalog":
            {
                var svc = new SettingsCatalogService(g);
                foreach (var p in await svc.ListSettingsCatalogPoliciesAsync(ct))
                {
                    IReadOnlyList<DeviceManagementConfigurationSetting> settings;
                    try { settings = (await svc.GetPolicySettingsAsync(p.Id!, ct)).ToList(); }
                    catch { settings = Array.Empty<DeviceManagementConfigurationSetting>(); }
                    await export.ExportSettingsCatalogPolicyAsync(
                        p, settings, Array.Empty<DeviceManagementConfigurationPolicyAssignment>(), outRoot, mt, ct);
                }
                break;
            }
            case "conditional-access":
            {
                // GUID-resolved export: rewrite user/group/role/app/location GUIDs to
                // display names so the repo is portable + readable (the doc's
                // WithResolvedGuids). Names are batch-resolved once for all policies.
                var policies = await new ConditionalAccessPolicyService(g).ListPoliciesAsync(ct);
                var nameLookup = await BuildCaNameLookupAsync(g, policies, ct);
                foreach (var p in policies)
                    await export.ExportConditionalAccessPolicyWithResolvedGuidsAsync(p, outRoot, mt, nameLookup, ct);
                break;
            }
        }
    }

    // Build the GUID -> display-name map a Conditional Access export resolves against:
    // directory objects (users/groups/roles/apps) via getByIds, plus named locations.
    // Sentinels ("All"/"None"/"GuestsOrExternalUsers"/…) are non-GUIDs and skipped;
    // everything is best-effort (unresolved IDs stay as GUIDs in the file).
    private static async Task<IReadOnlyDictionary<string, string>> BuildCaNameLookupAsync(
        Microsoft.Graph.Beta.GraphServiceClient g, IEnumerable<ConditionalAccessPolicy> policies, CancellationToken ct)
    {
        var dirIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in policies)
        {
            var c = p.Conditions;
            if (c is null) continue;
            AddGuids(dirIds, c.Users?.IncludeUsers, c.Users?.ExcludeUsers,
                c.Users?.IncludeGroups, c.Users?.ExcludeGroups,
                c.Users?.IncludeRoles, c.Users?.ExcludeRoles,
                c.Applications?.IncludeApplications, c.Applications?.ExcludeApplications);
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (dirIds.Count > 0)
        {
            try
            {
                foreach (var kv in await new DirectoryObjectResolver(g).ResolveAsync(dirIds, ct))
                    map[kv.Key] = kv.Value;
            }
            catch { /* best-effort — leave unresolved GUIDs in place */ }
        }
        try
        {
            foreach (var loc in await new NamedLocationService(g).ListNamedLocationsAsync(ct))
                if (loc.Id is not null && loc.DisplayName is not null) map[loc.Id] = loc.DisplayName;
        }
        catch { /* best-effort */ }
        return map;
    }

    private static void AddGuids(HashSet<string> set, params List<string>?[] lists)
    {
        foreach (var list in lists)
            if (list is not null)
                foreach (var id in list)
                    if (!string.IsNullOrWhiteSpace(id) && Guid.TryParse(id, out _)) set.Add(id);
    }

    // displayName → id for a writable surface (used to locate the live object an
    // update/delete targets). Built lazily, cached per apply.
    private static async Task<Dictionary<string, string>> ListLiveNamesAsync(
        Microsoft.Graph.Beta.GraphServiceClient g, GitOpsSurfaceDef def, CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        switch (def.Key)
        {
            case "device-configs":
                foreach (var x in await new ConfigurationProfileService(g).ListDeviceConfigurationsAsync(ct))
                    if (x.Id is not null && x.DisplayName is not null) map[x.DisplayName] = x.Id;
                break;
            case "compliance-policies":
                foreach (var x in await new CompliancePolicyService(g).ListCompliancePoliciesAsync(ct))
                    if (x.Id is not null && x.DisplayName is not null) map[x.DisplayName] = x.Id;
                break;
            case "settings-catalog":
                foreach (var x in await new SettingsCatalogService(g).ListSettingsCatalogPoliciesAsync(ct))
                    if (x.Id is not null && x.Name is not null) map[x.Name] = x.Id;
                break;
        }
        return map;
    }

    private static async Task<string?> ResolveLiveIdAsync(
        Microsoft.Graph.Beta.GraphServiceClient g, GitOpsSurfaceDef def, string name,
        Dictionary<string, Dictionary<string, string>> cache, CancellationToken ct)
    {
        if (!cache.TryGetValue(def.Key, out var map))
        {
            map = await ListLiveNamesAsync(g, def, ct);
            cache[def.Key] = map;
        }
        return map.TryGetValue(name, out var id) ? id : null;
    }

    // ── The apply-write model ────────────────────────────────────────────────
    // apply-Added   -> CreateAsync = ImportService.Import<T>Async + MigrationTable
    //                  (the doc's "push": create-on-import, source ids remapped).
    // apply-Modified -> UpdateAsync = Core update with a MINIMAL patch. ImportService
    //                  is create-only (it has no Import-update), so updates can't go
    //                  through it; and a full-object PATCH would re-send null-on-read /
    //                  non-nullable fields Graph rejects (e.g. supportsScopeTags). The
    //                  minimal patch (apply-the-diff) is the correct seam for updates.
    // apply-Removed  -> DeleteAsync via Core (gated behind confirmDestroy).

    // create-on-import (ImportService rewrites source ids through the migration table).
    private static async Task<(string Id, string Name)> CreateAsync(
        Microsoft.Graph.Beta.GraphServiceClient g, GitOpsSurfaceDef def, string filePath, CancellationToken ct)
    {
        var import = BuildImport(g);
        var mt = new MigrationTable();
        switch (def.Key)
        {
            case "device-configs":
            {
                var model = await import.ReadDeviceConfigurationAsync(filePath, ct)
                    ?? throw new InvalidOperationException("unreadable device-config file");
                var created = await import.ImportDeviceConfigurationAsync(model, mt, ct);
                return (created.Id ?? "", created.DisplayName ?? "");
            }
            case "compliance-policies":
            {
                var ex = await import.ReadCompliancePolicyAsync(filePath, ct)
                    ?? throw new InvalidOperationException("unreadable compliance-policy file");
                var created = await import.ImportCompliancePolicyAsync(ex, mt, ct);
                return (created.Id ?? "", created.DisplayName ?? "");
            }
            case "settings-catalog":
            {
                var ex = await import.ReadSettingsCatalogPolicyAsync(filePath, ct)
                    ?? throw new InvalidOperationException("unreadable settings-catalog file");
                var created = await import.ImportSettingsCatalogPolicyAsync(ex, mt, ct);
                return (created.Id ?? "", created.Name ?? "");
            }
            default:
                throw new InvalidOperationException($"{def.Key} is not writable");
        }
    }

    // Modify an existing object with a MINIMAL patch — only the top-level fields that
    // differ from live (GitOps "apply the diff"). The minimal body is parsed back
    // through Kiota (so @odata.type resolves the subtype), avoiding the full-object
    // PATCH that would re-send null read-only / non-nullable fields Graph rejects.
    private static async Task UpdateAsync(
        Microsoft.Graph.Beta.GraphServiceClient g, GitOpsSurfaceDef def,
        string repoBodyJson, string liveBodyJson, string liveId, CancellationToken ct)
    {
        switch (def.Key)
        {
            case "device-configs":
            {
                var patch = GitOpsTree.BuildMinimalPatch(repoBodyJson, liveBodyJson);
                if (patch is null) return; // nothing but the discriminator changed
                var model = await KiotaJsonSerializer.DeserializeAsync<DeviceConfiguration>(patch, ct)
                    ?? throw new InvalidOperationException("could not parse minimal patch");
                CrudJson.MakeWriteReady(model);
                model.Id = liveId;
                await new ConfigurationProfileService(g).UpdateDeviceConfigurationAsync(model, ct);
                break;
            }
            case "compliance-policies":
            {
                var patch = GitOpsTree.BuildMinimalPatch(repoBodyJson, liveBodyJson);
                if (patch is null) return;
                var model = await KiotaJsonSerializer.DeserializeAsync<DeviceCompliancePolicy>(patch, ct)
                    ?? throw new InvalidOperationException("could not parse minimal patch");
                CrudJson.MakeWriteReady(model);
                model.Id = liveId;
                await new CompliancePolicyService(g).UpdateCompliancePolicyAsync(model, ct);
                break;
            }
            case "settings-catalog":
            {
                // The settings-catalog file is a wrapper {policy,settings,assignments};
                // metadata update operates on the inner policy object.
                var repoPolicy = (System.Text.Json.Nodes.JsonNode.Parse(repoBodyJson) as System.Text.Json.Nodes.JsonObject)?["policy"]?.ToJsonString();
                var livePolicy = (System.Text.Json.Nodes.JsonNode.Parse(liveBodyJson) as System.Text.Json.Nodes.JsonObject)?["policy"]?.ToJsonString() ?? "{}";
                if (repoPolicy is null) return;
                var patch = GitOpsTree.BuildMinimalPatch(repoPolicy, livePolicy);
                if (patch is null) return;
                var model = await KiotaJsonSerializer.DeserializeAsync<DeviceManagementConfigurationPolicy>(patch, ct)
                    ?? throw new InvalidOperationException("could not parse minimal patch");
                CrudJson.MakeWriteReady(model);
                await new SettingsCatalogService(g).UpdateSettingsCatalogPolicyMetadataAsync(liveId, model, ct);
                break;
            }
            default:
                throw new InvalidOperationException($"{def.Key} is not writable");
        }
    }

    private static async Task DeleteAsync(
        Microsoft.Graph.Beta.GraphServiceClient g, GitOpsSurfaceDef def, string liveId, CancellationToken ct)
    {
        switch (def.Key)
        {
            case "device-configs":
                await new ConfigurationProfileService(g).DeleteDeviceConfigurationAsync(liveId, ct);
                break;
            case "compliance-policies":
                await new CompliancePolicyService(g).DeleteCompliancePolicyAsync(liveId, ct);
                break;
            case "settings-catalog":
                await new SettingsCatalogService(g).DeleteSettingsCatalogPolicyAsync(liveId, ct);
                break;
            default:
                throw new InvalidOperationException($"{def.Key} is not writable");
        }
    }

    // ImportService needs only the Core services for the writable surfaces.
    private static ImportService BuildImport(Microsoft.Graph.Beta.GraphServiceClient g) =>
        new(
            new ConfigurationProfileService(g),
            compliancePolicyService: new CompliancePolicyService(g),
            settingsCatalogService: new SettingsCatalogService(g));

    // Snapshot-on-write — the apply IS a snapshot generation (history is free).
    private static async Task<string?> SnapshotAsync(
        ISnapshotStore store, GitOpsSurfaceDef def, string objectId, string name, string body,
        string? tenantId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(objectId)) return null;
        var rec = new ConfigSnapshotRecord(
            Guid.NewGuid().ToString("n"), objectId, def.ObjectType, name, DateTime.UtcNow, body,
            TenantId: tenantId);
        var stored = await store.AppendSnapshotIfChangedAsync(rec, ct);
        if (stored is not null) await store.CommitIndexAsync();
        return stored?.SnapshotId;
    }

    private static async Task AuditAsync(
        ISnapshotStore store, string actor, string verb, GitOpsSurfaceDef def, string objectId, string name,
        string? tenantId)
    {
        await store.AppendAuditEventAsync(new AuditEventRecord(
            Guid.NewGuid().ToString("n"), DateTime.UtcNow, actor,
            $"{verb} (GitOps)", def.ObjectType, objectId, name, tenantId));
        await store.CommitIndexAsync();
    }
}
