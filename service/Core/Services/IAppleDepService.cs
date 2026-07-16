using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IAppleDepService
{
    Task<List<DepOnboardingSetting>> ListDepOnboardingSettingsAsync(CancellationToken cancellationToken = default);
    Task<DepOnboardingSetting?> GetDepOnboardingSettingAsync(string id, CancellationToken cancellationToken = default);
    Task<List<ImportedAppleDeviceIdentity>> ListImportedAppleDeviceIdentitiesAsync(string depOnboardingSettingId, CancellationToken cancellationToken = default);
}
