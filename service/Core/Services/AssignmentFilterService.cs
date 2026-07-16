using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class AssignmentFilterService : IAssignmentFilterService
{
    private readonly GraphServiceClient _graphClient;

    public AssignmentFilterService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<DeviceAndAppManagementAssignmentFilter>> ListFiltersAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<DeviceAndAppManagementAssignmentFilter>();

        var response = await _graphClient.DeviceManagement.AssignmentFilters
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
                response = await _graphClient.DeviceManagement.AssignmentFilters
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

    public async Task<DeviceAndAppManagementAssignmentFilter?> GetFilterAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceManagement.AssignmentFilters[id]
            .GetAsync(cancellationToken: cancellationToken);
    }
}
