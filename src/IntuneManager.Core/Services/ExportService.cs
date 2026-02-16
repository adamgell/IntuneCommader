using System.Text.Json;
using IntuneManager.Core.Models;
using Microsoft.Graph.Models;

namespace IntuneManager.Core.Services;

public class ExportService : IExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task ExportDeviceConfigurationAsync(
        DeviceConfiguration config,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "DeviceConfigurations");
        Directory.CreateDirectory(folderPath);

        var sanitizedName = SanitizeFileName(config.DisplayName ?? config.Id ?? "unknown");
        var filePath = Path.Combine(folderPath, $"{sanitizedName}.json");

        // Serialize using System.Text.Json with the Graph model
        var json = JsonSerializer.Serialize(config, config.GetType(), JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        // Add to migration table
        if (config.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "DeviceConfiguration",
                OriginalId = config.Id,
                Name = config.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportDeviceConfigurationsAsync(
        IEnumerable<DeviceConfiguration> configs,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var config in configs)
        {
            await ExportDeviceConfigurationAsync(config, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task SaveMigrationTableAsync(MigrationTable table, string outputPath, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(outputPath, "migration-table.json");
        var json = JsonSerializer.Serialize(table, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    public async Task ExportCompliancePolicyAsync(
        DeviceCompliancePolicy policy,
        IReadOnlyList<DeviceCompliancePolicyAssignment> assignments,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "CompliancePolicies");
        Directory.CreateDirectory(folderPath);

        var sanitizedName = SanitizeFileName(policy.DisplayName ?? policy.Id ?? "unknown");
        var filePath = Path.Combine(folderPath, $"{sanitizedName}.json");

        var export = new CompliancePolicyExport
        {
            Policy = policy,
            Assignments = assignments.ToList()
        };

        var json = JsonSerializer.Serialize(export, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        if (policy.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "CompliancePolicy",
                OriginalId = policy.Id,
                Name = policy.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportCompliancePoliciesAsync(
        IEnumerable<(DeviceCompliancePolicy Policy, IReadOnlyList<DeviceCompliancePolicyAssignment> Assignments)> policies,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var (policy, assignments) in policies)
        {
            await ExportCompliancePolicyAsync(policy, assignments, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportApplicationAsync(
        MobileApp app,
        IReadOnlyList<MobileAppAssignment> assignments,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "Applications");
        Directory.CreateDirectory(folderPath);

        var sanitizedName = SanitizeFileName(app.DisplayName ?? app.Id ?? "unknown");
        var filePath = Path.Combine(folderPath, $"{sanitizedName}.json");

        var export = new ApplicationExport
        {
            Application = app,
            Assignments = assignments.ToList()
        };

        var json = JsonSerializer.Serialize(export, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        if (app.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "Application",
                OriginalId = app.Id,
                Name = app.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportApplicationsAsync(
        IEnumerable<(MobileApp App, IReadOnlyList<MobileAppAssignment> Assignments)> apps,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var (app, assignments) in apps)
        {
            await ExportApplicationAsync(app, assignments, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }
}
