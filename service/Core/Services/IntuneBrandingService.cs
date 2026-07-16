using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class IntuneBrandingService : IIntuneBrandingService
{
    private readonly GraphServiceClient _graphClient;

    public IntuneBrandingService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<IntuneBrandingProfile>> ListIntuneBrandingProfilesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<IntuneBrandingProfile>();

        var response = await _graphClient.DeviceManagement.IntuneBrandingProfiles
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
                response = await _graphClient.DeviceManagement.IntuneBrandingProfiles
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

    public async Task<IntuneBrandingProfile?> GetIntuneBrandingProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceManagement.IntuneBrandingProfiles[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<IntuneBrandingProfile> CreateIntuneBrandingProfileAsync(IntuneBrandingProfile profile, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.DeviceManagement.IntuneBrandingProfiles
            .PostAsync(profile, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create Intune branding profile");
    }

    public async Task<IntuneBrandingProfile> UpdateIntuneBrandingProfileAsync(IntuneBrandingProfile profile, CancellationToken cancellationToken = default)
    {
        var id = profile.Id ?? throw new ArgumentException("Intune branding profile must have an ID for update");

        var result = await _graphClient.DeviceManagement.IntuneBrandingProfiles[id]
            .PatchAsync(profile, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetIntuneBrandingProfileAsync(id, cancellationToken), "Intune branding profile");
    }

    public async Task DeleteIntuneBrandingProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.IntuneBrandingProfiles[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }
}
