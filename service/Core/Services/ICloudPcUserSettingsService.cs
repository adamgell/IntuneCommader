using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface ICloudPcUserSettingsService
{
    Task<List<CloudPcUserSetting>> ListUserSettingsAsync(CancellationToken cancellationToken = default);
    Task<CloudPcUserSetting?> GetUserSettingAsync(string id, CancellationToken cancellationToken = default);
}
