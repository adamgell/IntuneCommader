using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IApplicationService
{
    Task<List<MobileApp>> ListApplicationsAsync(CancellationToken cancellationToken = default);
    Task<MobileApp?> GetApplicationAsync(string id, CancellationToken cancellationToken = default);
    Task<List<MobileAppAssignment>> GetAssignmentsAsync(string appId, CancellationToken cancellationToken = default);
    Task AssignApplicationAsync(string appId, List<MobileAppAssignment> assignments, CancellationToken cancellationToken = default);
}
