using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IMacCustomAttributeService
{
    Task<List<DeviceCustomAttributeShellScript>> ListMacCustomAttributesAsync(CancellationToken cancellationToken = default);
    Task<DeviceCustomAttributeShellScript?> GetMacCustomAttributeAsync(string id, CancellationToken cancellationToken = default);
    Task<DeviceCustomAttributeShellScript> CreateMacCustomAttributeAsync(DeviceCustomAttributeShellScript script, CancellationToken cancellationToken = default);
    Task<DeviceCustomAttributeShellScript> UpdateMacCustomAttributeAsync(DeviceCustomAttributeShellScript script, CancellationToken cancellationToken = default);
    Task DeleteMacCustomAttributeAsync(string id, CancellationToken cancellationToken = default);
}
