using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IAuthenticationContextService
{
    Task<List<AuthenticationContextClassReference>> ListAuthenticationContextsAsync(CancellationToken cancellationToken = default);
    Task<AuthenticationContextClassReference?> GetAuthenticationContextAsync(string id, CancellationToken cancellationToken = default);
    Task<AuthenticationContextClassReference> CreateAuthenticationContextAsync(AuthenticationContextClassReference contextClassReference, CancellationToken cancellationToken = default);
    Task<AuthenticationContextClassReference> UpdateAuthenticationContextAsync(AuthenticationContextClassReference contextClassReference, CancellationToken cancellationToken = default);
    Task DeleteAuthenticationContextAsync(string id, CancellationToken cancellationToken = default);
}
