// DeviceScripts endpoint group — ported IntuneCommander LIVE surfaces (scripts,
// device categories, Windows update rings, managed-device inventory). Lives in its
// own module (not Program.cs) so it can name Microsoft.Graph.Beta.Models types for
// CRUD body binding (CrudJson.ReadModelAsync<T>) — a Graph `using` in Program.cs
// shadows IResult and breaks Minimal API overload resolution.
//
// Each surface follows the shared shape: GET /{tag} (list -> ListItemDto, 409 when
// signed out), GET /{tag}/{id} (raw Graph JSON, 404 if null), and for CRUD surfaces
// POST/PATCH/DELETE. List projections mirror the existing /device-configs etc.
// examples in Program.cs. 409 (Conflict) = signed out (auth.Graph is null).
using System.Text.Json;
using Microsoft.Graph.Beta.Models;
using Intune.Commander.Core.Services;
using CmProjectX.Store;

namespace CmProjectX.Api;

public static class DeviceScriptsEndpoints
{
    public static void MapDeviceScripts(this Microsoft.AspNetCore.Builder.WebApplication app)
    {
        // ─── remediation-scripts (DeviceHealthScript) — CRUD ──────────────────
        app.MapGet("/remediation-scripts", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "DeviceHealthScripts", ct => new DeviceHealthScriptService(g).ListDeviceHealthScriptsAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(x.Id ?? "", x.DisplayName ?? "(unnamed)", string.Join(" · ", new[] { x.OdataType?.Split('.').LastOrDefault()?.Replace("microsoftGraph", "") ?? "deviceHealthScript", string.IsNullOrEmpty(x.Publisher) ? null : x.Publisher, string.IsNullOrEmpty(x.Version) ? null : "v" + x.Version }.Where(s => !string.IsNullOrEmpty(s))), x.IsGlobalScript == true ? "global" : null)));
        });
        app.MapGet("/remediation-scripts/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "DeviceHealthScripts", id, ct => new DeviceHealthScriptService(g).GetDeviceHealthScriptAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });
        app.MapPost("/remediation-scripts", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<DeviceHealthScript>(req, ct);
            var created = await new DeviceHealthScriptService(g).CreateDeviceHealthScriptAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "DeviceHealthScripts");
            return Results.Ok(new { id = created.Id });
        });
        app.MapPatch("/remediation-scripts/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<DeviceHealthScript>(req, ct);
            model.Id = id;
            await new DeviceHealthScriptService(g).UpdateDeviceHealthScriptAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "DeviceHealthScripts", id);
            return Results.NoContent();
        });
        app.MapDelete("/remediation-scripts/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new DeviceHealthScriptService(g).DeleteDeviceHealthScriptAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "DeviceHealthScripts", id);
            return Results.NoContent();
        });

        // ─── Detection & Remediation — run reporting + on-demand run ──────────
        // Volatile reports go STRAIGHT to Graph (no cache); the on-demand run is a
        // device action gated by the same env kill-switch + destructive opt-in as M14.

        // GET /remediation-scripts/{id}/run-summary — proactive-remediation counts as tiles.
        app.MapGet("/remediation-scripts/{id}/run-summary", async (string id, AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var s = await new DeviceHealthScriptService(g).GetRunSummaryAsync(id, ct);
            if (s is null) return Results.Ok(Array.Empty<ListItemDto>());
            ListItemDto Tile(string label, int? n) => new(label, label, (n ?? 0).ToString(), null);
            var tiles = new[]
            {
                Tile("No issue", s.NoIssueDetectedDeviceCount),
                Tile("Issue detected", s.IssueDetectedDeviceCount),
                Tile("Remediated", s.IssueRemediatedDeviceCount),
                Tile("Detection errors", s.DetectionScriptErrorDeviceCount),
                Tile("Detection pending", s.DetectionScriptPendingDeviceCount),
                Tile("Remediation errors", s.RemediationScriptErrorDeviceCount),
                Tile("Remediation skipped", s.RemediationSkippedDeviceCount),
            };
            return Results.Ok(tiles);
        });

        // GET /remediation-scripts/{id}/device-states — per-device detection/remediation state.
        app.MapGet("/remediation-scripts/{id}/device-states", async (string id, AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var states = await new DeviceHealthScriptService(g).GetDeviceRunStatesAsync(id, ct);
            var rows = states.Select(x => new DeviceRunStateDto(
                x.ManagedDevice?.Id ?? "",
                x.ManagedDevice?.DeviceName,
                x.DetectionState?.ToString(),
                x.RemediationState?.ToString(),
                x.LastStateUpdateDateTime?.ToString("o"))).ToList();
            return Results.Ok(rows);
        });

        // GET /remediation-scripts/{id}/scripts — decoded detection + remediation PowerShell
        // for the code viewer. Pure read (no gating). Served through the warm cache like the
        // detail GET (script content is version-stable; CRUD writes already evict it).
        app.MapGet("/remediation-scripts/{id}/scripts", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var s = await CachedReader.GetAsync(cache, auth, "DeviceHealthScripts", id, ct => new DeviceHealthScriptService(g).GetDeviceHealthScriptAsync(id, ct), ct);
            if (s is null) return Results.NotFound();
            static string? Decode(byte[]? b) => b is { Length: > 0 } ? System.Text.Encoding.UTF8.GetString(b) : null;
            return Results.Ok(new RemediationScriptContentDto(
                s.Id ?? id, s.DisplayName, Decode(s.DetectionScriptContent), Decode(s.RemediationScriptContent)));
        });

        // POST /remediation-scripts/{id}/run/{deviceId} — on-demand remediation on one
        // device. Gated like M14 destructive: the ACTIONS_DISABLED kill switch + the
        // DESTRUCTIVE opt-in + a confirm flag (the script runs on the device).
        app.MapPost("/remediation-scripts/{id}/run/{deviceId}", async (string id, string deviceId, RunRemediationRequest? body, AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            if (ActionsDisabled()) return ApiResults.Forbidden("device actions are disabled (CMPROJECTX_DEVICE_ACTIONS_DISABLED)");
            if (!DestructiveEnabled()) return ApiResults.Forbidden("on-demand remediation runs a script on the device — set CMPROJECTX_DEVICE_ACTIONS_DESTRUCTIVE=1 to allow");
            if (body?.Confirm != true) return ApiResults.BadRequest("confirm:true required (this runs the remediation script on the device now)");
            await new DeviceHealthScriptService(g).InitiateOnDemandRemediationAsync(deviceId, id, ct);
            return Results.NoContent();
        });

        // ─── compliance-scripts (DeviceComplianceScript) — CRUD ───────────────
        app.MapGet("/compliance-scripts", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "ComplianceScripts", ct => new ComplianceScriptService(g).ListComplianceScriptsAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(x.Id ?? "", x.DisplayName ?? "(unnamed)", !string.IsNullOrEmpty(x.Publisher) ? x.Publisher! : (x.OdataType?.Split('.').LastOrDefault()?.Replace("microsoftGraph", "") ?? ""), string.IsNullOrEmpty(x.Version) ? null : ("v" + x.Version))));
        });
        app.MapGet("/compliance-scripts/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "ComplianceScripts", id, ct => new ComplianceScriptService(g).GetComplianceScriptAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });
        app.MapPost("/compliance-scripts", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<DeviceComplianceScript>(req, ct);
            var created = await new ComplianceScriptService(g).CreateComplianceScriptAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "ComplianceScripts");
            return Results.Ok(new { id = created.Id });
        });
        app.MapPatch("/compliance-scripts/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<DeviceComplianceScript>(req, ct);
            model.Id = id;
            await new ComplianceScriptService(g).UpdateComplianceScriptAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "ComplianceScripts", id);
            return Results.NoContent();
        });
        app.MapDelete("/compliance-scripts/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new ComplianceScriptService(g).DeleteComplianceScriptAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "ComplianceScripts", id);
            return Results.NoContent();
        });

        // ─── platform-scripts (DeviceManagementScript) — CRUD ─────────────────
        app.MapGet("/platform-scripts", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "DeviceManagementScripts", ct => new DeviceManagementScriptService(g).ListDeviceManagementScriptsAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(x.Id ?? "", x.DisplayName ?? "(unnamed script)", string.Join(" · ", new[] { x.OdataType?.Split('.').LastOrDefault()?.Replace("microsoftGraph", "") ?? "deviceManagementScript", x.FileName, x.RunAsAccount?.ToString() }.Where(s => !string.IsNullOrEmpty(s))), (x.Assignments?.Count ?? 0) > 0 ? "assigned" : null)));
        });
        app.MapGet("/platform-scripts/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "DeviceManagementScripts", id, ct => new DeviceManagementScriptService(g).GetDeviceManagementScriptAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });
        app.MapPost("/platform-scripts", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<DeviceManagementScript>(req, ct);
            var created = await new DeviceManagementScriptService(g).CreateDeviceManagementScriptAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "DeviceManagementScripts");
            return Results.Ok(new { id = created.Id });
        });
        app.MapPatch("/platform-scripts/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<DeviceManagementScript>(req, ct);
            model.Id = id;
            await new DeviceManagementScriptService(g).UpdateDeviceManagementScriptAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "DeviceManagementScripts", id);
            return Results.NoContent();
        });
        app.MapDelete("/platform-scripts/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new DeviceManagementScriptService(g).DeleteDeviceManagementScriptAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "DeviceManagementScripts", id);
            return Results.NoContent();
        });

        // ─── shell-scripts (DeviceShellScript) — CRUD ─────────────────────────
        app.MapGet("/shell-scripts", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "DeviceShellScripts", ct => new DeviceShellScriptService(g).ListDeviceShellScriptsAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(x.Id ?? "", x.DisplayName ?? "(unnamed)", !string.IsNullOrEmpty(x.FileName) ? x.FileName : (x.RunAsAccount != null ? $"Run as {x.RunAsAccount}" : (x.OdataType?.Split('.').LastOrDefault() ?? "shell script")), x.RunAsAccount?.ToString())));
        });
        app.MapGet("/shell-scripts/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "DeviceShellScripts", id, ct => new DeviceShellScriptService(g).GetDeviceShellScriptAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });
        app.MapPost("/shell-scripts", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<DeviceShellScript>(req, ct);
            var created = await new DeviceShellScriptService(g).CreateDeviceShellScriptAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "DeviceShellScripts");
            return Results.Ok(new { id = created.Id });
        });
        app.MapPatch("/shell-scripts/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<DeviceShellScript>(req, ct);
            model.Id = id;
            await new DeviceShellScriptService(g).UpdateDeviceShellScriptAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "DeviceShellScripts", id);
            return Results.NoContent();
        });
        app.MapDelete("/shell-scripts/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new DeviceShellScriptService(g).DeleteDeviceShellScriptAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "DeviceShellScripts", id);
            return Results.NoContent();
        });

        // ─── mac-custom-attributes (DeviceCustomAttributeShellScript) — CRUD ──
        // Full CRUD in Core (MacCustomAttributeService) but no assign method, so
        // writable + non-assignable (mirrors the M14 inventory fold-in).
        app.MapGet("/mac-custom-attributes", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "MacCustomAttributes", ct => new MacCustomAttributeService(g).ListMacCustomAttributesAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(x.Id ?? "", x.DisplayName ?? "(unnamed)", string.Join(" · ", new[] { x.CustomAttributeType?.ToString(), x.FileName, x.RunAsAccount?.ToString() }.Where(s => !string.IsNullOrEmpty(s))), x.CustomAttributeName)));
        });
        app.MapGet("/mac-custom-attributes/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "MacCustomAttributes", id, ct => new MacCustomAttributeService(g).GetMacCustomAttributeAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });
        app.MapPost("/mac-custom-attributes", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<DeviceCustomAttributeShellScript>(req, ct);
            var created = await new MacCustomAttributeService(g).CreateMacCustomAttributeAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "MacCustomAttributes");
            return Results.Ok(new { id = created.Id });
        });
        app.MapPatch("/mac-custom-attributes/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<DeviceCustomAttributeShellScript>(req, ct);
            model.Id = id;
            await new MacCustomAttributeService(g).UpdateMacCustomAttributeAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "MacCustomAttributes", id);
            return Results.NoContent();
        });
        app.MapDelete("/mac-custom-attributes/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new MacCustomAttributeService(g).DeleteMacCustomAttributeAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "MacCustomAttributes", id);
            return Results.NoContent();
        });

        // ─── device-categories (DeviceCategory) — CRUD ────────────────────────
        app.MapGet("/device-categories", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "DeviceCategories", ct => new DeviceCategoryService(g).ListDeviceCategoriesAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(x.Id ?? "", x.DisplayName ?? "(unnamed)", !string.IsNullOrWhiteSpace(x.Description) ? x.Description! : (x.OdataType?.Split('.').LastOrDefault()?.Replace("microsoftGraph", "") ?? "deviceCategory"), x.RoleScopeTagIds?.Any(t => t != "0") == true ? "scoped" : null)));
        });
        app.MapGet("/device-categories/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "DeviceCategories", id, ct => new DeviceCategoryService(g).GetDeviceCategoryAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });
        app.MapPost("/device-categories", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<DeviceCategory>(req, ct);
            var created = await new DeviceCategoryService(g).CreateDeviceCategoryAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "DeviceCategories");
            return Results.Ok(new { id = created.Id });
        });
        app.MapPatch("/device-categories/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<DeviceCategory>(req, ct);
            model.Id = id;
            await new DeviceCategoryService(g).UpdateDeviceCategoryAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "DeviceCategories", id);
            return Results.NoContent();
        });
        app.MapDelete("/device-categories/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new DeviceCategoryService(g).DeleteDeviceCategoryAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "DeviceCategories", id);
            return Results.NoContent();
        });

        // ─── feature-updates (WindowsFeatureUpdateProfile) — CRUD ─────────────
        app.MapGet("/feature-updates", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "FeatureUpdateProfiles", ct => new FeatureUpdateProfileService(g).ListFeatureUpdateProfilesAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(x.Id ?? "", x.DisplayName ?? "(unnamed)", !string.IsNullOrEmpty(x.FeatureUpdateVersion) ? $"Windows {x.FeatureUpdateVersion}" : (x.OdataType?.Split('.').LastOrDefault()?.Replace("microsoftGraph", "") ?? "Feature update"), x.EndOfSupportDate.HasValue && x.EndOfSupportDate.Value < DateTimeOffset.UtcNow ? "EOS" : null)));
        });
        app.MapGet("/feature-updates/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "FeatureUpdateProfiles", id, ct => new FeatureUpdateProfileService(g).GetFeatureUpdateProfileAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });
        app.MapPost("/feature-updates", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<WindowsFeatureUpdateProfile>(req, ct);
            var created = await new FeatureUpdateProfileService(g).CreateFeatureUpdateProfileAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "FeatureUpdateProfiles");
            return Results.Ok(new { id = created.Id });
        });
        app.MapPatch("/feature-updates/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<WindowsFeatureUpdateProfile>(req, ct);
            model.Id = id;
            await new FeatureUpdateProfileService(g).UpdateFeatureUpdateProfileAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "FeatureUpdateProfiles", id);
            return Results.NoContent();
        });
        app.MapDelete("/feature-updates/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new FeatureUpdateProfileService(g).DeleteFeatureUpdateProfileAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "FeatureUpdateProfiles", id);
            return Results.NoContent();
        });

        // ─── quality-updates (WindowsQualityUpdateProfile) — CRUD ─────────────
        app.MapGet("/quality-updates", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "QualityUpdateProfiles", ct => new QualityUpdateProfileService(g).ListQualityUpdateProfilesAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(x.Id ?? "", x.DisplayName ?? "(unnamed)", x.ReleaseDateDisplayName ?? x.DeployableContentDisplayName ?? x.OdataType?.Split('.').LastOrDefault()?.Replace("microsoftGraph", "") ?? "", x.Assignments != null && x.Assignments.Count > 0 ? "assigned" : null)));
        });
        app.MapGet("/quality-updates/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "QualityUpdateProfiles", id, ct => new QualityUpdateProfileService(g).GetQualityUpdateProfileAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });
        app.MapPost("/quality-updates", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<WindowsQualityUpdateProfile>(req, ct);
            var created = await new QualityUpdateProfileService(g).CreateQualityUpdateProfileAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "QualityUpdateProfiles");
            return Results.Ok(new { id = created.Id });
        });
        app.MapPatch("/quality-updates/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<WindowsQualityUpdateProfile>(req, ct);
            model.Id = id;
            await new QualityUpdateProfileService(g).UpdateQualityUpdateProfileAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "QualityUpdateProfiles", id);
            return Results.NoContent();
        });
        app.MapDelete("/quality-updates/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new QualityUpdateProfileService(g).DeleteQualityUpdateProfileAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "QualityUpdateProfiles", id);
            return Results.NoContent();
        });

        // ─── driver-updates (WindowsDriverUpdateProfile) — CRUD ───────────────
        app.MapGet("/driver-updates", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "DriverUpdateProfiles", ct => new DriverUpdateProfileService(g).ListDriverUpdateProfilesAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(x.Id ?? "", x.DisplayName ?? "(unnamed)", (x.ApprovalType?.ToString() ?? x.OdataType?.Split('.').LastOrDefault()?.Replace("microsoftGraph", "") ?? "driver update") + " - " + (x.NewUpdates?.ToString() ?? "0") + " new", x.Assignments != null && x.Assignments.Count > 0 ? "assigned" : null)));
        });
        app.MapGet("/driver-updates/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "DriverUpdateProfiles", id, ct => new DriverUpdateProfileService(g).GetDriverUpdateProfileAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });
        app.MapPost("/driver-updates", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<WindowsDriverUpdateProfile>(req, ct);
            var created = await new DriverUpdateProfileService(g).CreateDriverUpdateProfileAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "DriverUpdateProfiles");
            return Results.Ok(new { id = created.Id });
        });
        app.MapPatch("/driver-updates/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<WindowsDriverUpdateProfile>(req, ct);
            model.Id = id;
            await new DriverUpdateProfileService(g).UpdateDriverUpdateProfileAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "DriverUpdateProfiles", id);
            return Results.NoContent();
        });
        app.MapDelete("/driver-updates/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new DriverUpdateProfileService(g).DeleteDriverUpdateProfileAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "DriverUpdateProfiles", id);
            return Results.NoContent();
        });

        // ─── managed-devices (ManagedDevice) — list + detail + actions (M14) ──
        // Devices aren't authored (no config CRUD); they're *acted on*. The list stays
        // read-only; M14 adds GET /{id} detail and the Pattern-F action verbs below.
        app.MapGet("/managed-devices", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "ManagedDevices", ct => new ManagedDeviceService(g).ListManagedDevicesAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(x.Id ?? "", x.DeviceName ?? "(unnamed)", string.Join(" • ", new[] { x.OperatingSystem, x.OsVersion, x.Model }.Where(s => !string.IsNullOrEmpty(s))), x.ComplianceState?.ToString())));
        });
        app.MapGet("/managed-devices/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "ManagedDevices", id, ct => new ManagedDeviceService(g).GetManagedDeviceAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });

        // The action catalog — drives the client's action buttons + confirm dialogs.
        app.MapGet("/managed-devices/actions", () =>
            Results.Ok(DeviceActionCatalog.Actions.Values.Select(a => new DeviceActionInfoDto(a.Id, a.DisplayName, a.Destructive, a.Destructive))));

        // Device action HISTORY (Pattern F read). Volatile — straight to Graph, NO blob
        // cache (mirrors AssignmentExplorerEndpoints). Projects Graph deviceActionResult →
        // DeviceActionRecord rows for the client's history table. `actor` is null: Graph's
        // deviceActionResults carry no initiator, it's reserved for a future audit join.
        app.MapGet("/managed-devices/{id}/actions", async (string id, AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var results = await new ManagedDeviceService(g).GetDeviceActionResultsAsync(id, ct);
            var rows = results.Select((r, i) => new DeviceActionRecord(
                $"{r.ActionName ?? "action"}#{i}",
                r.ActionName ?? "",
                r.ActionState?.ToString(),
                r.StartDateTime?.ToString("o"),
                r.LastUpdatedDateTime?.ToString("o"),
                null)).ToList();
            return Results.Ok(rows);
        });

        // Run one action against one device. Rails, in order (cheapest first): global
        // kill-switch → known verb → org allowlist (destructive) → scope-check (framed
        // 403) → typed-confirm == device name (destructive). Dispatches the EffectiveVerb
        // (autopilotReset → wipe), audits, evicts the detail cache.
        app.MapPost("/managed-devices/{id}/actions/{action}", async (string id, string action, HttpRequest req, AuthSession auth, ICacheService cache, ISnapshotStore store, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var cfg = DeviceActionGate.FromEnvironment(DeviceActionCatalog.DestructiveVerbIds);
            if (cfg.GlobalKill) return ApiResults.Forbidden("device actions are disabled (CMPROJECTX_DEVICE_ACTIONS_DISABLED)");
            if (!DeviceActionCatalog.Actions.TryGetValue(action, out var meta)) return ApiResults.BadRequest($"unknown device action '{action}'");

            if (meta.Destructive && !DeviceActionGate.IsDestructiveVerbEnabled(cfg, meta.Id))
                return ApiResults.Forbidden($"destructive action '{meta.Id}' is disabled by org policy (enable CMPROJECTX_DEVICE_ACTION_{meta.Id.ToUpperInvariant()}=1, or CMPROJECTX_DEVICE_ACTIONS_DESTRUCTIVE=1 for all)");

            var scopeGap = await CheckDeviceActionScopeAsync(auth, meta.Destructive, ct);
            if (scopeGap is not null) return scopeGap;

            var body = await ReadActionBodyAsync(req, ct);
            string? deviceName = null;
            if (meta.Destructive)
            {
                var dev = await new ManagedDeviceService(g).GetManagedDeviceAsync(id, ct);
                if (dev is null) return Results.NotFound();
                deviceName = dev.DeviceName;
                if (!DeviceActionGate.SingleConfirmMatches(body?.Confirm, deviceName))
                    return ApiResults.BadRequest($"destructive action requires confirm == device name (\"{deviceName}\")");
            }

            await new ManagedDeviceService(g).ExecuteDeviceActionAsync(id, meta.EffectiveVerb, BodyJson(meta, body), ct);
            await store.AppendAuditEventAsync(new AuditEventRecord(
                Guid.NewGuid().ToString("n"), DateTime.UtcNow, auth.ActiveProfile?.Name ?? "operator",
                $"Device action: {meta.Id}", "managedDevice", id, deviceName,
                auth.ActiveProfile?.TenantId));
            await store.CommitIndexAsync();
            CacheInvalidation.OnWrite(cache, auth, "ManagedDevices", id);
            return Results.NoContent();
        });

        // Run one action against many devices. Reversible bulk is capped at 200; a
        // destructive bulk needs BOTH the per-verb allowlist AND the separate
        // CMPROJECTX_DEVICE_ACTIONS_BULK_DESTRUCTIVE opt-in, is capped at 25, and requires
        // the case-SENSITIVE sweep phrase "{verb} {count}". Per-device results make partial
        // failures visible; each success emits its own append-only audit row.
        app.MapPost("/managed-devices/actions/{action}", async (string action, HttpRequest req, AuthSession auth, ICacheService cache, ISnapshotStore store, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var cfg = DeviceActionGate.FromEnvironment(DeviceActionCatalog.DestructiveVerbIds);
            if (cfg.GlobalKill) return ApiResults.Forbidden("device actions are disabled (CMPROJECTX_DEVICE_ACTIONS_DISABLED)");
            if (!DeviceActionCatalog.Actions.TryGetValue(action, out var meta)) return ApiResults.BadRequest($"unknown device action '{action}'");

            var body = await req.ReadFromJsonAsync<BulkDeviceActionRequest>(cancellationToken: ct);
            var ids = body?.DeviceIds?.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList() ?? new();
            if (ids.Count == 0) return ApiResults.BadRequest("deviceIds is required");

            if (meta.Destructive)
            {
                if (!DeviceActionGate.IsDestructiveVerbEnabled(cfg, meta.Id))
                    return ApiResults.Forbidden($"destructive action '{meta.Id}' is disabled by org policy (enable CMPROJECTX_DEVICE_ACTION_{meta.Id.ToUpperInvariant()}=1, or CMPROJECTX_DEVICE_ACTIONS_DESTRUCTIVE=1 for all)");
                if (!cfg.BulkDestructive)
                    return ApiResults.Forbidden("destructive bulk actions are disabled (set CMPROJECTX_DEVICE_ACTIONS_BULK_DESTRUCTIVE=1 to allow)");
            }

            var cap = DeviceActionGate.BulkCapFor(cfg, meta.Destructive);
            if (ids.Count > cap)
                return ApiResults.BadRequest($"selection of {ids.Count} exceeds the {(meta.Destructive ? "destructive" : "reversible")} bulk cap of {cap}");

            if (meta.Destructive && !DeviceActionGate.BulkConfirmMatches(body?.Confirm, meta.Id, ids.Count))
                return ApiResults.BadRequest($"destructive bulk action requires confirm == \"{DeviceActionGate.BulkConfirmToken(meta.Id, ids.Count)}\"");

            var scopeGap = await CheckDeviceActionScopeAsync(auth, meta.Destructive, ct);
            if (scopeGap is not null) return scopeGap;

            var actor = auth.ActiveProfile?.Name ?? "operator";
            var bodyJson = BodyJson(meta, new DeviceActionRequest(null, body?.Parameters));
            var svc = new ManagedDeviceService(g);
            var results = new List<BulkDeviceActionResultDto>(ids.Count);
            foreach (var did in ids)
            {
                try
                {
                    await svc.ExecuteDeviceActionAsync(did, meta.EffectiveVerb, bodyJson, ct);
                    await store.AppendAuditEventAsync(new AuditEventRecord(
                        Guid.NewGuid().ToString("n"), DateTime.UtcNow, actor,
                        $"Device action (bulk): {meta.Id}", "managedDevice", did, null,
                        auth.ActiveProfile?.TenantId));
                    results.Add(new BulkDeviceActionResultDto(did, true, null));
                }
                catch (Exception ex)
                {
                    // Concise, bounded per-device error — the first line, capped — so one
                    // device's failure doesn't abort the batch or dump full exception text.
                    var msg = ex.Message.Split('\n')[0].Trim();
                    if (msg.Length > 200) msg = msg[..200];
                    results.Add(new BulkDeviceActionResultDto(did, false, msg));
                }
            }
            await store.CommitIndexAsync();
            CacheInvalidation.OnWrite(cache, auth, "ManagedDevices");
            return Results.Ok(results);
        });

        // ─── platform-scripts (DeviceManagementScript) — ASSIGNMENTS ──────────
        app.MapGet("/platform-scripts/{id}/assignments", async (string id, AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var asgs = await new DeviceManagementScriptService(g).GetAssignmentsAsync(id, ct);
            var rows = asgs.Select(a => Assignments.ReadTarget(a.Target)).ToList();
            return Results.Ok(await Assignments.ResolveNamesAsync(rows, g, ct));
        });
        app.MapPost("/platform-scripts/{id}/assignments", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var dtos = await req.ReadFromJsonAsync<List<AssignmentDto>>(cancellationToken: ct) ?? new();
            var typed = dtos.Select(d => new DeviceManagementScriptAssignment { Target = Assignments.BuildTarget(d) }).ToList();
            await new DeviceManagementScriptService(g).AssignScriptAsync(id, typed, ct);
            CacheInvalidation.OnWrite(cache, auth, "DeviceManagementScripts");
            return Results.NoContent();
        });

        // ─── shell-scripts (DeviceShellScript) — ASSIGNMENTS ──────────────────
        app.MapGet("/shell-scripts/{id}/assignments", async (string id, AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var asgs = await new DeviceShellScriptService(g).GetAssignmentsAsync(id, ct);
            var rows = asgs.Select(a => Assignments.ReadTarget(a.Target)).ToList();
            return Results.Ok(await Assignments.ResolveNamesAsync(rows, g, ct));
        });
        app.MapPost("/shell-scripts/{id}/assignments", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var dtos = await req.ReadFromJsonAsync<List<AssignmentDto>>(cancellationToken: ct) ?? new();
            var typed = dtos.Select(d => new DeviceManagementScriptAssignment { Target = Assignments.BuildTarget(d) }).ToList();
            await new DeviceShellScriptService(g).AssignScriptAsync(id, typed, ct);
            CacheInvalidation.OnWrite(cache, auth, "DeviceShellScripts");
            return Results.NoContent();
        });
    }

    // ── M14 device-action gating (catalog + gate live in DeviceActionGate.cs) ──
    // The remediation on-demand run below reuses the same two kill-switch env flags.
    private static bool ActionsDisabled() => DeviceActionGate.IsTruthy(Environment.GetEnvironmentVariable("CMPROJECTX_DEVICE_ACTIONS_DISABLED"));
    private static bool DestructiveEnabled() => DeviceActionGate.IsTruthy(Environment.GetEnvironmentVariable("CMPROJECTX_DEVICE_ACTIONS_DESTRUCTIVE"));

    // Rail 3 (scope-check). Reuse the session's live credential + scopes (same token
    // cache as sign-in — never build a fresh credential, which would re-prompt the
    // browser for delegated profiles; see PermissionCheckEndpoints). Returns a FRAMED
    // 403 ("permission-check" gap) when the required device-action scope is absent, so
    // the operator sees a clear gap instead of a raw Graph 403 stacktrace. Fails OPEN on
    // a check ERROR (unreadable token) — Graph stays the backstop — but CLOSED on a
    // definitive missing scope. Returns null when the action may proceed.
    private static async Task<IResult?> CheckDeviceActionScopeAsync(AuthSession auth, bool destructive, CancellationToken ct)
    {
        var credential = auth.Credential;
        var scopes = auth.Scopes;
        if (credential is null || scopes is null) return null; // no credential to inspect → let Graph decide
        try
        {
            var result = await new PermissionCheckService(credential, scopes).CheckPermissionsAsync(ct);
            var granted = result.GrantedPermissions.Concat(result.ExtraPermissions);
            if (DeviceActionGate.HasDeviceActionScope(granted, destructive)) return null;
            var need = DeviceActionGate.RequiredScope(destructive);
            return Results.Json(new
            {
                error = $"Permission Check gap: this action needs the '{need}' Graph permission, which the signed-in token doesn't carry.",
                kind = "permission-check",
                requiredScope = need,
                claimSource = result.ClaimSource,
            }, statusCode: 403);
        }
        catch
        {
            return null; // couldn't evaluate the token — don't block; Graph will 403 if truly unauthorized
        }
    }

    // Explicit caller parameters win; otherwise fall back to the action's DefaultBody.
    private static string? BodyJson(DeviceActionMeta meta, DeviceActionRequest? body)
    {
        if (body?.Parameters is { } pe && pe.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            return pe.GetRawText();
        return meta.DefaultBody;
    }

    private static async Task<DeviceActionRequest?> ReadActionBodyAsync(HttpRequest req, CancellationToken ct)
    {
        if (req.ContentLength is null or 0) return null;
        try { return await req.ReadFromJsonAsync<DeviceActionRequest>(cancellationToken: ct); }
        catch { return null; }
    }
}

// Server-side request bodies for the M14 device-action endpoints. `Confirm` is the
// gate token (device name for single destructive, "{action} {count}" for bulk);
// `Parameters` is the raw Graph action parameter object, passed through verbatim.
public sealed record DeviceActionRequest(string? Confirm, JsonElement? Parameters);
public sealed record BulkDeviceActionRequest(List<string>? DeviceIds, string? Confirm, JsonElement? Parameters);
