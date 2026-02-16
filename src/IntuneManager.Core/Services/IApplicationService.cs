using Microsoft.Graph.Models;

namespace IntuneManager.Core.Services;

public interface IApplicationService
{
    Task<List<MobileApp>> ListApplicationsAsync(CancellationToken cancellationToken = default);
    Task<MobileApp?> GetApplicationAsync(string id, CancellationToken cancellationToken = default);
    Task<List<MobileAppAssignment>> GetAssignmentsAsync(string appId, CancellationToken cancellationToken = default);
}
