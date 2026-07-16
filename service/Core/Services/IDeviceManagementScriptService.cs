using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IDeviceManagementScriptService
{
    Task<List<DeviceManagementScript>> ListDeviceManagementScriptsAsync(CancellationToken cancellationToken = default);
    Task<DeviceManagementScript?> GetDeviceManagementScriptAsync(string id, CancellationToken cancellationToken = default);
    Task<DeviceManagementScript> CreateDeviceManagementScriptAsync(DeviceManagementScript script, CancellationToken cancellationToken = default);
    Task<DeviceManagementScript> UpdateDeviceManagementScriptAsync(DeviceManagementScript script, CancellationToken cancellationToken = default);
    Task DeleteDeviceManagementScriptAsync(string id, CancellationToken cancellationToken = default);
    Task<List<DeviceManagementScriptAssignment>> GetAssignmentsAsync(string scriptId, CancellationToken cancellationToken = default);
    Task AssignScriptAsync(string scriptId, List<DeviceManagementScriptAssignment> assignments, CancellationToken cancellationToken = default);
}
