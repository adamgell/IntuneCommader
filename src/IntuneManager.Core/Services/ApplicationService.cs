using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace IntuneManager.Core.Services;

public class ApplicationService : IApplicationService
{
    private readonly GraphServiceClient _graphClient;

    public ApplicationService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<MobileApp>> ListApplicationsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<MobileApp>();

        var response = await _graphClient.DeviceAppManagement.MobileApps
            .GetAsync(cancellationToken: cancellationToken);

        if (response?.Value != null)
        {
            result.AddRange(response.Value);

            var pageIterator = PageIterator<MobileApp, MobileAppCollectionResponse>
                .CreatePageIterator(_graphClient, response, item =>
                {
                    result.Add(item);
                    return true;
                });

            await pageIterator.IterateAsync(cancellationToken);
        }

        return result;
    }

    public async Task<MobileApp?> GetApplicationAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceAppManagement.MobileApps[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<List<MobileAppAssignment>> GetAssignmentsAsync(string appId, CancellationToken cancellationToken = default)
    {
        var response = await _graphClient.DeviceAppManagement.MobileApps[appId]
            .Assignments.GetAsync(cancellationToken: cancellationToken);

        return response?.Value ?? [];
    }
}
