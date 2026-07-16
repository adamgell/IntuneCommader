using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class FeatureUpdateProfileService : IFeatureUpdateProfileService
{
    private readonly GraphServiceClient _graphClient;

    public FeatureUpdateProfileService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<WindowsFeatureUpdateProfile>> ListFeatureUpdateProfilesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<WindowsFeatureUpdateProfile>();

        var response = await _graphClient.DeviceManagement.WindowsFeatureUpdateProfiles
            .GetAsync(req =>
            {
                req.QueryParameters.Top = 200;
            }, cancellationToken);

        while (response != null)
        {
            if (response.Value != null)
                result.AddRange(response.Value);

            if (!string.IsNullOrEmpty(response.OdataNextLink))
            {
                response = await _graphClient.DeviceManagement.WindowsFeatureUpdateProfiles
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

    public async Task<WindowsFeatureUpdateProfile?> GetFeatureUpdateProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceManagement.WindowsFeatureUpdateProfiles[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<WindowsFeatureUpdateProfile> CreateFeatureUpdateProfileAsync(WindowsFeatureUpdateProfile profile, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.DeviceManagement.WindowsFeatureUpdateProfiles
            .PostAsync(profile, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create feature update profile");
    }

    public async Task<WindowsFeatureUpdateProfile> UpdateFeatureUpdateProfileAsync(WindowsFeatureUpdateProfile profile, CancellationToken cancellationToken = default)
    {
        var id = profile.Id ?? throw new ArgumentException("Feature update profile must have an ID for update");

        var result = await _graphClient.DeviceManagement.WindowsFeatureUpdateProfiles[id]
            .PatchAsync(profile, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetFeatureUpdateProfileAsync(id, cancellationToken), "feature update profile");
    }

    public async Task DeleteFeatureUpdateProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.WindowsFeatureUpdateProfiles[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }
}
