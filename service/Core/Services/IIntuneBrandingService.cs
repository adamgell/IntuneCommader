using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IIntuneBrandingService
{
    Task<List<IntuneBrandingProfile>> ListIntuneBrandingProfilesAsync(CancellationToken cancellationToken = default);
    Task<IntuneBrandingProfile?> GetIntuneBrandingProfileAsync(string id, CancellationToken cancellationToken = default);
    Task<IntuneBrandingProfile> CreateIntuneBrandingProfileAsync(IntuneBrandingProfile profile, CancellationToken cancellationToken = default);
    Task<IntuneBrandingProfile> UpdateIntuneBrandingProfileAsync(IntuneBrandingProfile profile, CancellationToken cancellationToken = default);
    Task DeleteIntuneBrandingProfileAsync(string id, CancellationToken cancellationToken = default);
}
