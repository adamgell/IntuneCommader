using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class DeviceCategoryService : IDeviceCategoryService
{
    private readonly GraphServiceClient _graphClient;

    public DeviceCategoryService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<DeviceCategory>> ListDeviceCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<DeviceCategory>();

        var response = await _graphClient.DeviceManagement.DeviceCategories
            .GetAsync(req =>
            {
                req.QueryParameters.Top = 999;
            }, cancellationToken);

        while (response != null)
        {
            if (response.Value != null)
                result.AddRange(response.Value);

            if (!string.IsNullOrEmpty(response.OdataNextLink))
            {
                response = await _graphClient.DeviceManagement.DeviceCategories
                    .WithUrl(response.OdataNextLink)
                    .GetAsync(cancellationToken: cancellationToken);
            }
            else
            {
                break;
            }
        }

        return result;
    }

    public async Task<DeviceCategory?> GetDeviceCategoryAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceManagement.DeviceCategories[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<DeviceCategory> CreateDeviceCategoryAsync(DeviceCategory category, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.DeviceManagement.DeviceCategories
            .PostAsync(category, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create device category");
    }

    public async Task<DeviceCategory> UpdateDeviceCategoryAsync(DeviceCategory category, CancellationToken cancellationToken = default)
    {
        var id = category.Id ?? throw new ArgumentException("Device category must have an ID for update");

        var result = await _graphClient.DeviceManagement.DeviceCategories[id]
            .PatchAsync(category, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetDeviceCategoryAsync(id, cancellationToken), "device category");
    }

    public async Task DeleteDeviceCategoryAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.DeviceCategories[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }
}
