using Microsoft.Graph.Beta.Models;
using Intune.Commander.Core.Services;
// Microsoft.Graph.Beta.Models also defines a `WebApplication` type, which collides
// with the ASP.NET host type used as the extension-method receiver below. Alias the
// host explicitly so the `this` parameter resolves unambiguously.
using WebApplication = Microsoft.AspNetCore.Builder.WebApplication;

namespace CmProjectX.Api;

// Enrollment group — live Graph endpoints ported from IntuneCommander. Each handler
// builds the forked Core service with the active GraphServiceClient (AuthSession.Graph)
// and projects the Graph element type to the normalized ListItemDto row. 409 = signed
// out. This file lives outside Program.cs precisely so it may name Graph.Beta.Models
// types (a Graph `using` in Program.cs shadows IResult and breaks Minimal API overloads).
public static class EnrollmentEndpoints
{
    public static void MapEnrollment(this WebApplication app)
    {
        // ─── enrollment-configs (CRUD) — DeviceEnrollmentConfiguration ───────────
        app.MapGet("/enrollment-configs", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "EnrollmentConfigurations", ct => new EnrollmentConfigurationService(g).ListEnrollmentConfigurationsAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.DisplayName ?? "(unnamed)",
                string.Join(" · ", new[]
                {
                    x.OdataType?.Split('.').LastOrDefault()?.Replace("microsoftGraph", "") ?? "deviceEnrollmentConfiguration",
                    x.Priority is { } p ? $"priority {p}" : null,
                }.Where(s => !string.IsNullOrEmpty(s))),
                x.Version is { } v ? $"v{v}" : null)));
        });

        app.MapGet("/enrollment-configs/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "EnrollmentConfigurations", id, ct => new EnrollmentConfigurationService(g).GetEnrollmentConfigurationAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });

        app.MapPost("/enrollment-configs", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<DeviceEnrollmentConfiguration>(req, ct);
            var created = await new EnrollmentConfigurationService(g).CreateEnrollmentConfigurationAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "EnrollmentConfigurations");
            return Results.Ok(new { id = created.Id });
        });

        app.MapPatch("/enrollment-configs/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<DeviceEnrollmentConfiguration>(req, ct);
            model.Id = id;
            await new EnrollmentConfigurationService(g).UpdateEnrollmentConfigurationAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "EnrollmentConfigurations", id);
            return Results.NoContent();
        });

        app.MapDelete("/enrollment-configs/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new EnrollmentConfigurationService(g).DeleteEnrollmentConfigurationAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "EnrollmentConfigurations", id);
            return Results.NoContent();
        });

        // ─── autopilot (CRUD) — WindowsAutopilotDeploymentProfile ────────────────
        app.MapGet("/autopilot", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "AutopilotProfiles", ct => new AutopilotService(g).ListAutopilotProfilesAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.DisplayName ?? "(unnamed)",
                string.Join(" · ", new[]
                {
                    x.OdataType?.Split('.').LastOrDefault()?.Replace("microsoftGraph", "") ?? "windowsAutopilotDeploymentProfile",
                    x.LastModifiedDateTime is { } lm ? lm.ToString("yyyy-MM-dd") : null,
                }.Where(s => !string.IsNullOrEmpty(s))),
                string.IsNullOrWhiteSpace(x.Description) ? null : "described")));
        });

        app.MapGet("/autopilot/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "AutopilotProfiles", id, ct => new AutopilotService(g).GetAutopilotProfileAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });

        app.MapPost("/autopilot", async (HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<WindowsAutopilotDeploymentProfile>(req, ct);
            var created = await new AutopilotService(g).CreateAutopilotProfileAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "AutopilotProfiles");
            return Results.Ok(new { id = created.Id });
        });

        app.MapPatch("/autopilot/{id}", async (string id, HttpRequest req, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var model = await CrudJson.ReadModelAsync<WindowsAutopilotDeploymentProfile>(req, ct);
            model.Id = id;
            await new AutopilotService(g).UpdateAutopilotProfileAsync(model, ct);
            CacheInvalidation.OnWrite(cache, auth, "AutopilotProfiles", id);
            return Results.NoContent();
        });

        app.MapDelete("/autopilot/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            await new AutopilotService(g).DeleteAutopilotProfileAsync(id, ct);
            CacheInvalidation.OnWrite(cache, auth, "AutopilotProfiles", id);
            return Results.NoContent();
        });

        // ─── apple-dep (READ) — DepOnboardingSetting ─────────────────────────────
        app.MapGet("/apple-dep", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "AppleDep", ct => new AppleDepService(g).ListDepOnboardingSettingsAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                !string.IsNullOrWhiteSpace(x.TokenName) ? x.TokenName! : (x.AppleIdentifier ?? "(DEP token)"),
                string.Join(" · ", new[]
                {
                    string.IsNullOrEmpty(x.AppleIdentifier) ? null : x.AppleIdentifier,
                    x.TokenType is { } tt ? tt.ToString() : null,
                    x.SyncedDeviceCount is { } sdc ? $"{sdc} device(s)" : null,
                }.Where(s => !string.IsNullOrEmpty(s))),
                x.TokenExpirationDateTime is { } exp && exp < DateTimeOffset.UtcNow ? "expired" : null)));
        });

        app.MapGet("/apple-dep/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "AppleDep", id, ct => new AppleDepService(g).GetDepOnboardingSettingAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });

        // ─── cloudpc-provisioning (READ) — CloudPcProvisioningPolicy ─────────────
        app.MapGet("/cloudpc-provisioning", async (AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var items = await CachedReader.ListAsync(cache, auth, "CloudPcProvisioning", ct => new CloudPcProvisioningService(g).ListProvisioningPoliciesAsync(ct), ct);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.DisplayName ?? "(unnamed)",
                string.Join(" · ", new[]
                {
                    string.IsNullOrEmpty(x.ImageDisplayName) ? null : x.ImageDisplayName,
                    x.ProvisioningType is { } pt ? pt.ToString() : null,
                }.Where(s => !string.IsNullOrEmpty(s))),
                string.IsNullOrWhiteSpace(x.Description) ? null : "described")));
        });

        app.MapGet("/cloudpc-provisioning/{id}", async (string id, AuthSession auth, ICacheService cache, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var obj = await CachedReader.GetAsync(cache, auth, "CloudPcProvisioning", id, ct => new CloudPcProvisioningService(g).GetProvisioningPolicyAsync(id, ct), ct);
            return obj is null ? Results.NotFound() : Results.Content(CrudJson.ToJson(obj), "application/json");
        });
    }
}
