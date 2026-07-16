using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IFeatureUpdateProfileService
{
    Task<List<WindowsFeatureUpdateProfile>> ListFeatureUpdateProfilesAsync(CancellationToken cancellationToken = default);
    Task<WindowsFeatureUpdateProfile?> GetFeatureUpdateProfileAsync(string id, CancellationToken cancellationToken = default);
    Task<WindowsFeatureUpdateProfile> CreateFeatureUpdateProfileAsync(WindowsFeatureUpdateProfile profile, CancellationToken cancellationToken = default);
    Task<WindowsFeatureUpdateProfile> UpdateFeatureUpdateProfileAsync(WindowsFeatureUpdateProfile profile, CancellationToken cancellationToken = default);
    Task DeleteFeatureUpdateProfileAsync(string id, CancellationToken cancellationToken = default);
}
