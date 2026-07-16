using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class QualityUpdateProfileService : IQualityUpdateProfileService
{
    private readonly GraphServiceClient _graphClient;

    public QualityUpdateProfileService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<WindowsQualityUpdateProfile>> ListQualityUpdateProfilesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<WindowsQualityUpdateProfile>();

        var response = await _graphClient.DeviceManagement.WindowsQualityUpdateProfiles
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
                response = await _graphClient.DeviceManagement.WindowsQualityUpdateProfiles
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

    public async Task<WindowsQualityUpdateProfile?> GetQualityUpdateProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceManagement.WindowsQualityUpdateProfiles[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<WindowsQualityUpdateProfile> CreateQualityUpdateProfileAsync(WindowsQualityUpdateProfile profile, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.DeviceManagement.WindowsQualityUpdateProfiles
            .PostAsync(profile, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create quality update profile");
    }

    public async Task<WindowsQualityUpdateProfile> UpdateQualityUpdateProfileAsync(WindowsQualityUpdateProfile profile, CancellationToken cancellationToken = default)
    {
        var id = profile.Id ?? throw new ArgumentException("Quality update profile must have an ID for update");

        var result = await _graphClient.DeviceManagement.WindowsQualityUpdateProfiles[id]
            .PatchAsync(profile, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetQualityUpdateProfileAsync(id, cancellationToken), "quality update profile");
    }

    public async Task DeleteQualityUpdateProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.WindowsQualityUpdateProfiles[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }
}
