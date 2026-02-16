using IntuneManager.Core.Models;
using Microsoft.Graph.Models;

namespace IntuneManager.Core.Services;

public interface IExportService
{
    Task ExportDeviceConfigurationAsync(DeviceConfiguration config, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportDeviceConfigurationsAsync(IEnumerable<DeviceConfiguration> configs, string outputPath, CancellationToken cancellationToken = default);

    Task ExportCompliancePolicyAsync(DeviceCompliancePolicy policy, IReadOnlyList<DeviceCompliancePolicyAssignment> assignments, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportCompliancePoliciesAsync(IEnumerable<(DeviceCompliancePolicy Policy, IReadOnlyList<DeviceCompliancePolicyAssignment> Assignments)> policies, string outputPath, CancellationToken cancellationToken = default);

    Task ExportApplicationAsync(MobileApp app, IReadOnlyList<MobileAppAssignment> assignments, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportApplicationsAsync(IEnumerable<(MobileApp App, IReadOnlyList<MobileAppAssignment> Assignments)> apps, string outputPath, CancellationToken cancellationToken = default);

    Task SaveMigrationTableAsync(MigrationTable table, string outputPath, CancellationToken cancellationToken = default);
}
