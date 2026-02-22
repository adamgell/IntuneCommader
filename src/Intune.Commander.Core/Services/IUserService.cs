using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IUserService
{
    /// <summary>
    /// Searches for users by display name (startsWith) or exact UPN.
    /// </summary>
    Task<List<User>> SearchUsersAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all users with full pagination ($top=999 + @odata.nextLink).
    /// </summary>
    Task<List<User>> ListUsersAsync(CancellationToken cancellationToken = default);
}
