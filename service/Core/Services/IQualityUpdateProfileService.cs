using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IQualityUpdateProfileService
{
    Task<List<WindowsQualityUpdateProfile>> ListQualityUpdateProfilesAsync(CancellationToken cancellationToken = default);
    Task<WindowsQualityUpdateProfile?> GetQualityUpdateProfileAsync(string id, CancellationToken cancellationToken = default);
    Task<WindowsQualityUpdateProfile> CreateQualityUpdateProfileAsync(WindowsQualityUpdateProfile profile, CancellationToken cancellationToken = default);
    Task<WindowsQualityUpdateProfile> UpdateQualityUpdateProfileAsync(WindowsQualityUpdateProfile profile, CancellationToken cancellationToken = default);
    Task DeleteQualityUpdateProfileAsync(string id, CancellationToken cancellationToken = default);
}
