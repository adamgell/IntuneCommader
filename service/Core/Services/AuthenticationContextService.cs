using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class AuthenticationContextService : IAuthenticationContextService
{
    private readonly GraphServiceClient _graphClient;

    public AuthenticationContextService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<AuthenticationContextClassReference>> ListAuthenticationContextsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<AuthenticationContextClassReference>();

        var response = await _graphClient.Identity.ConditionalAccess.AuthenticationContextClassReferences
            .GetAsync(cancellationToken: cancellationToken);

        while (response != null)
        {
            if (response.Value != null)
                result.AddRange(response.Value);

            if (!string.IsNullOrEmpty(response.OdataNextLink))
            {
                response = await _graphClient.Identity.ConditionalAccess.AuthenticationContextClassReferences
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

    public async Task<AuthenticationContextClassReference?> GetAuthenticationContextAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.Identity.ConditionalAccess.AuthenticationContextClassReferences[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<AuthenticationContextClassReference> CreateAuthenticationContextAsync(AuthenticationContextClassReference contextClassReference, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.Identity.ConditionalAccess.AuthenticationContextClassReferences
            .PostAsync(contextClassReference, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create authentication context");
    }

    public async Task<AuthenticationContextClassReference> UpdateAuthenticationContextAsync(AuthenticationContextClassReference contextClassReference, CancellationToken cancellationToken = default)
    {
        var id = contextClassReference.Id ?? throw new ArgumentException("Authentication context must have an ID for update");

        var result = await _graphClient.Identity.ConditionalAccess.AuthenticationContextClassReferences[id]
            .PatchAsync(contextClassReference, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetAuthenticationContextAsync(id, cancellationToken), "authentication context");
    }

    public async Task DeleteAuthenticationContextAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.Identity.ConditionalAccess.AuthenticationContextClassReferences[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }
}
