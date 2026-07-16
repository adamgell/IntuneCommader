using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IDeviceCategoryService
{
    Task<List<DeviceCategory>> ListDeviceCategoriesAsync(CancellationToken cancellationToken = default);
    Task<DeviceCategory?> GetDeviceCategoryAsync(string id, CancellationToken cancellationToken = default);
    Task<DeviceCategory> CreateDeviceCategoryAsync(DeviceCategory category, CancellationToken cancellationToken = default);
    Task<DeviceCategory> UpdateDeviceCategoryAsync(DeviceCategory category, CancellationToken cancellationToken = default);
    Task DeleteDeviceCategoryAsync(string id, CancellationToken cancellationToken = default);
}
