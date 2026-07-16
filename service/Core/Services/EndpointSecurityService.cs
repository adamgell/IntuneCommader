using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class EndpointSecurityService : IEndpointSecurityService
{
    private readonly GraphServiceClient _graphClient;

    public EndpointSecurityService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<DeviceManagementIntent>> ListEndpointSecurityIntentsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<DeviceManagementIntent>();

        var response = await _graphClient.DeviceManagement.Intents
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
                response = await _graphClient.DeviceManagement.Intents
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

    public async Task<DeviceManagementIntent?> GetEndpointSecurityIntentAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceManagement.Intents[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<DeviceManagementIntent> CreateEndpointSecurityIntentAsync(DeviceManagementIntent intent, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.DeviceManagement.Intents
            .PostAsync(intent, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create endpoint security intent");
    }

    public async Task<DeviceManagementIntent> UpdateEndpointSecurityIntentAsync(DeviceManagementIntent intent, CancellationToken cancellationToken = default)
    {
        var id = intent.Id ?? throw new ArgumentException("Endpoint security intent must have an ID for update");

        var result = await _graphClient.DeviceManagement.Intents[id]
            .PatchAsync(intent, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetEndpointSecurityIntentAsync(id, cancellationToken), "endpoint security intent");
    }

    public async Task DeleteEndpointSecurityIntentAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.Intents[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }

    public async Task<List<DeviceManagementIntentAssignment>> GetAssignmentsAsync(string intentId, CancellationToken cancellationToken = default)
    {
        var response = await _graphClient.DeviceManagement.Intents[intentId]
            .Assignments.GetAsync(cancellationToken: cancellationToken);

        return response?.Value ?? [];
    }

    public async Task AssignIntentAsync(string intentId, List<DeviceManagementIntentAssignment> assignments, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.Intents[intentId]
            .Assign.PostAsync(
                new Microsoft.Graph.Beta.DeviceManagement.Intents.Item.Assign.AssignPostRequestBody
                {
                    Assignments = assignments
                },
                cancellationToken: cancellationToken);
    }
}
