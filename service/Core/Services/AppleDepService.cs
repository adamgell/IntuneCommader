using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class AppleDepService : IAppleDepService
{
    private readonly GraphServiceClient _graphClient;

    public AppleDepService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<DepOnboardingSetting>> ListDepOnboardingSettingsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<DepOnboardingSetting>();

        var response = await _graphClient.DeviceManagement.DepOnboardingSettings
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
                response = await _graphClient.DeviceManagement.DepOnboardingSettings
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

    public async Task<DepOnboardingSetting?> GetDepOnboardingSettingAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceManagement.DepOnboardingSettings[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<List<ImportedAppleDeviceIdentity>> ListImportedAppleDeviceIdentitiesAsync(string depOnboardingSettingId, CancellationToken cancellationToken = default)
    {
        var result = new List<ImportedAppleDeviceIdentity>();

        var response = await _graphClient.DeviceManagement.DepOnboardingSettings[depOnboardingSettingId]
            .ImportedAppleDeviceIdentities
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
                response = await _graphClient.DeviceManagement.DepOnboardingSettings[depOnboardingSettingId]
                    .ImportedAppleDeviceIdentities
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
}
