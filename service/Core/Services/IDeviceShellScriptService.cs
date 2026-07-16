using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IDeviceShellScriptService
{
    Task<List<DeviceShellScript>> ListDeviceShellScriptsAsync(CancellationToken cancellationToken = default);
    Task<DeviceShellScript?> GetDeviceShellScriptAsync(string id, CancellationToken cancellationToken = default);
    Task<DeviceShellScript> CreateDeviceShellScriptAsync(DeviceShellScript script, CancellationToken cancellationToken = default);
    Task<DeviceShellScript> UpdateDeviceShellScriptAsync(DeviceShellScript script, CancellationToken cancellationToken = default);
    Task DeleteDeviceShellScriptAsync(string id, CancellationToken cancellationToken = default);
    Task<List<DeviceManagementScriptAssignment>> GetAssignmentsAsync(string scriptId, CancellationToken cancellationToken = default);
    Task AssignScriptAsync(string scriptId, List<DeviceManagementScriptAssignment> assignments, CancellationToken cancellationToken = default);
}
