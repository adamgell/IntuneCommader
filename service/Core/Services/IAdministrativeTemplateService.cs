using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IAdministrativeTemplateService
{
    Task<List<GroupPolicyConfiguration>> ListAdministrativeTemplatesAsync(CancellationToken cancellationToken = default);
    Task<GroupPolicyConfiguration?> GetAdministrativeTemplateAsync(string id, CancellationToken cancellationToken = default);
    Task<GroupPolicyConfiguration> CreateAdministrativeTemplateAsync(GroupPolicyConfiguration template, CancellationToken cancellationToken = default);
    Task<GroupPolicyConfiguration> UpdateAdministrativeTemplateAsync(GroupPolicyConfiguration template, CancellationToken cancellationToken = default);
    Task DeleteAdministrativeTemplateAsync(string id, CancellationToken cancellationToken = default);
    Task<List<GroupPolicyConfigurationAssignment>> GetAssignmentsAsync(string templateId, CancellationToken cancellationToken = default);
    Task AssignAdministrativeTemplateAsync(string templateId, List<GroupPolicyConfigurationAssignment> assignments, CancellationToken cancellationToken = default);
}
