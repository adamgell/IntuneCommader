using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class DriverUpdateProfileService : IDriverUpdateProfileService
{
    private readonly GraphServiceClient _graphClient;

    public DriverUpdateProfileService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<WindowsDriverUpdateProfile>> ListDriverUpdateProfilesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<WindowsDriverUpdateProfile>();

        var response = await _graphClient.DeviceManagement.WindowsDriverUpdateProfiles
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
                response = await _graphClient.DeviceManagement.WindowsDriverUpdateProfiles
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

    public async Task<WindowsDriverUpdateProfile?> GetDriverUpdateProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceManagement.WindowsDriverUpdateProfiles[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<WindowsDriverUpdateProfile> CreateDriverUpdateProfileAsync(WindowsDriverUpdateProfile profile, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.DeviceManagement.WindowsDriverUpdateProfiles
            .PostAsync(profile, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create driver update profile");
    }

    public async Task<WindowsDriverUpdateProfile> UpdateDriverUpdateProfileAsync(WindowsDriverUpdateProfile profile, CancellationToken cancellationToken = default)
    {
        var id = profile.Id ?? throw new ArgumentException("Driver update profile must have an ID for update");

        var result = await _graphClient.DeviceManagement.WindowsDriverUpdateProfiles[id]
            .PatchAsync(profile, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetDriverUpdateProfileAsync(id, cancellationToken), "driver update profile");
    }

    public async Task DeleteDriverUpdateProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.WindowsDriverUpdateProfiles[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }
}
