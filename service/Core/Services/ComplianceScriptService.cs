using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class ComplianceScriptService : IComplianceScriptService
{
    private readonly GraphServiceClient _graphClient;

    public ComplianceScriptService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<DeviceComplianceScript>> ListComplianceScriptsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<DeviceComplianceScript>();

        var response = await _graphClient.DeviceManagement.DeviceComplianceScripts
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
                response = await _graphClient.DeviceManagement.DeviceComplianceScripts
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

    public async Task<DeviceComplianceScript?> GetComplianceScriptAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceManagement.DeviceComplianceScripts[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<DeviceComplianceScript> CreateComplianceScriptAsync(DeviceComplianceScript script, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.DeviceManagement.DeviceComplianceScripts
            .PostAsync(script, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create compliance script");
    }

    public async Task<DeviceComplianceScript> UpdateComplianceScriptAsync(DeviceComplianceScript script, CancellationToken cancellationToken = default)
    {
        var id = script.Id ?? throw new ArgumentException("Compliance script must have an ID for update");

        var result = await _graphClient.DeviceManagement.DeviceComplianceScripts[id]
            .PatchAsync(script, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetComplianceScriptAsync(id, cancellationToken), "compliance script");
    }

    public async Task DeleteComplianceScriptAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.DeviceComplianceScripts[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }
}
