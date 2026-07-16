using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface ITermsOfUseService
{
    Task<List<Agreement>> ListTermsOfUseAgreementsAsync(CancellationToken cancellationToken = default);
    Task<Agreement?> GetTermsOfUseAgreementAsync(string id, CancellationToken cancellationToken = default);
    Task<Agreement> CreateTermsOfUseAgreementAsync(Agreement agreement, CancellationToken cancellationToken = default);
    Task<Agreement> UpdateTermsOfUseAgreementAsync(Agreement agreement, CancellationToken cancellationToken = default);
    Task DeleteTermsOfUseAgreementAsync(string id, CancellationToken cancellationToken = default);
}
