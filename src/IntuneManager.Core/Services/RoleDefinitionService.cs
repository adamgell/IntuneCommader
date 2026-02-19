using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace IntuneManager.Core.Services;

public class RoleDefinitionService : IRoleDefinitionService
{
    private readonly GraphServiceClient _graphClient;

    public RoleDefinitionService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<RoleDefinition>> ListRoleDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<RoleDefinition>();

        var response = await _graphClient.DeviceManagement.RoleDefinitions
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
                response = await _graphClient.DeviceManagement.RoleDefinitions
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

    public async Task<RoleDefinition?> GetRoleDefinitionAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceManagement.RoleDefinitions[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<RoleDefinition> CreateRoleDefinitionAsync(RoleDefinition roleDefinition, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.DeviceManagement.RoleDefinitions
            .PostAsync(roleDefinition, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create role definition");
    }

    public async Task<RoleDefinition> UpdateRoleDefinitionAsync(RoleDefinition roleDefinition, CancellationToken cancellationToken = default)
    {
        var id = roleDefinition.Id ?? throw new ArgumentException("Role definition must have an ID for update");

        var result = await _graphClient.DeviceManagement.RoleDefinitions[id]
            .PatchAsync(roleDefinition, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetRoleDefinitionAsync(id, cancellationToken), "role definition");
    }

    public async Task DeleteRoleDefinitionAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.RoleDefinitions[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }
}
