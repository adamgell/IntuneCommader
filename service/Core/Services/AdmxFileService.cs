using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class AdmxFileService : IAdmxFileService
{
    private readonly GraphServiceClient _graphClient;

    public AdmxFileService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<GroupPolicyUploadedDefinitionFile>> ListAdmxFilesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<GroupPolicyUploadedDefinitionFile>();

        var response = await _graphClient.DeviceManagement.GroupPolicyUploadedDefinitionFiles
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
                response = await _graphClient.DeviceManagement.GroupPolicyUploadedDefinitionFiles
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

    public async Task<GroupPolicyUploadedDefinitionFile?> GetAdmxFileAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceManagement.GroupPolicyUploadedDefinitionFiles[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<GroupPolicyUploadedDefinitionFile> CreateAdmxFileAsync(GroupPolicyUploadedDefinitionFile admxFile, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.DeviceManagement.GroupPolicyUploadedDefinitionFiles
            .PostAsync(admxFile, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create ADMX definition file");
    }

    public async Task DeleteAdmxFileAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.GroupPolicyUploadedDefinitionFiles[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }
}
