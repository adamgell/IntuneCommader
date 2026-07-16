using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IEntraUserService
{
    Task<List<User>> ListUsersAsync(CancellationToken cancellationToken = default);
}
