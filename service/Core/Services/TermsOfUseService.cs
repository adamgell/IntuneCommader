using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class TermsOfUseService : ITermsOfUseService
{
    private readonly GraphServiceClient _graphClient;

    public TermsOfUseService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<Agreement>> ListTermsOfUseAgreementsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<Agreement>();

        var response = await _graphClient.IdentityGovernance.TermsOfUse.Agreements
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
                response = await _graphClient.IdentityGovernance.TermsOfUse.Agreements
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

    public async Task<Agreement?> GetTermsOfUseAgreementAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.IdentityGovernance.TermsOfUse.Agreements[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<Agreement> CreateTermsOfUseAgreementAsync(Agreement agreement, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.IdentityGovernance.TermsOfUse.Agreements
            .PostAsync(agreement, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create terms of use agreement");
    }

    public async Task<Agreement> UpdateTermsOfUseAgreementAsync(Agreement agreement, CancellationToken cancellationToken = default)
    {
        var id = agreement.Id ?? throw new ArgumentException("Terms of use agreement must have an ID for update");

        var result = await _graphClient.IdentityGovernance.TermsOfUse.Agreements[id]
            .PatchAsync(agreement, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetTermsOfUseAgreementAsync(id, cancellationToken), "terms of use agreement");
    }

    public async Task DeleteTermsOfUseAgreementAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.IdentityGovernance.TermsOfUse.Agreements[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }
}