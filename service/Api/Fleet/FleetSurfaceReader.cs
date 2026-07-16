using Microsoft.Graph.Beta.Models;
using Intune.Commander.Core.Services;
using GraphServiceClient = Microsoft.Graph.Beta.GraphServiceClient;

namespace CmProjectX.Api;

// M20 — surface dispatch for the fan-out plane. A fleet read/campaign runs the SAME
// (verb, path) the single-tenant client would, but against each target tenant's Graph.
// Rather than re-deriving that per surface, this table maps a surface key →
//   • the normalized LIST projection (the very ListItemDto the single-tenant endpoint
//     emits — so fleet rows look identical, just tenant-stamped), and
//   • a by-name lookup that returns the matched object's id + normalized body JSON,
//     used to read the golden baseline and to diff each target's live object.
// The campaign WRITE itself is NOT issued here — it is enqueued as a gated M13 replay
// (POST /pending-changes) against SurfacePath(surface), so per-surface write quirks +
// snapshot + audit reuse the existing approval-apply pipeline.
//
// This intentionally covers the assignable config surfaces a golden-template broadcast
// targets (the design's examples: compliance-policies, device-configs, settings-catalog,
// endpoint-security, app-protection). A surface absent here returns IsSupported == false,
// reported per tenant as `unsupported` — degrade, never throw.
internal static class FleetSurfaceReader
{
    public static bool IsSupported(string surface) => Normalize(surface) is not null;

    // The sidecar route the gated M13 replay PATCHes/POSTs against (e.g. "/compliance-policies").
    public static string? SurfacePath(string surface)
    {
        var key = Normalize(surface);
        return key is null ? null : "/" + key;
    }

    // Fan-out LIST: project the tenant's surface into the shared ListItemDto rows.
    public static async Task<List<ListItemDto>> ListAsync(
        GraphServiceClient g, string surface, CancellationToken ct)
    {
        switch (Normalize(surface))
        {
            case "compliance-policies":
            {
                var items = await new CompliancePolicyService(g).ListCompliancePoliciesAsync(ct);
                return items.Select(x => new ListItemDto(
                    x.Id ?? "", x.DisplayName ?? "(unnamed)",
                    (x.OdataType?.Split('.').LastOrDefault()?.Replace("CompliancePolicy", "") ?? "Compliance"),
                    x.Version != null ? "v" + x.Version : null,
                    ListProjection.PlatformOf(x.OdataType),
                    x.LastModifiedDateTime?.ToString("yyyy-MM-dd"))).ToList();
            }
            case "device-configs":
            {
                var items = await new ConfigurationProfileService(g).ListDeviceConfigurationsAsync(ct);
                return items.Select(x => new ListItemDto(
                    x.Id ?? "", x.DisplayName ?? "(unnamed)",
                    x.OdataType?.Split('.').LastOrDefault()?.Replace("microsoftGraph", "") ?? "configuration",
                    null, ListProjection.PlatformOf(x.OdataType),
                    x.LastModifiedDateTime?.ToString("yyyy-MM-dd"))).ToList();
            }
            case "settings-catalog":
            {
                var items = await new SettingsCatalogService(g).ListSettingsCatalogPoliciesAsync(ct);
                return items.Select(x => new ListItemDto(
                    x.Id ?? "", x.Name ?? "(unnamed)", "Settings Catalog",
                    null, ListProjection.PlatformOf(x.Platforms?.ToString()),
                    x.LastModifiedDateTime?.ToString("yyyy-MM-dd"))).ToList();
            }
            case "endpoint-security":
            {
                var items = await new EndpointSecurityService(g).ListEndpointSecurityIntentsAsync(ct);
                return items.Select(x => new ListItemDto(
                    x.Id ?? "", x.DisplayName ?? "(unnamed)", "Endpoint Security",
                    null, null, x.LastModifiedDateTime?.ToString("yyyy-MM-dd"))).ToList();
            }
            case "app-protection":
            {
                var items = await new AppProtectionPolicyService(g).ListAppProtectionPoliciesAsync(ct);
                return items.Select(x => new ListItemDto(
                    x.Id ?? "", x.DisplayName ?? "(unnamed)", "App Protection",
                    null, null, x.LastModifiedDateTime?.ToString("yyyy-MM-dd"))).ToList();
            }
            default:
                throw new NotSupportedException($"surface '{surface}' is not supported for fleet fan-out");
        }
    }

    // By-name lookup → the matched object's (id, normalized JSON body). Null when the
    // surface has no object named objectName in this tenant (the campaign reports skip).
    public static async Task<(string Id, string BodyJson)?> FindByNameAsync(
        GraphServiceClient g, string surface, string objectName, CancellationToken ct)
    {
        switch (Normalize(surface))
        {
            case "compliance-policies":
            {
                var match = (await new CompliancePolicyService(g).ListCompliancePoliciesAsync(ct))
                    .FirstOrDefault(x => NameMatch(x.DisplayName, objectName));
                if (match?.Id is null) return null;
                var full = await new CompliancePolicyService(g).GetCompliancePolicyAsync(match.Id, ct);
                return full is null ? null : (match.Id, CrudJson.ToJson(full));
            }
            case "device-configs":
            {
                var match = (await new ConfigurationProfileService(g).ListDeviceConfigurationsAsync(ct))
                    .FirstOrDefault(x => NameMatch(x.DisplayName, objectName));
                if (match?.Id is null) return null;
                var full = await new ConfigurationProfileService(g).GetDeviceConfigurationAsync(match.Id, ct);
                return full is null ? null : (match.Id, CrudJson.ToJson(full));
            }
            case "settings-catalog":
            {
                var match = (await new SettingsCatalogService(g).ListSettingsCatalogPoliciesAsync(ct))
                    .FirstOrDefault(x => NameMatch(x.Name, objectName));
                if (match?.Id is null) return null;
                var full = await new SettingsCatalogService(g).GetSettingsCatalogPolicyAsync(match.Id, ct);
                return full is null ? null : (match.Id, CrudJson.ToJson(full));
            }
            case "endpoint-security":
            {
                var match = (await new EndpointSecurityService(g).ListEndpointSecurityIntentsAsync(ct))
                    .FirstOrDefault(x => NameMatch(x.DisplayName, objectName));
                if (match?.Id is null) return null;
                var full = await new EndpointSecurityService(g).GetEndpointSecurityIntentAsync(match.Id, ct);
                return full is null ? null : (match.Id, CrudJson.ToJson(full));
            }
            case "app-protection":
            {
                var match = (await new AppProtectionPolicyService(g).ListAppProtectionPoliciesAsync(ct))
                    .FirstOrDefault(x => NameMatch(x.DisplayName, objectName));
                if (match?.Id is null) return null;
                var full = await new AppProtectionPolicyService(g).GetAppProtectionPolicyAsync(match.Id, ct);
                return full is null ? null : (match.Id, CrudJson.ToJson(full));
            }
            default:
                throw new NotSupportedException($"surface '{surface}' is not supported for fleet fan-out");
        }
    }

    private static bool NameMatch(string? a, string b) =>
        string.Equals(a?.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    // Canonical surface key (tolerant of a leading slash / casing). null = unsupported.
    private static string? Normalize(string surface)
    {
        var k = surface.Trim().TrimStart('/').ToLowerInvariant();
        return k switch
        {
            "compliance-policies" => "compliance-policies",
            "device-configs" => "device-configs",
            "settings-catalog" => "settings-catalog",
            "endpoint-security" => "endpoint-security",
            "app-protection" => "app-protection",
            _ => null,
        };
    }
}
