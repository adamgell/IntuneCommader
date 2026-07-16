using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class EnrollmentConfigurationService : IEnrollmentConfigurationService
{
    private readonly GraphServiceClient _graphClient;

    public EnrollmentConfigurationService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<DeviceEnrollmentConfiguration>> ListEnrollmentConfigurationsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<DeviceEnrollmentConfiguration>();

        var response = await _graphClient.DeviceManagement.DeviceEnrollmentConfigurations
            .GetAsync(req =>
            {
                req.QueryParameters.Top = 200;
            }, cancellationToken);

        while (response != null)
        {
            if (response.Value != null)
                result.AddRange(response.Value);

            if (!string.IsNullOrEmpty(response.OdataNextLink))
            {
                response = await _graphClient.DeviceManagement.DeviceEnrollmentConfigurations
                    .WithUrl(response.OdataNextLink)
                    .GetAsync(cancellationToken: cancellationToken);
            }
            else
            {
                break;
            }
        }

        return result;
    }

    public async Task<List<DeviceEnrollmentConfiguration>> ListEnrollmentStatusPagesAsync(CancellationToken cancellationToken = default)
    {
        var all = await ListEnrollmentConfigurationsAsync(cancellationToken);
        return all.Where(IsEnrollmentStatusPage).ToList();
    }

    public async Task<List<DeviceEnrollmentConfiguration>> ListEnrollmentRestrictionsAsync(CancellationToken cancellationToken = default)
    {
        var all = await ListEnrollmentConfigurationsAsync(cancellationToken);
        return all.Where(IsEnrollmentRestriction).ToList();
    }

    public async Task<List<DeviceEnrollmentConfiguration>> ListCoManagementSettingsAsync(CancellationToken cancellationToken = default)
    {
        var all = await ListEnrollmentConfigurationsAsync(cancellationToken);
        return all.Where(IsCoManagementSettings).ToList();
    }

    public async Task<DeviceEnrollmentConfiguration?> GetEnrollmentConfigurationAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceManagement.DeviceEnrollmentConfigurations[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<DeviceEnrollmentConfiguration> CreateEnrollmentConfigurationAsync(DeviceEnrollmentConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.DeviceManagement.DeviceEnrollmentConfigurations
            .PostAsync(configuration, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create enrollment configuration");
    }

    public async Task<DeviceEnrollmentConfiguration> UpdateEnrollmentConfigurationAsync(DeviceEnrollmentConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var id = configuration.Id ?? throw new ArgumentException("Enrollment configuration must have an ID for update");

        var result = await _graphClient.DeviceManagement.DeviceEnrollmentConfigurations[id]
            .PatchAsync(configuration, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetEnrollmentConfigurationAsync(id, cancellationToken), "enrollment configuration");
    }

    public async Task DeleteEnrollmentConfigurationAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.DeviceEnrollmentConfigurations[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }

    private static bool IsEnrollmentStatusPage(DeviceEnrollmentConfiguration configuration)
    {
        return configuration.OdataType?.Contains("windows10EnrollmentCompletionPageConfiguration", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsCoManagementSettings(DeviceEnrollmentConfiguration configuration)
    {
        return configuration.OdataType?.Contains("singlePlatformSccmEnrollment", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsEnrollmentRestriction(DeviceEnrollmentConfiguration configuration)
    {
        return !IsEnrollmentStatusPage(configuration) && !IsCoManagementSettings(configuration);
    }
}
