using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class CloudPcUserSettingsService : ICloudPcUserSettingsService
{
    private readonly GraphServiceClient _graphClient;

    public CloudPcUserSettingsService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<CloudPcUserSetting>> ListUserSettingsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<CloudPcUserSetting>();

        var response = await _graphClient.DeviceManagement.VirtualEndpoint.UserSettings
            .GetAsync(cancellationToken: cancellationToken);

        while (response != null)
        {
            if (response.Value != null)
                result.AddRange(response.Value);

            if (!string.IsNullOrEmpty(response.OdataNextLink))
            {
                response = await _graphClient.DeviceManagement.VirtualEndpoint.UserSettings
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

    public async Task<CloudPcUserSetting?> GetUserSettingAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceManagement.VirtualEndpoint.UserSettings[id]
            .GetAsync(cancellationToken: cancellationToken);
    }
}
