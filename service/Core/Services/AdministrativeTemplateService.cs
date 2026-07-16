using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class AdministrativeTemplateService : IAdministrativeTemplateService
{
    private readonly GraphServiceClient _graphClient;

    public AdministrativeTemplateService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<GroupPolicyConfiguration>> ListAdministrativeTemplatesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<GroupPolicyConfiguration>();

        var response = await _graphClient.DeviceManagement.GroupPolicyConfigurations
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
                response = await _graphClient.DeviceManagement.GroupPolicyConfigurations
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

    public async Task<GroupPolicyConfiguration?> GetAdministrativeTemplateAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceManagement.GroupPolicyConfigurations[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<GroupPolicyConfiguration> CreateAdministrativeTemplateAsync(GroupPolicyConfiguration template, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.DeviceManagement.GroupPolicyConfigurations
            .PostAsync(template, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create administrative template");
    }

    public async Task<GroupPolicyConfiguration> UpdateAdministrativeTemplateAsync(GroupPolicyConfiguration template, CancellationToken cancellationToken = default)
    {
        var id = template.Id ?? throw new ArgumentException("Administrative template must have an ID for update");

        var result = await _graphClient.DeviceManagement.GroupPolicyConfigurations[id]
            .PatchAsync(template, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetAdministrativeTemplateAsync(id, cancellationToken), "administrative template");
    }

    public async Task DeleteAdministrativeTemplateAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.GroupPolicyConfigurations[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }

    public async Task<List<GroupPolicyConfigurationAssignment>> GetAssignmentsAsync(string templateId, CancellationToken cancellationToken = default)
    {
        var response = await _graphClient.DeviceManagement.GroupPolicyConfigurations[templateId]
            .Assignments.GetAsync(cancellationToken: cancellationToken);

        return response?.Value ?? [];
    }

    public async Task AssignAdministrativeTemplateAsync(string templateId, List<GroupPolicyConfigurationAssignment> assignments, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.GroupPolicyConfigurations[templateId]
            .Assign.PostAsAssignPostResponseAsync(
                new Microsoft.Graph.Beta.DeviceManagement.GroupPolicyConfigurations.Item.Assign.AssignPostRequestBody
                {
                    Assignments = assignments
                },
                cancellationToken: cancellationToken);
    }
}
