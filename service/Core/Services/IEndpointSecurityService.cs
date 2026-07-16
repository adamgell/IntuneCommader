using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IEndpointSecurityService
{
    Task<List<DeviceManagementIntent>> ListEndpointSecurityIntentsAsync(CancellationToken cancellationToken = default);
    Task<DeviceManagementIntent?> GetEndpointSecurityIntentAsync(string id, CancellationToken cancellationToken = default);
    Task<DeviceManagementIntent> CreateEndpointSecurityIntentAsync(DeviceManagementIntent intent, CancellationToken cancellationToken = default);
    Task<DeviceManagementIntent> UpdateEndpointSecurityIntentAsync(DeviceManagementIntent intent, CancellationToken cancellationToken = default);
    Task DeleteEndpointSecurityIntentAsync(string id, CancellationToken cancellationToken = default);
    Task<List<DeviceManagementIntentAssignment>> GetAssignmentsAsync(string intentId, CancellationToken cancellationToken = default);
    Task AssignIntentAsync(string intentId, List<DeviceManagementIntentAssignment> assignments, CancellationToken cancellationToken = default);
}
