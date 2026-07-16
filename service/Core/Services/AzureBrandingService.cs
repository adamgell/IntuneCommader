using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class AzureBrandingService : IAzureBrandingService
{
    private readonly GraphServiceClient _graphClient;

    public AzureBrandingService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    private async Task<string> GetOrganizationIdAsync(CancellationToken cancellationToken)
    {
        var response = await _graphClient.Organization
            .GetAsync(req =>
            {
                req.QueryParameters.Top = 1;
            }, cancellationToken);

        var orgId = response?.Value?.FirstOrDefault()?.Id;
        return orgId ?? throw new InvalidOperationException("No organization found for branding operations");
    }

    public async Task<List<OrganizationalBrandingLocalization>> ListBrandingLocalizationsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<OrganizationalBrandingLocalization>();
        var organizationId = await GetOrganizationIdAsync(cancellationToken);

        var response = await _graphClient.Organization[organizationId].Branding.Localizations
            .GetAsync(req =>
            {
                req.QueryParameters.Top = 200;
            }, cancellationToken);

        while (response != null)
        {
            if (response.Value != null)
                result.AddRange(response.Value);

            if (!string.IsNullOrEmpty(response.OdataNextLink))
            {
                response = await _graphClient.Organization[organizationId].Branding.Localizations
                    .WithUrl(response.OdataNextLink)
                    .GetAsync(cancellationToken: cancellationToken);
            }
            else
            {
                break;
            }
        }

        return result;
    }

    public async Task<OrganizationalBrandingLocalization?> GetBrandingLocalizationAsync(string id, CancellationToken cancellationToken = default)
    {
        var organizationId = await GetOrganizationIdAsync(cancellationToken);
        return await _graphClient.Organization[organizationId].Branding.Localizations[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<OrganizationalBrandingLocalization> CreateBrandingLocalizationAsync(OrganizationalBrandingLocalization localization, CancellationToken cancellationToken = default)
    {
        var organizationId = await GetOrganizationIdAsync(cancellationToken);
        var result = await _graphClient.Organization[organizationId].Branding.Localizations
            .PostAsync(localization, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create branding localization");
    }

    public async Task<OrganizationalBrandingLocalization> UpdateBrandingLocalizationAsync(OrganizationalBrandingLocalization localization, CancellationToken cancellationToken = default)
    {
        var organizationId = await GetOrganizationIdAsync(cancellationToken);
        var id = localization.Id ?? throw new ArgumentException("Branding localization must have an ID for update");

        var result = await _graphClient.Organization[organizationId].Branding.Localizations[id]
            .PatchAsync(localization, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetBrandingLocalizationAsync(id, cancellationToken), "branding localization");
    }

    public async Task DeleteBrandingLocalizationAsync(string id, CancellationToken cancellationToken = default)
    {
        var organizationId = await GetOrganizationIdAsync(cancellationToken);
        await _graphClient.Organization[organizationId].Branding.Localizations[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }
}
