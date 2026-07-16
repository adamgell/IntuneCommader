using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IDeviceService
{
    Task<List<ManagedDevice>> SearchDevicesAsync(string query, CancellationToken cancellationToken = default);
    Task<List<ManagedDevice>> ListAllDevicesAsync(CancellationToken cancellationToken = default);
    Task<ManagedDevice?> GetDeviceAsync(string deviceId, CancellationToken cancellationToken = default);
}
