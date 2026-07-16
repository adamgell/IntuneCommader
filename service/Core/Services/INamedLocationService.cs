using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface INamedLocationService
{
    Task<List<NamedLocation>> ListNamedLocationsAsync(CancellationToken cancellationToken = default);
    Task<NamedLocation?> GetNamedLocationAsync(string id, CancellationToken cancellationToken = default);
    Task<NamedLocation> CreateNamedLocationAsync(NamedLocation namedLocation, CancellationToken cancellationToken = default);
    Task<NamedLocation> UpdateNamedLocationAsync(NamedLocation namedLocation, CancellationToken cancellationToken = default);
    Task DeleteNamedLocationAsync(string id, CancellationToken cancellationToken = default);
}
