using IntuneManager.Core.Models;
using IntuneManager.Core.Services;
using Microsoft.Graph.Models;

namespace IntuneManager.Core.Tests.Services;

public class ExportServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ExportService _service;

    public ExportServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"intunemanager-export-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _service = new ExportService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task ExportDeviceConfiguration_CreatesJsonFile()
    {
        var config = new DeviceConfiguration
        {
            Id = "test-id",
            DisplayName = "Test Config"
        };
        var table = new MigrationTable();

        await _service.ExportDeviceConfigurationAsync(config, _tempDir, table);

        var expectedPath = Path.Combine(_tempDir, "DeviceConfigurations", "Test Config.json");
        Assert.True(File.Exists(expectedPath));
    }

    [Fact]
    public async Task ExportDeviceConfiguration_UpdatesMigrationTable()
    {
        var config = new DeviceConfiguration
        {
            Id = "test-id",
            DisplayName = "Test Config"
        };
        var table = new MigrationTable();

        await _service.ExportDeviceConfigurationAsync(config, _tempDir, table);

        Assert.Single(table.Entries);
        Assert.Equal("test-id", table.Entries[0].OriginalId);
        Assert.Equal("DeviceConfiguration", table.Entries[0].ObjectType);
    }

    [Fact]
    public async Task ExportDeviceConfigurations_ExportsMultipleFiles()
    {
        var configs = new[]
        {
            new DeviceConfiguration { Id = "id-1", DisplayName = "Config One" },
            new DeviceConfiguration { Id = "id-2", DisplayName = "Config Two" }
        };

        await _service.ExportDeviceConfigurationsAsync(configs, _tempDir);

        var folder = Path.Combine(_tempDir, "DeviceConfigurations");
        Assert.Equal(2, Directory.GetFiles(folder, "*.json").Length);
    }

    [Fact]
    public async Task ExportDeviceConfigurations_CreatesMigrationTableFile()
    {
        var configs = new[]
        {
            new DeviceConfiguration { Id = "id-1", DisplayName = "Config One" }
        };

        await _service.ExportDeviceConfigurationsAsync(configs, _tempDir);

        var tablePath = Path.Combine(_tempDir, "migration-table.json");
        Assert.True(File.Exists(tablePath));
    }

    [Fact]
    public async Task SaveMigrationTable_WritesValidJson()
    {
        var table = new MigrationTable();
        table.AddOrUpdate(new MigrationEntry
        {
            ObjectType = "DeviceConfiguration",
            OriginalId = "id-1",
            Name = "Policy"
        });

        await _service.SaveMigrationTableAsync(table, _tempDir);

        var json = await File.ReadAllTextAsync(Path.Combine(_tempDir, "migration-table.json"));
        Assert.Contains("DeviceConfiguration", json);
        Assert.Contains("id-1", json);
    }

    [Fact]
    public async Task ExportDeviceConfiguration_SanitizesFileName()
    {
        var config = new DeviceConfiguration
        {
            Id = "test-id",
            DisplayName = "Test/Config:With<Invalid>Chars"
        };
        var table = new MigrationTable();

        await _service.ExportDeviceConfigurationAsync(config, _tempDir, table);

        var folder = Path.Combine(_tempDir, "DeviceConfigurations");
        var files = Directory.GetFiles(folder, "*.json");
        Assert.Single(files);
    }

    [Fact]
    public async Task ExportCompliancePolicy_CreatesJsonFile()
    {
        var policy = new DeviceCompliancePolicy
        {
            Id = "cp-id",
            DisplayName = "Test Compliance"
        };
        var assignments = new List<DeviceCompliancePolicyAssignment>();
        var table = new MigrationTable();

        await _service.ExportCompliancePolicyAsync(policy, assignments, _tempDir, table);

        var expectedPath = Path.Combine(_tempDir, "CompliancePolicies", "Test Compliance.json");
        Assert.True(File.Exists(expectedPath));
    }

    [Fact]
    public async Task ExportCompliancePolicy_UpdatesMigrationTable()
    {
        var policy = new DeviceCompliancePolicy
        {
            Id = "cp-id",
            DisplayName = "Test Compliance"
        };
        var assignments = new List<DeviceCompliancePolicyAssignment>();
        var table = new MigrationTable();

        await _service.ExportCompliancePolicyAsync(policy, assignments, _tempDir, table);

        Assert.Single(table.Entries);
        Assert.Equal("cp-id", table.Entries[0].OriginalId);
        Assert.Equal("CompliancePolicy", table.Entries[0].ObjectType);
    }

    [Fact]
    public async Task ExportCompliancePolicy_IncludesAssignmentsInJson()
    {
        var policy = new DeviceCompliancePolicy
        {
            Id = "cp-id",
            DisplayName = "With Assignments"
        };
        var assignments = new List<DeviceCompliancePolicyAssignment>
        {
            new() { Id = "a1", Target = new DeviceAndAppManagementAssignmentTarget() }
        };
        var table = new MigrationTable();

        await _service.ExportCompliancePolicyAsync(policy, assignments, _tempDir, table);

        var filePath = Path.Combine(_tempDir, "CompliancePolicies", "With Assignments.json");
        var json = await File.ReadAllTextAsync(filePath);
        Assert.Contains("assignments", json);
    }

    [Fact]
    public async Task ExportCompliancePolicies_ExportsMultiple()
    {
        var policies = new (DeviceCompliancePolicy, IReadOnlyList<DeviceCompliancePolicyAssignment>)[]
        {
            (new DeviceCompliancePolicy { Id = "cp-1", DisplayName = "Policy One" }, Array.Empty<DeviceCompliancePolicyAssignment>()),
            (new DeviceCompliancePolicy { Id = "cp-2", DisplayName = "Policy Two" }, Array.Empty<DeviceCompliancePolicyAssignment>())
        };

        await _service.ExportCompliancePoliciesAsync(policies, _tempDir);

        var folder = Path.Combine(_tempDir, "CompliancePolicies");
        Assert.Equal(2, Directory.GetFiles(folder, "*.json").Length);
    }

    // --- Application export tests ---

    [Fact]
    public async Task ExportApplication_CreatesJsonFile()
    {
        var app = new MobileApp
        {
            Id = "app-id",
            DisplayName = "Test App"
        };
        var assignments = new List<MobileAppAssignment>();
        var table = new MigrationTable();

        await _service.ExportApplicationAsync(app, assignments, _tempDir, table);

        var expectedPath = Path.Combine(_tempDir, "Applications", "Test App.json");
        Assert.True(File.Exists(expectedPath));
    }

    [Fact]
    public async Task ExportApplication_UpdatesMigrationTable()
    {
        var app = new MobileApp
        {
            Id = "app-id",
            DisplayName = "Test App"
        };
        var assignments = new List<MobileAppAssignment>();
        var table = new MigrationTable();

        await _service.ExportApplicationAsync(app, assignments, _tempDir, table);

        Assert.Single(table.Entries);
        Assert.Equal("app-id", table.Entries[0].OriginalId);
        Assert.Equal("Application", table.Entries[0].ObjectType);
    }

    [Fact]
    public async Task ExportApplication_IncludesAssignmentsInJson()
    {
        var app = new MobileApp
        {
            Id = "app-id",
            DisplayName = "With Assignments"
        };
        var assignments = new List<MobileAppAssignment>
        {
            new() { Id = "a1", Intent = InstallIntent.Required }
        };
        var table = new MigrationTable();

        await _service.ExportApplicationAsync(app, assignments, _tempDir, table);

        var filePath = Path.Combine(_tempDir, "Applications", "With Assignments.json");
        var json = await File.ReadAllTextAsync(filePath);
        Assert.Contains("assignments", json);
    }

    [Fact]
    public async Task ExportApplications_ExportsMultiple()
    {
        var apps = new (MobileApp, IReadOnlyList<MobileAppAssignment>)[]
        {
            (new MobileApp { Id = "app-1", DisplayName = "App One" }, Array.Empty<MobileAppAssignment>()),
            (new MobileApp { Id = "app-2", DisplayName = "App Two" }, Array.Empty<MobileAppAssignment>())
        };

        await _service.ExportApplicationsAsync(apps, _tempDir);

        var folder = Path.Combine(_tempDir, "Applications");
        Assert.Equal(2, Directory.GetFiles(folder, "*.json").Length);
    }
}
