using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class ReusablePolicySettingService : IReusablePolicySettingService
{
    private readonly GraphServiceClient _graphClient;

    public ReusablePolicySettingService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<DeviceManagementReusablePolicySetting>> ListReusablePolicySettingsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<DeviceManagementReusablePolicySetting>();

        var response = await _graphClient.DeviceManagement.ReusablePolicySettings
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
                response = await _graphClient.DeviceManagement.ReusablePolicySettings
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

    public async Task<DeviceManagementReusablePolicySetting?> GetReusablePolicySettingAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceManagement.ReusablePolicySettings[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<DeviceManagementReusablePolicySetting> CreateReusablePolicySettingAsync(DeviceManagementReusablePolicySetting setting, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.DeviceManagement.ReusablePolicySettings
            .PostAsync(setting, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create reusable policy setting");
    }

    public async Task<DeviceManagementReusablePolicySetting> UpdateReusablePolicySettingAsync(DeviceManagementReusablePolicySetting setting, CancellationToken cancellationToken = default)
    {
        var id = setting.Id ?? throw new ArgumentException("Reusable policy setting must have an ID for update");

        var result = await _graphClient.DeviceManagement.ReusablePolicySettings[id]
            .PatchAsync(setting, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetReusablePolicySettingAsync(id, cancellationToken), "reusable policy setting");
    }

    public async Task DeleteReusablePolicySettingAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.ReusablePolicySettings[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }
}
