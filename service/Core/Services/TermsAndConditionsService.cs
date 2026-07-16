using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class TermsAndConditionsService : ITermsAndConditionsService
{
    private readonly GraphServiceClient _graphClient;

    public TermsAndConditionsService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<TermsAndConditions>> ListTermsAndConditionsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<TermsAndConditions>();

        var response = await _graphClient.DeviceManagement.TermsAndConditions
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
                response = await _graphClient.DeviceManagement.TermsAndConditions
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

    public async Task<TermsAndConditions?> GetTermsAndConditionsAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceManagement.TermsAndConditions[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<TermsAndConditions> CreateTermsAndConditionsAsync(TermsAndConditions termsAndConditions, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.DeviceManagement.TermsAndConditions
            .PostAsync(termsAndConditions, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create terms and conditions");
    }

    public async Task<TermsAndConditions> UpdateTermsAndConditionsAsync(TermsAndConditions termsAndConditions, CancellationToken cancellationToken = default)
    {
        var id = termsAndConditions.Id ?? throw new ArgumentException("Terms and conditions must have an ID for update");

        var result = await _graphClient.DeviceManagement.TermsAndConditions[id]
            .PatchAsync(termsAndConditions, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetTermsAndConditionsAsync(id, cancellationToken), "terms and conditions");
    }

    public async Task DeleteTermsAndConditionsAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.TermsAndConditions[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }
}
