using Microsoft.Graph.Beta.Models;
using Intune.Commander.Core.Services;
// Microsoft.Graph.Beta.Models also defines a `WebApplication` type, which shadows the
// ASP.NET host type in this file's `this WebApplication app` extension. Alias the host
// type so the Graph models `using` above can't ambiguate it (CS0104).
using WebApplication = Microsoft.AspNetCore.Builder.WebApplication;

namespace CmProjectX.Api;

// TenantAdmin group — live Graph endpoints for tenant-wide admin surfaces (scope
// tags, RBAC role definitions, assignment filters, policy sets, branding, T&C,
// Cloud PC user settings, ADMX files, reusable settings, notification templates).
//
// Each handler constructs the forked Core service with the active GraphServiceClient
// (AuthSession.Graph) and either projects the Graph element type into the normalized
// ListItemDto (lists) or round-trips JSON via CrudJson (detail / CRUD). 409 = signed
// out. Graph type names live here (not Program.cs) on purpose — see CrudJson remarks.
public static class TenantAdminEndpoints
{
    public static void MapTenantAdmin(this WebApplication app)
    {
        // ─── scope-tags (RoleScopeTag) — CRUD ────────────────────────────────
        app.MapGet("/scope-tags", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "ScopeTags", ct => new ScopeTagService(g).ListScopeTagsAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.DisplayName ?? "(unnamed)",
                !string.IsNullOrWhiteSpace(x.Description) ? x.Description! : "scope tag",
                x.IsBuiltIn == true ? "built-in" : null)));
        });
        app.MapGet("/scope-tags/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "ScopeTags", id, ct => new ScopeTagService(g).GetScopeTagAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });
        app.MapPost("/scope-tags", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<RoleScopeTag>(req, ct);
            var created = await new ScopeTagService(g).CreateScopeTagAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "ScopeTags");
            return Results.Ok(new { id = created.Id });
        });
        app.MapPatch("/scope-tags/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<RoleScopeTag>(req, ct);
            model.Id = id;
            await new ScopeTagService(g).UpdateScopeTagAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "ScopeTags", id);
            return Results.NoContent();
        });
        app.MapDelete("/scope-tags/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new ScopeTagService(g).DeleteScopeTagAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "ScopeTags", id);
            return Results.NoContent();
        });

        // ─── role-definitions (RoleDefinition) — CRUD ────────────────────────
        app.MapGet("/role-definitions", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "RoleDefinitions", ct => new RoleDefinitionService(g).ListRoleDefinitionsAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.DisplayName ?? "(unnamed)",
                !string.IsNullOrWhiteSpace(x.Description) ? x.Description! : $"{(x.RolePermissions?.Count ?? 0)} permission set(s)",
                x.IsBuiltInRoleDefinition == true ? "built-in" : null)));
        });
        app.MapGet("/role-definitions/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "RoleDefinitions", id, ct => new RoleDefinitionService(g).GetRoleDefinitionAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });
        app.MapPost("/role-definitions", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<RoleDefinition>(req, ct);
            var created = await new RoleDefinitionService(g).CreateRoleDefinitionAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "RoleDefinitions");
            return Results.Ok(new { id = created.Id });
        });
        app.MapPatch("/role-definitions/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<RoleDefinition>(req, ct);
            model.Id = id;
            await new RoleDefinitionService(g).UpdateRoleDefinitionAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "RoleDefinitions", id);
            return Results.NoContent();
        });
        app.MapDelete("/role-definitions/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new RoleDefinitionService(g).DeleteRoleDefinitionAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "RoleDefinitions", id);
            return Results.NoContent();
        });

        // ─── role-assignments (DeviceAndAppManagementRoleAssignment) — READ ──
        // RBAC role assignments are read-only here: Core exposes list + get only
        // (members/scope are group GUIDs; create/update is out of scope for this screen).
        app.MapGet("/role-assignments", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "RoleAssignments", ct => new RoleDefinitionService(g).GetRoleAssignmentsAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.DisplayName ?? "(unnamed)",
                !string.IsNullOrWhiteSpace(x.Description) ? x.Description! : $"{(x.Members?.Count ?? 0)} member(s)",
                x.ScopeType?.ToString())));
        });
        app.MapGet("/role-assignments/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "RoleAssignments", id, ct => new RoleDefinitionService(g).GetRoleAssignmentAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });

        // ─── assignment-filters (DeviceAndAppManagementAssignmentFilter) — READ ─
        app.MapGet("/assignment-filters", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "AssignmentFilters", ct => new AssignmentFilterService(g).ListFiltersAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.DisplayName ?? "(unnamed)",
                string.Join(" · ", new[] { x.Platform?.ToString(), x.AssignmentFilterManagementType?.ToString() }.Where(s => !string.IsNullOrEmpty(s))),
                !string.IsNullOrWhiteSpace(x.Rule) ? "rule" : null)));
        });
        app.MapGet("/assignment-filters/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "AssignmentFilters", id, ct => new AssignmentFilterService(g).GetFilterAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });

        // ─── policy-sets (PolicySet) — READ ──────────────────────────────────
        app.MapGet("/policy-sets", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "PolicySets", ct => new PolicySetService(g).ListPolicySetsAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.DisplayName ?? "(unnamed)",
                $"{(x.Items?.Count ?? 0)} item(s)" + (x.Status != null ? $" · {x.Status}" : ""),
                (x.Items?.Count ?? 0) > 0 ? "populated" : null)));
        });
        app.MapGet("/policy-sets/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "PolicySets", id, ct => new PolicySetService(g).GetPolicySetAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });

        // ─── intune-branding (IntuneBrandingProfile) — CRUD ──────────────────
        app.MapGet("/intune-branding", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "IntuneBrandingProfiles", ct => new IntuneBrandingService(g).ListIntuneBrandingProfilesAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.ProfileName ?? x.DisplayName ?? "(unnamed)",
                !string.IsNullOrWhiteSpace(x.ProfileDescription) ? x.ProfileDescription! : "branding profile",
                x.IsDefaultProfile == true ? "default" : null)));
        });
        app.MapGet("/intune-branding/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "IntuneBrandingProfiles", id, ct => new IntuneBrandingService(g).GetIntuneBrandingProfileAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });
        app.MapPost("/intune-branding", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<IntuneBrandingProfile>(req, ct);
            var created = await new IntuneBrandingService(g).CreateIntuneBrandingProfileAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "IntuneBrandingProfiles");
            return Results.Ok(new { id = created.Id });
        });
        app.MapPatch("/intune-branding/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<IntuneBrandingProfile>(req, ct);
            model.Id = id;
            await new IntuneBrandingService(g).UpdateIntuneBrandingProfileAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "IntuneBrandingProfiles", id);
            return Results.NoContent();
        });
        app.MapDelete("/intune-branding/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new IntuneBrandingService(g).DeleteIntuneBrandingProfileAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "IntuneBrandingProfiles", id);
            return Results.NoContent();
        });

        // ─── azure-branding (OrganizationalBrandingLocalization) — CRUD ──────
        // Org id is resolved internally by the service; the {id} route param is the
        // localization id (a locale string, e.g. "en-US"). The element type has no
        // DisplayName — Id (the locale) is the title.
        app.MapGet("/azure-branding", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "AzureBrandingLocalizations", ct => new AzureBrandingService(g).ListBrandingLocalizationsAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                string.IsNullOrWhiteSpace(x.Id) ? "(default)" : x.Id!,
                !string.IsNullOrWhiteSpace(x.SignInPageText) ? x.SignInPageText! : "localization",
                !string.IsNullOrWhiteSpace(x.BackgroundColor) ? "themed" : null)));
        });
        app.MapGet("/azure-branding/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "AzureBrandingLocalizations", id, ct => new AzureBrandingService(g).GetBrandingLocalizationAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });
        app.MapPost("/azure-branding", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<OrganizationalBrandingLocalization>(req, ct);
            var created = await new AzureBrandingService(g).CreateBrandingLocalizationAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "AzureBrandingLocalizations");
            return Results.Ok(new { id = created.Id });
        });
        app.MapPatch("/azure-branding/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<OrganizationalBrandingLocalization>(req, ct);
            model.Id = id;
            await new AzureBrandingService(g).UpdateBrandingLocalizationAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "AzureBrandingLocalizations", id);
            return Results.NoContent();
        });
        app.MapDelete("/azure-branding/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new AzureBrandingService(g).DeleteBrandingLocalizationAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "AzureBrandingLocalizations", id);
            return Results.NoContent();
        });

        // ─── terms-conditions (TermsAndConditions) — CRUD ────────────────────
        app.MapGet("/terms-conditions", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "TermsAndConditions", ct => new TermsAndConditionsService(g).ListTermsAndConditionsAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.DisplayName ?? "(unnamed)",
                !string.IsNullOrWhiteSpace(x.Title) ? x.Title! : "terms & conditions",
                x.Version != null ? $"v{x.Version}" : null)));
        });
        app.MapGet("/terms-conditions/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "TermsAndConditions", id, ct => new TermsAndConditionsService(g).GetTermsAndConditionsAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });
        app.MapPost("/terms-conditions", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<TermsAndConditions>(req, ct);
            var created = await new TermsAndConditionsService(g).CreateTermsAndConditionsAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "TermsAndConditions");
            return Results.Ok(new { id = created.Id });
        });
        app.MapPatch("/terms-conditions/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<TermsAndConditions>(req, ct);
            model.Id = id;
            await new TermsAndConditionsService(g).UpdateTermsAndConditionsAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "TermsAndConditions", id);
            return Results.NoContent();
        });
        app.MapDelete("/terms-conditions/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new TermsAndConditionsService(g).DeleteTermsAndConditionsAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "TermsAndConditions", id);
            return Results.NoContent();
        });

        // ─── cloudpc-user-settings (CloudPcUserSetting) — READ ───────────────
        app.MapGet("/cloudpc-user-settings", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "CloudPcUserSettings", ct => new CloudPcUserSettingsService(g).ListUserSettingsAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.DisplayName ?? "(unnamed)",
                string.Join(" · ", new[]
                {
                    x.SelfServiceEnabled == true ? "self-service" : null,
                    x.LocalAdminEnabled == true ? "local-admin" : null,
                    x.ResetEnabled == true ? "reset" : null
                }.Where(s => !string.IsNullOrEmpty(s))),
                (x.Assignments?.Count ?? 0) > 0 ? "assigned" : null)));
        });
        app.MapGet("/cloudpc-user-settings/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "CloudPcUserSettings", id, ct => new CloudPcUserSettingsService(g).GetUserSettingAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });

        // ─── admx-files (GroupPolicyUploadedDefinitionFile) — Create+Delete+List+Get
        // (immutable: no update/PATCH).
        app.MapGet("/admx-files", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "AdmxFiles", ct => new AdmxFileService(g).ListAdmxFilesAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.DisplayName ?? x.FileName ?? "(unnamed)",
                string.Join(" · ", new[] { x.FileName, x.DefaultLanguageCode, x.Status?.ToString() }.Where(s => !string.IsNullOrEmpty(s))),
                x.Status?.ToString())));
        });
        app.MapGet("/admx-files/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "AdmxFiles", id, ct => new AdmxFileService(g).GetAdmxFileAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });
        app.MapPost("/admx-files", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<GroupPolicyUploadedDefinitionFile>(req, ct);
            var created = await new AdmxFileService(g).CreateAdmxFileAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "AdmxFiles");
            return Results.Ok(new { id = created.Id });
        });
        app.MapDelete("/admx-files/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new AdmxFileService(g).DeleteAdmxFileAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "AdmxFiles", id);
            return Results.NoContent();
        });

        // ─── reusable-settings (DeviceManagementReusablePolicySetting) — CRUD ─
        app.MapGet("/reusable-settings", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "ReusableSettings", ct => new ReusablePolicySettingService(g).ListReusablePolicySettingsAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.DisplayName ?? "(unnamed)",
                !string.IsNullOrWhiteSpace(x.Description) ? x.Description! : $"{(x.ReferencingConfigurationPolicyCount ?? 0)} referencing policy(ies)",
                (x.ReferencingConfigurationPolicyCount ?? 0) > 0 ? "in use" : null)));
        });
        app.MapGet("/reusable-settings/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "ReusableSettings", id, ct => new ReusablePolicySettingService(g).GetReusablePolicySettingAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });
        app.MapPost("/reusable-settings", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<DeviceManagementReusablePolicySetting>(req, ct);
            var created = await new ReusablePolicySettingService(g).CreateReusablePolicySettingAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "ReusableSettings");
            return Results.Ok(new { id = created.Id });
        });
        app.MapPatch("/reusable-settings/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<DeviceManagementReusablePolicySetting>(req, ct);
            model.Id = id;
            await new ReusablePolicySettingService(g).UpdateReusablePolicySettingAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "ReusableSettings", id);
            return Results.NoContent();
        });
        app.MapDelete("/reusable-settings/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new ReusablePolicySettingService(g).DeleteReusablePolicySettingAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "ReusableSettings", id);
            return Results.NoContent();
        });

        // ─── notification-templates (NotificationMessageTemplate) — CRUD ─────
        app.MapGet("/notification-templates", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "NotificationTemplates", ct => new NotificationTemplateService(g).ListNotificationTemplatesAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.DisplayName ?? "(unnamed)",
                string.Join(" · ", new[] { x.DefaultLocale, x.BrandingOptions?.ToString() }.Where(s => !string.IsNullOrEmpty(s))),
                (x.LocalizedNotificationMessages?.Count ?? 0) > 0 ? $"{x.LocalizedNotificationMessages!.Count} locale(s)" : null)));
        });
        app.MapGet("/notification-templates/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "NotificationTemplates", id, ct => new NotificationTemplateService(g).GetNotificationTemplateAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });
        app.MapPost("/notification-templates", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<NotificationMessageTemplate>(req, ct);
            var created = await new NotificationTemplateService(g).CreateNotificationTemplateAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "NotificationTemplates");
            return Results.Ok(new { id = created.Id });
        });
        app.MapPatch("/notification-templates/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<NotificationMessageTemplate>(req, ct);
            model.Id = id;
            await new NotificationTemplateService(g).UpdateNotificationTemplateAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "NotificationTemplates", id);
            return Results.NoContent();
        });
        app.MapDelete("/notification-templates/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new NotificationTemplateService(g).DeleteNotificationTemplateAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "NotificationTemplates", id);
            return Results.NoContent();
        });
    }
}
