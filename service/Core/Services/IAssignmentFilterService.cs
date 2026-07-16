using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IAssignmentFilterService
{
    Task<List<DeviceAndAppManagementAssignmentFilter>> ListFiltersAsync(CancellationToken cancellationToken = default);
    Task<DeviceAndAppManagementAssignmentFilter?> GetFilterAsync(string id, CancellationToken cancellationToken = default);
}
