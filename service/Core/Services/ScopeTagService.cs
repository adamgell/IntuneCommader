using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class ScopeTagService : IScopeTagService
{
    private readonly GraphServiceClient _graphClient;

    public ScopeTagService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<RoleScopeTag>> ListScopeTagsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<RoleScopeTag>();

        var response = await _graphClient.DeviceManagement.RoleScopeTags
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
                response = await _graphClient.DeviceManagement.RoleScopeTags
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

    public async Task<RoleScopeTag?> GetScopeTagAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceManagement.RoleScopeTags[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<RoleScopeTag> CreateScopeTagAsync(RoleScopeTag scopeTag, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.DeviceManagement.RoleScopeTags
            .PostAsync(scopeTag, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create scope tag");
    }

    public async Task<RoleScopeTag> UpdateScopeTagAsync(RoleScopeTag scopeTag, CancellationToken cancellationToken = default)
    {
        var id = scopeTag.Id ?? throw new ArgumentException("Scope tag must have an ID for update");

        var result = await _graphClient.DeviceManagement.RoleScopeTags[id]
            .PatchAsync(scopeTag, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetScopeTagAsync(id, cancellationToken), "scope tag");
    }

    public async Task DeleteScopeTagAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.RoleScopeTags[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }
}
