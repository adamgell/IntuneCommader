using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IAdmxFileService
{
    Task<List<GroupPolicyUploadedDefinitionFile>> ListAdmxFilesAsync(CancellationToken cancellationToken = default);
    Task<GroupPolicyUploadedDefinitionFile?> GetAdmxFileAsync(string id, CancellationToken cancellationToken = default);
    Task<GroupPolicyUploadedDefinitionFile> CreateAdmxFileAsync(GroupPolicyUploadedDefinitionFile admxFile, CancellationToken cancellationToken = default);
    Task DeleteAdmxFileAsync(string id, CancellationToken cancellationToken = default);
}
