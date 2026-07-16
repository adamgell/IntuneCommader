using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IDriverUpdateProfileService
{
    Task<List<WindowsDriverUpdateProfile>> ListDriverUpdateProfilesAsync(CancellationToken cancellationToken = default);
    Task<WindowsDriverUpdateProfile?> GetDriverUpdateProfileAsync(string id, CancellationToken cancellationToken = default);
    Task<WindowsDriverUpdateProfile> CreateDriverUpdateProfileAsync(WindowsDriverUpdateProfile profile, CancellationToken cancellationToken = default);
    Task<WindowsDriverUpdateProfile> UpdateDriverUpdateProfileAsync(WindowsDriverUpdateProfile profile, CancellationToken cancellationToken = default);
    Task DeleteDriverUpdateProfileAsync(string id, CancellationToken cancellationToken = default);
}
