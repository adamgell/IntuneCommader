using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IVppTokenService
{
    Task<List<VppToken>> ListVppTokensAsync(CancellationToken cancellationToken = default);
    Task<VppToken?> GetVppTokenAsync(string id, CancellationToken cancellationToken = default);
}
