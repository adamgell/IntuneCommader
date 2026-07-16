using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IAzureBrandingService
{
    Task<List<OrganizationalBrandingLocalization>> ListBrandingLocalizationsAsync(CancellationToken cancellationToken = default);
    Task<OrganizationalBrandingLocalization?> GetBrandingLocalizationAsync(string id, CancellationToken cancellationToken = default);
    Task<OrganizationalBrandingLocalization> CreateBrandingLocalizationAsync(OrganizationalBrandingLocalization localization, CancellationToken cancellationToken = default);
    Task<OrganizationalBrandingLocalization> UpdateBrandingLocalizationAsync(OrganizationalBrandingLocalization localization, CancellationToken cancellationToken = default);
    Task DeleteBrandingLocalizationAsync(string id, CancellationToken cancellationToken = default);
}
