using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class VppTokenService : IVppTokenService
{
    private readonly GraphServiceClient _graphClient;

    public VppTokenService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<VppToken>> ListVppTokensAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<VppToken>();

        var response = await _graphClient.DeviceAppManagement.VppTokens
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
                response = await _graphClient.DeviceAppManagement.VppTokens
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

    public async Task<VppToken?> GetVppTokenAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceAppManagement.VppTokens[id]
            .GetAsync(cancellationToken: cancellationToken);
    }
}
