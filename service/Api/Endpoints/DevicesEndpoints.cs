using Microsoft.Graph.Beta.Models;
using Intune.Commander.Core.Services;
// Microsoft.Graph.Beta.Models also defines a `WebApplication` type, which collides
// with the ASP.NET host type used by the extension method. Alias to disambiguate.
using WebApplication = Microsoft.AspNetCore.Builder.WebApplication;

namespace CmProjectX.Api;

// Devices (M2) — per-surface CRUD endpoint module for the device-management
// configuration surfaces. Lives outside Program.cs precisely so it can name
// Microsoft.Graph.Beta.Models element types (DeviceConfiguration, etc.) for
// CrudJson.ReadModelAsync<T> / ToJson<T> — a Graph `using` in Program.cs shadows
// IResult and breaks Minimal API lambda overload resolution.
//
// Every surface constructs its forked Core service with the active
// GraphServiceClient (AuthSession.Graph) and 409s when signed out. LIST projects
// the Graph element into the normalized {id,title,subtitle,badge} ListItemDto.
public static class DevicesEndpoints
{
    public static void MapDevices(this WebApplication app)
    {
        // ─── device-configs — ConfigurationProfileService / DeviceConfiguration (CRUD) ───
        app.MapGet("/device-configs", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "DeviceConfigurations", ct => new ConfigurationProfileService(g).ListDeviceConfigurationsAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.DisplayName ?? "(unnamed)",
                (x.OdataType?.Split('.').LastOrDefault()?.Replace("microsoftGraph", "") ?? "configuration") + (x.Version != null ? $" • v{x.Version}" : ""),
                null,
                ListProjection.PlatformOf(x.OdataType),
                x.LastModifiedDateTime?.ToString("yyyy-MM-dd"))));
        });

        app.MapGet("/device-configs/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "DeviceConfigurations", id, ct => new ConfigurationProfileService(g).GetDeviceConfigurationAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });

        app.MapPost("/device-configs", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<DeviceConfiguration>(req, ct);
            var created = await new ConfigurationProfileService(g).CreateDeviceConfigurationAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "DeviceConfigurations");
            return Results.Ok(new { id = created.Id });
        });

        app.MapPatch("/device-configs/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<DeviceConfiguration>(req, ct);
            model.Id = id;
            await new ConfigurationProfileService(g).UpdateDeviceConfigurationAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "DeviceConfigurations", id);
            return Results.NoContent();
        });

        app.MapDelete("/device-configs/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new ConfigurationProfileService(g).DeleteDeviceConfigurationAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "DeviceConfigurations", id);
            return Results.NoContent();
        });

        // ─── compliance-policies — CompliancePolicyService / DeviceCompliancePolicy (CRUD) ───
        app.MapGet("/compliance-policies", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "CompliancePolicies", ct => new CompliancePolicyService(g).ListCompliancePoliciesAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.DisplayName ?? "(unnamed)",
                (x.OdataType?.Split('.').LastOrDefault()?.Replace("CompliancePolicy", "") ?? "Compliance") + (x.LastModifiedDateTime != null ? " · " + x.LastModifiedDateTime.Value.ToString("yyyy-MM-dd") : ""),
                x.Version != null ? "v" + x.Version.ToString() : null,
                ListProjection.PlatformOf(x.OdataType),
                x.LastModifiedDateTime?.ToString("yyyy-MM-dd"))));
        });

        app.MapGet("/compliance-policies/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "CompliancePolicies", id, ct => new CompliancePolicyService(g).GetCompliancePolicyAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });

        app.MapPost("/compliance-policies", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<DeviceCompliancePolicy>(req, ct);
            var created = await new CompliancePolicyService(g).CreateCompliancePolicyAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "CompliancePolicies");
            return Results.Ok(new { id = created.Id });
        });

        app.MapPatch("/compliance-policies/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<DeviceCompliancePolicy>(req, ct);
            model.Id = id;
            await new CompliancePolicyService(g).UpdateCompliancePolicyAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "CompliancePolicies", id);
            return Results.NoContent();
        });

        app.MapDelete("/compliance-policies/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new CompliancePolicyService(g).DeleteCompliancePolicyAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "CompliancePolicies", id);
            return Results.NoContent();
        });

        // ─── settings-catalog — SettingsCatalogService / DeviceManagementConfigurationPolicy (CRUD) ───
        // Title uses Name (not DisplayName); UPDATE is metadata-only: UpdateSettingsCatalogPolicyMetadataAsync(id, model).
        app.MapGet("/settings-catalog", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "SettingsCatalog", ct => new SettingsCatalogService(g).ListSettingsCatalogPoliciesAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.Name ?? "(unnamed)",
                $"{(x.Platforms?.ToString() ?? "unknown")} · {(x.SettingCount ?? 0)} settings",
                x.IsAssigned == true ? "assigned" : null,
                x.Platforms?.ToString(),
                x.LastModifiedDateTime?.ToString("yyyy-MM-dd"))));
        });

        app.MapGet("/settings-catalog/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "SettingsCatalog", id, ct => new SettingsCatalogService(g).GetSettingsCatalogPolicyAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });

        app.MapPost("/settings-catalog", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<DeviceManagementConfigurationPolicy>(req, ct);
            var created = await new SettingsCatalogService(g).CreateSettingsCatalogPolicyAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "SettingsCatalog");
            return Results.Ok(new { id = created.Id });
        });

        app.MapPatch("/settings-catalog/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<DeviceManagementConfigurationPolicy>(req, ct);
            model.Id = id;
            await new SettingsCatalogService(g).UpdateSettingsCatalogPolicyMetadataAsync(id, model, ct);
            CacheInvalidation.OnWrite(cache, auth, "SettingsCatalog", id);
            return Results.NoContent();
        });

        app.MapDelete("/settings-catalog/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new SettingsCatalogService(g).DeleteSettingsCatalogPolicyAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "SettingsCatalog", id);
            return Results.NoContent();
        });

        // settings-catalog SETTINGS — the deep settingInstance tree, fetched separately
        // (the policy GET returns metadata only). Returns the settings as a JSON array
        // for the client's collapsible Settings tree.
        app.MapGet("/settings-catalog/{id}/settings", async (string id, AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var settings = await new SettingsCatalogService(g).GetPolicySettingsAsync(id, ct);
            var json = "[" + string.Join(",", settings.Select(s => CrudJson.ToJson(s))) + "]";
            return Results.Content(json, "application/json");
        });

        // ─── admin-templates — AdministrativeTemplateService / GroupPolicyConfiguration (CRUD) ───
        app.MapGet("/admin-templates", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "AdministrativeTemplates", ct => new AdministrativeTemplateService(g).ListAdministrativeTemplatesAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.DisplayName ?? "(unnamed)",
                (x.OdataType?.Split('.').LastOrDefault()?.Replace("microsoftGraph", "") ?? "groupPolicyConfiguration") + ((x.RoleScopeTagIds?.Count ?? 0) > 0 ? $" · {x.RoleScopeTagIds!.Count} scope tag(s)" : ""),
                string.IsNullOrWhiteSpace(x.Description) ? null : "described",
                "Windows", // administrative templates are Windows-only
                x.LastModifiedDateTime?.ToString("yyyy-MM-dd"))));
        });

        app.MapGet("/admin-templates/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "AdministrativeTemplates", id, ct => new AdministrativeTemplateService(g).GetAdministrativeTemplateAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });

        app.MapPost("/admin-templates", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<GroupPolicyConfiguration>(req, ct);
            var created = await new AdministrativeTemplateService(g).CreateAdministrativeTemplateAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "AdministrativeTemplates");
            return Results.Ok(new { id = created.Id });
        });

        app.MapPatch("/admin-templates/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<GroupPolicyConfiguration>(req, ct);
            model.Id = id;
            await new AdministrativeTemplateService(g).UpdateAdministrativeTemplateAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "AdministrativeTemplates", id);
            return Results.NoContent();
        });

        app.MapDelete("/admin-templates/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new AdministrativeTemplateService(g).DeleteAdministrativeTemplateAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "AdministrativeTemplates", id);
            return Results.NoContent();
        });

        // ─── endpoint-security — EndpointSecurityService / DeviceManagementIntent (CRUD) ───
        app.MapGet("/endpoint-security", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "EndpointSecurityIntents", ct => new EndpointSecurityService(g).ListEndpointSecurityIntentsAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.DisplayName ?? "(unnamed)",
                (string.IsNullOrEmpty(x.TemplateId) ? (x.OdataType?.Split('.').LastOrDefault()?.Replace("microsoftGraph", "") ?? "Intent") : x.TemplateId!) + (x.LastModifiedDateTime is { } lm ? " · " + lm.ToString("yyyy-MM-dd") : ""),
                (x.IsAssigned ?? false) ? "assigned" : null,
                ListProjection.PlatformOf(x.OdataType),
                x.LastModifiedDateTime?.ToString("yyyy-MM-dd"))));
        });

        app.MapGet("/endpoint-security/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "EndpointSecurityIntents", id, ct => new EndpointSecurityService(g).GetEndpointSecurityIntentAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });

        app.MapPost("/endpoint-security", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<DeviceManagementIntent>(req, ct);
            var created = await new EndpointSecurityService(g).CreateEndpointSecurityIntentAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "EndpointSecurityIntents");
            return Results.Ok(new { id = created.Id });
        });

        app.MapPatch("/endpoint-security/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<DeviceManagementIntent>(req, ct);
            model.Id = id;
            await new EndpointSecurityService(g).UpdateEndpointSecurityIntentAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "EndpointSecurityIntents", id);
            return Results.NoContent();
        });

        app.MapDelete("/endpoint-security/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new EndpointSecurityService(g).DeleteEndpointSecurityIntentAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "EndpointSecurityIntents", id);
            return Results.NoContent();
        });

        // ─── ASSIGNMENTS — normalized group/filter assignment surfaces ───
        // Each surface exposes GET (read normalized AssignmentDto list) and POST
        // (replace assignments from an AssignmentDto list). Targets are built/read
        // via the shared Assignments helpers; wrappers expose a settable .Target.

        // compliance-policies assignments — CompliancePolicyService / DeviceCompliancePolicyAssignment
        app.MapGet("/compliance-policies/{id}/assignments", async (string id, AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var asgs = await new CompliancePolicyService(g).GetAssignmentsAsync(id, ct);
            var rows = asgs.Select(a => Assignments.ReadTarget(a.Target)).ToList();
            return Results.Ok(await Assignments.ResolveNamesAsync(rows, g, ct));
        });

        app.MapPost("/compliance-policies/{id}/assignments", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var dtos = await req.ReadFromJsonAsync<List<AssignmentDto>>(cancellationToken: ct) ?? new();
            var typed = dtos.Select(d => new DeviceCompliancePolicyAssignment { Target = Assignments.BuildTarget(d) }).ToList();
            await new CompliancePolicyService(g).AssignPolicyAsync(id, typed, ct);
            CacheInvalidation.OnWrite(cache, auth, "CompliancePolicies");
            return Results.NoContent();
        });

        // settings-catalog assignments — SettingsCatalogService / DeviceManagementConfigurationPolicyAssignment
        app.MapGet("/settings-catalog/{id}/assignments", async (string id, AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var asgs = await new SettingsCatalogService(g).GetAssignmentsAsync(id, ct);
            var rows = asgs.Select(a => Assignments.ReadTarget(a.Target)).ToList();
            return Results.Ok(await Assignments.ResolveNamesAsync(rows, g, ct));
        });

        app.MapPost("/settings-catalog/{id}/assignments", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var dtos = await req.ReadFromJsonAsync<List<AssignmentDto>>(cancellationToken: ct) ?? new();
            var typed = dtos.Select(d => new DeviceManagementConfigurationPolicyAssignment { Target = Assignments.BuildTarget(d) }).ToList();
            await new SettingsCatalogService(g).AssignSettingsCatalogPolicyAsync(id, typed, ct);
            CacheInvalidation.OnWrite(cache, auth, "SettingsCatalog");
            return Results.NoContent();
        });

        // admin-templates assignments — AdministrativeTemplateService / GroupPolicyConfigurationAssignment
        app.MapGet("/admin-templates/{id}/assignments", async (string id, AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var asgs = await new AdministrativeTemplateService(g).GetAssignmentsAsync(id, ct);
            var rows = asgs.Select(a => Assignments.ReadTarget(a.Target)).ToList();
            return Results.Ok(await Assignments.ResolveNamesAsync(rows, g, ct));
        });

        app.MapPost("/admin-templates/{id}/assignments", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var dtos = await req.ReadFromJsonAsync<List<AssignmentDto>>(cancellationToken: ct) ?? new();
            var typed = dtos.Select(d => new GroupPolicyConfigurationAssignment { Target = Assignments.BuildTarget(d) }).ToList();
            await new AdministrativeTemplateService(g).AssignAdministrativeTemplateAsync(id, typed, ct);
            CacheInvalidation.OnWrite(cache, auth, "AdministrativeTemplates");
            return Results.NoContent();
        });

        // endpoint-security assignments — EndpointSecurityService / DeviceManagementIntentAssignment
        app.MapGet("/endpoint-security/{id}/assignments", async (string id, AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var asgs = await new EndpointSecurityService(g).GetAssignmentsAsync(id, ct);
            var rows = asgs.Select(a => Assignments.ReadTarget(a.Target)).ToList();
            return Results.Ok(await Assignments.ResolveNamesAsync(rows, g, ct));
        });

        app.MapPost("/endpoint-security/{id}/assignments", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var dtos = await req.ReadFromJsonAsync<List<AssignmentDto>>(cancellationToken: ct) ?? new();
            var typed = dtos.Select(d => new DeviceManagementIntentAssignment { Target = Assignments.BuildTarget(d) }).ToList();
            await new EndpointSecurityService(g).AssignIntentAsync(id, typed, ct);
            CacheInvalidation.OnWrite(cache, auth, "EndpointSecurityIntents");
            return Results.NoContent();
        });

        // device-configs assignments — READ-ONLY (Core exposes no set method, so the
        // detail panel displays the resolved set but offers no editor). Names resolved.
        app.MapGet("/device-configs/{id}/assignments", async (string id, AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var asgs = await new ConfigurationProfileService(g).GetAssignmentsAsync(id, ct);
            var rows = asgs.Select(a => Assignments.ReadTarget(a.Target)).ToList();
            return Results.Ok(await Assignments.ResolveNamesAsync(rows, g, ct));
        });
    }
}
