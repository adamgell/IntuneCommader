using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface ITermsAndConditionsService
{
    Task<List<TermsAndConditions>> ListTermsAndConditionsAsync(CancellationToken cancellationToken = default);
    Task<TermsAndConditions?> GetTermsAndConditionsAsync(string id, CancellationToken cancellationToken = default);
    Task<TermsAndConditions> CreateTermsAndConditionsAsync(TermsAndConditions termsAndConditions, CancellationToken cancellationToken = default);
    Task<TermsAndConditions> UpdateTermsAndConditionsAsync(TermsAndConditions termsAndConditions, CancellationToken cancellationToken = default);
    Task DeleteTermsAndConditionsAsync(string id, CancellationToken cancellationToken = default);
}
