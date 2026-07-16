using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class NamedLocationService : INamedLocationService
{
    private readonly GraphServiceClient _graphClient;

    public NamedLocationService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<NamedLocation>> ListNamedLocationsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<NamedLocation>();

        var response = await _graphClient.Identity.ConditionalAccess.NamedLocations
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
                response = await _graphClient.Identity.ConditionalAccess.NamedLocations
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

    public async Task<NamedLocation?> GetNamedLocationAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.Identity.ConditionalAccess.NamedLocations[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<NamedLocation> CreateNamedLocationAsync(NamedLocation namedLocation, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.Identity.ConditionalAccess.NamedLocations
            .PostAsync(namedLocation, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create named location");
    }

    public async Task<NamedLocation> UpdateNamedLocationAsync(NamedLocation namedLocation, CancellationToken cancellationToken = default)
    {
        var id = namedLocation.Id ?? throw new ArgumentException("Named location must have an ID for update");

        NamedLocation patchPayload = namedLocation switch
        {
            IpNamedLocation ip => new IpNamedLocation
            {
                DisplayName = ip.DisplayName,
                IsTrusted = ip.IsTrusted,
                IpRanges = ip.IpRanges,
                OdataType = string.IsNullOrWhiteSpace(ip.OdataType)
                    ? "#microsoft.graph.ipNamedLocation"
                    : ip.OdataType,
            },
            CountryNamedLocation country => new CountryNamedLocation
            {
                DisplayName = country.DisplayName,
                CountriesAndRegions = country.CountriesAndRegions,
                CountryLookupMethod = country.CountryLookupMethod,
                IncludeUnknownCountriesAndRegions = country.IncludeUnknownCountriesAndRegions,
                OdataType = string.IsNullOrWhiteSpace(country.OdataType)
                    ? "#microsoft.graph.countryNamedLocation"
                    : country.OdataType,
            },
            _ => new NamedLocation
            {
                DisplayName = namedLocation.DisplayName,
                OdataType = namedLocation.OdataType,
            }
        };

        var result = await _graphClient.Identity.ConditionalAccess.NamedLocations[id]
            .PatchAsync(patchPayload, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetNamedLocationAsync(id, cancellationToken), "named location");
    }

    public async Task DeleteNamedLocationAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.Identity.ConditionalAccess.NamedLocations[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }
}
