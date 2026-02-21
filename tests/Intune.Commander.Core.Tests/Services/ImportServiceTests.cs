using System.Text.Json;
using Intune.Commander.Core.Models;
using Intune.Commander.Core.Services;
using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Tests.Services;

public class ImportServiceTests : IDisposable
{
    private readonly string _tempDir;

    public ImportServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"intunemanager-import-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task ReadDeviceConfigurationAsync_ReadsValidJson()
    {
        var config = new DeviceConfiguration { Id = "cfg-1", DisplayName = "Config One" };
        var file = Path.Combine(_tempDir, "cfg.json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(config));

        var sut = new ImportService(new StubConfigurationService());
        var read = await sut.ReadDeviceConfigurationAsync(file);

        Assert.NotNull(read);
        Assert.Equal("cfg-1", read.Id);
        Assert.Equal("Config One", read.DisplayName);
    }

    [Fact]
    public async Task ReadDeviceConfigurationsFromFolderAsync_MissingFolder_ReturnsEmpty()
    {
        var sut = new ImportService(new StubConfigurationService());

        var result = await sut.ReadDeviceConfigurationsFromFolderAsync(_tempDir);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadDeviceConfigurationsFromFolderAsync_ReadsAllJsonFiles()
    {
        var folder = Path.Combine(_tempDir, "DeviceConfigurations");
        Directory.CreateDirectory(folder);

        await File.WriteAllTextAsync(Path.Combine(folder, "a.json"), JsonSerializer.Serialize(
            new DeviceConfiguration { Id = "a", DisplayName = "A" }));
        await File.WriteAllTextAsync(Path.Combine(folder, "b.json"), JsonSerializer.Serialize(
            new DeviceConfiguration { Id = "b", DisplayName = "B" }));

        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadDeviceConfigurationsFromFolderAsync(_tempDir);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.Id == "a");
        Assert.Contains(result, c => c.Id == "b");
    }

    [Fact]
    public async Task ReadMigrationTableAsync_MissingFile_ReturnsEmpty()
    {
        var sut = new ImportService(new StubConfigurationService());

        var table = await sut.ReadMigrationTableAsync(_tempDir);

        Assert.Empty(table.Entries);
    }

    [Fact]
    public async Task ReadMigrationTableAsync_ReadsExistingFile()
    {
        var table = new MigrationTable();
        table.AddOrUpdate(new MigrationEntry
        {
            ObjectType = "DeviceConfiguration",
            OriginalId = "old-1",
            NewId = "new-1",
            Name = "One"
        });

        var path = Path.Combine(_tempDir, "migration-table.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(table));

        var sut = new ImportService(new StubConfigurationService());
        var read = await sut.ReadMigrationTableAsync(_tempDir);

        Assert.Single(read.Entries);
        Assert.Equal("old-1", read.Entries[0].OriginalId);
    }

    [Fact]
    public async Task ImportDeviceConfigurationAsync_ClearsReadOnlyFields_AndUpdatesMigration()
    {
        var cfgService = new StubConfigurationService
        {
            CreateResult = new DeviceConfiguration { Id = "new-cfg", DisplayName = "Created" }
        };
        var sut = new ImportService(cfgService);
        var table = new MigrationTable();

        var source = new DeviceConfiguration
        {
            Id = "old-cfg",
            DisplayName = "Source",
            CreatedDateTime = DateTimeOffset.UtcNow,
            LastModifiedDateTime = DateTimeOffset.UtcNow,
            Version = 99
        };

        var created = await sut.ImportDeviceConfigurationAsync(source, table);

        Assert.Equal("new-cfg", created.Id);
        Assert.NotNull(cfgService.LastCreatedConfig);
        Assert.Null(cfgService.LastCreatedConfig!.Id);
        Assert.Null(cfgService.LastCreatedConfig.CreatedDateTime);
        Assert.Null(cfgService.LastCreatedConfig.LastModifiedDateTime);
        Assert.Null(cfgService.LastCreatedConfig.Version);
        Assert.Single(table.Entries);
        Assert.Equal("old-cfg", table.Entries[0].OriginalId);
        Assert.Equal("new-cfg", table.Entries[0].NewId);
    }

    [Fact]
    public async Task ReadCompliancePolicyAsync_ReadsValidJson()
    {
        var export = new CompliancePolicyExport
        {
            Policy = new DeviceCompliancePolicy { Id = "p1", DisplayName = "Policy One" },
            Assignments = []
        };

        var file = Path.Combine(_tempDir, "policy.json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(export));

        var sut = new ImportService(new StubConfigurationService(), new StubComplianceService());
        var read = await sut.ReadCompliancePolicyAsync(file);

        Assert.NotNull(read);
        Assert.Equal("p1", read.Policy.Id);
    }

    [Fact]
    public async Task ReadCompliancePoliciesFromFolderAsync_MissingFolder_ReturnsEmpty()
    {
        var sut = new ImportService(new StubConfigurationService(), new StubComplianceService());

        var result = await sut.ReadCompliancePoliciesFromFolderAsync(_tempDir);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadCompliancePoliciesFromFolderAsync_ReadsAllJsonFiles()
    {
        var folder = Path.Combine(_tempDir, "CompliancePolicies");
        Directory.CreateDirectory(folder);

        var p1 = new CompliancePolicyExport { Policy = new DeviceCompliancePolicy { Id = "p1", DisplayName = "P1" } };
        var p2 = new CompliancePolicyExport { Policy = new DeviceCompliancePolicy { Id = "p2", DisplayName = "P2" } };

        await File.WriteAllTextAsync(Path.Combine(folder, "p1.json"), JsonSerializer.Serialize(p1));
        await File.WriteAllTextAsync(Path.Combine(folder, "p2.json"), JsonSerializer.Serialize(p2));

        var sut = new ImportService(new StubConfigurationService(), new StubComplianceService());
        var result = await sut.ReadCompliancePoliciesFromFolderAsync(_tempDir);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ImportCompliancePolicyAsync_WithoutService_Throws()
    {
        var sut = new ImportService(new StubConfigurationService(), null);
        var table = new MigrationTable();
        var export = new CompliancePolicyExport
        {
            Policy = new DeviceCompliancePolicy { Id = "old", DisplayName = "Old" }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ImportCompliancePolicyAsync(export, table));
    }

    [Fact]
    public async Task ImportEndpointSecurityIntentAsync_WithoutService_Throws()
    {
        var sut = new ImportService(new StubConfigurationService(), null);
        var table = new MigrationTable();
        var export = new EndpointSecurityExport { Intent = new DeviceManagementIntent { Id = "old", DisplayName = "Old" } };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ImportEndpointSecurityIntentAsync(export, table));
    }

    [Fact]
    public async Task ImportAdministrativeTemplateAsync_WithoutService_Throws()
    {
        var sut = new ImportService(new StubConfigurationService(), null);
        var table = new MigrationTable();
        var export = new AdministrativeTemplateExport { Template = new GroupPolicyConfiguration { Id = "old", DisplayName = "Old" } };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ImportAdministrativeTemplateAsync(export, table));
    }

    [Fact]
    public async Task ImportEnrollmentConfigurationAsync_WithoutService_Throws()
    {
        var sut = new ImportService(new StubConfigurationService(), null);
        var table = new MigrationTable();
        var config = new DeviceEnrollmentConfiguration { Id = "old", DisplayName = "Old" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ImportEnrollmentConfigurationAsync(config, table));
    }

    [Fact]
    public async Task ImportAppProtectionPolicyAsync_WithoutService_Throws()
    {
        var sut = new ImportService(new StubConfigurationService(), null);
        var table = new MigrationTable();
        var policy = new AndroidManagedAppProtection { Id = "old", DisplayName = "Old" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ImportAppProtectionPolicyAsync(policy, table));
    }

    [Fact]
    public async Task ImportManagedDeviceAppConfigurationAsync_WithoutService_Throws()
    {
        var sut = new ImportService(new StubConfigurationService(), null);
        var table = new MigrationTable();
        var config = new ManagedDeviceMobileAppConfiguration { Id = "old", DisplayName = "Old" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ImportManagedDeviceAppConfigurationAsync(config, table));
    }

    [Fact]
    public async Task ImportTargetedManagedAppConfigurationAsync_WithoutService_Throws()
    {
        var sut = new ImportService(new StubConfigurationService(), null);
        var table = new MigrationTable();
        var config = new TargetedManagedAppConfiguration { Id = "old", DisplayName = "Old" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ImportTargetedManagedAppConfigurationAsync(config, table));
    }

    [Fact]
    public async Task ImportTermsAndConditionsAsync_WithoutService_Throws()
    {
        var sut = new ImportService(new StubConfigurationService(), null);
        var table = new MigrationTable();
        var terms = new TermsAndConditions { Id = "old", DisplayName = "Old" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ImportTermsAndConditionsAsync(terms, table));
    }

    [Fact]
    public async Task ImportScopeTagAsync_WithoutService_Throws()
    {
        var sut = new ImportService(new StubConfigurationService(), null);
        var table = new MigrationTable();
        var scopeTag = new RoleScopeTag { Id = "old", DisplayName = "Old" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ImportScopeTagAsync(scopeTag, table));
    }

    [Fact]
    public async Task ImportRoleDefinitionAsync_WithoutService_Throws()
    {
        var sut = new ImportService(new StubConfigurationService(), null);
        var table = new MigrationTable();
        var roleDefinition = new RoleDefinition { Id = "old", DisplayName = "Old" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ImportRoleDefinitionAsync(roleDefinition, table));
    }

    [Fact]
    public async Task ImportIntuneBrandingProfileAsync_WithoutService_Throws()
    {
        var sut = new ImportService(new StubConfigurationService(), null);
        var table = new MigrationTable();
        var profile = new IntuneBrandingProfile { Id = "old", ProfileName = "Old" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ImportIntuneBrandingProfileAsync(profile, table));
    }

    [Fact]
    public async Task ImportAzureBrandingLocalizationAsync_WithoutService_Throws()
    {
        var sut = new ImportService(new StubConfigurationService(), null);
        var table = new MigrationTable();
        var localization = new OrganizationalBrandingLocalization { Id = "old" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ImportAzureBrandingLocalizationAsync(localization, table));
    }

    [Fact]
    public async Task ImportAutopilotProfileAsync_WithoutService_Throws()
    {
        var sut = new ImportService(new StubConfigurationService(), null);
        var table = new MigrationTable();
        var profile = new WindowsAutopilotDeploymentProfile { Id = "old", DisplayName = "Old" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ImportAutopilotProfileAsync(profile, table));
    }

    [Fact]
    public async Task ImportDeviceHealthScriptAsync_WithoutService_Throws()
    {
        var sut = new ImportService(new StubConfigurationService(), null);
        var table = new MigrationTable();
        var script = new DeviceHealthScript { Id = "old", DisplayName = "Old" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ImportDeviceHealthScriptAsync(script, table));
    }

    [Fact]
    public async Task ImportMacCustomAttributeAsync_WithoutService_Throws()
    {
        var sut = new ImportService(new StubConfigurationService(), null);
        var table = new MigrationTable();
        var script = new DeviceCustomAttributeShellScript { Id = "old", DisplayName = "Old" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ImportMacCustomAttributeAsync(script, table));
    }

    [Fact]
    public async Task ImportFeatureUpdateProfileAsync_WithoutService_Throws()
    {
        var sut = new ImportService(new StubConfigurationService(), null);
        var table = new MigrationTable();
        var profile = new WindowsFeatureUpdateProfile { Id = "old", DisplayName = "Old" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ImportFeatureUpdateProfileAsync(profile, table));
    }

    [Fact]
    public async Task ImportNamedLocationAsync_WithoutService_Throws()
    {
        var sut = new ImportService(new StubConfigurationService(), null);
        var table = new MigrationTable();
        var namedLocation = new NamedLocation { Id = "old" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ImportNamedLocationAsync(namedLocation, table));
    }

    [Fact]
    public async Task ImportAuthenticationStrengthPolicyAsync_WithoutService_Throws()
    {
        var sut = new ImportService(new StubConfigurationService(), null);
        var table = new MigrationTable();
        var policy = new AuthenticationStrengthPolicy { Id = "old", DisplayName = "Old" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ImportAuthenticationStrengthPolicyAsync(policy, table));
    }

    [Fact]
    public async Task ImportAuthenticationContextAsync_WithoutService_Throws()
    {
        var sut = new ImportService(new StubConfigurationService(), null);
        var table = new MigrationTable();
        var context = new AuthenticationContextClassReference { Id = "old", DisplayName = "Old" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ImportAuthenticationContextAsync(context, table));
    }

    [Fact]
    public async Task ImportTermsOfUseAgreementAsync_WithoutService_Throws()
    {
        var sut = new ImportService(new StubConfigurationService(), null);
        var table = new MigrationTable();
        var agreement = new Agreement { Id = "old", DisplayName = "Old" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ImportTermsOfUseAgreementAsync(agreement, table));
    }

    [Fact]
    public async Task ImportCompliancePolicyAsync_AssignsAndUpdatesMigration()
    {
        var complianceService = new StubComplianceService
        {
            CreateResult = new DeviceCompliancePolicy { Id = "new-pol", DisplayName = "Created Policy" }
        };

        var sut = new ImportService(new StubConfigurationService(), complianceService);
        var table = new MigrationTable();

        var export = new CompliancePolicyExport
        {
            Policy = new DeviceCompliancePolicy
            {
                Id = "old-pol",
                DisplayName = "Source Policy",
                CreatedDateTime = DateTimeOffset.UtcNow,
                LastModifiedDateTime = DateTimeOffset.UtcNow,
                Version = 3
            },
            Assignments =
            [
                new DeviceCompliancePolicyAssignment { Id = "assign-1" },
                new DeviceCompliancePolicyAssignment { Id = "assign-2" }
            ]
        };

        var created = await sut.ImportCompliancePolicyAsync(export, table);

        Assert.Equal("new-pol", created.Id);
        Assert.NotNull(complianceService.LastCreatedPolicy);
        Assert.Null(complianceService.LastCreatedPolicy!.Id);
        Assert.Null(complianceService.LastCreatedPolicy.CreatedDateTime);
        Assert.Null(complianceService.LastCreatedPolicy.LastModifiedDateTime);
        Assert.Null(complianceService.LastCreatedPolicy.Version);

        Assert.True(complianceService.AssignCalled);
        Assert.Equal("new-pol", complianceService.AssignedPolicyId);
        Assert.All(complianceService.AssignedAssignments!, a => Assert.Null(a.Id));

        Assert.Single(table.Entries);
        Assert.Equal("old-pol", table.Entries[0].OriginalId);
        Assert.Equal("new-pol", table.Entries[0].NewId);
    }

    [Fact]
    public async Task ImportEndpointSecurityIntentAsync_AssignsAndUpdatesMigration()
    {
        var endpointService = new StubEndpointSecurityService
        {
            CreateResult = new DeviceManagementIntent { Id = "new-intent", DisplayName = "Created Intent" }
        };
        var sut = new ImportService(new StubConfigurationService(), null, endpointSecurityService: endpointService);
        var table = new MigrationTable();

        var export = new EndpointSecurityExport
        {
            Intent = new DeviceManagementIntent { Id = "old-intent", DisplayName = "Source Intent" },
            Assignments = [new DeviceManagementIntentAssignment { Id = "ea-1" }]
        };

        var created = await sut.ImportEndpointSecurityIntentAsync(export, table);

        Assert.Equal("new-intent", created.Id);
        Assert.True(endpointService.AssignCalled);
        Assert.Equal("new-intent", endpointService.AssignedIntentId);
        Assert.All(endpointService.AssignedAssignments!, a => Assert.Null(a.Id));
        Assert.Single(table.Entries);
        Assert.Equal("EndpointSecurityIntent", table.Entries[0].ObjectType);
        Assert.Equal("old-intent", table.Entries[0].OriginalId);
        Assert.Equal("new-intent", table.Entries[0].NewId);
    }

    [Fact]
    public async Task ImportAdministrativeTemplateAsync_AssignsAndUpdatesMigration()
    {
        var templateService = new StubAdministrativeTemplateService
        {
            CreateResult = new GroupPolicyConfiguration { Id = "new-template", DisplayName = "Created Template" }
        };
        var sut = new ImportService(new StubConfigurationService(), null, administrativeTemplateService: templateService);
        var table = new MigrationTable();

        var export = new AdministrativeTemplateExport
        {
            Template = new GroupPolicyConfiguration
            {
                Id = "old-template",
                DisplayName = "Source Template",
                CreatedDateTime = DateTimeOffset.UtcNow,
                LastModifiedDateTime = DateTimeOffset.UtcNow
            },
            Assignments = [new GroupPolicyConfigurationAssignment { Id = "ta-1" }]
        };

        var created = await sut.ImportAdministrativeTemplateAsync(export, table);

        Assert.Equal("new-template", created.Id);
        Assert.NotNull(templateService.LastCreatedTemplate);
        Assert.Null(templateService.LastCreatedTemplate!.Id);
        Assert.Null(templateService.LastCreatedTemplate.CreatedDateTime);
        Assert.Null(templateService.LastCreatedTemplate.LastModifiedDateTime);
        Assert.True(templateService.AssignCalled);
        Assert.Equal("new-template", templateService.AssignedTemplateId);
        Assert.All(templateService.AssignedAssignments!, a => Assert.Null(a.Id));
        Assert.Single(table.Entries);
        Assert.Equal("AdministrativeTemplate", table.Entries[0].ObjectType);
        Assert.Equal("old-template", table.Entries[0].OriginalId);
        Assert.Equal("new-template", table.Entries[0].NewId);
    }

    [Fact]
    public async Task ImportEnrollmentConfigurationAsync_UpdatesMigration()
    {
        var enrollmentService = new StubEnrollmentConfigurationService
        {
            CreateResult = new DeviceEnrollmentConfiguration { Id = "new-enroll", DisplayName = "Created Enrollment" }
        };
        var sut = new ImportService(new StubConfigurationService(), null, enrollmentConfigurationService: enrollmentService);
        var table = new MigrationTable();

        var configuration = new DeviceEnrollmentConfiguration
        {
            Id = "old-enroll",
            DisplayName = "Source Enrollment",
            CreatedDateTime = DateTimeOffset.UtcNow,
            LastModifiedDateTime = DateTimeOffset.UtcNow,
            Priority = 10
        };

        var created = await sut.ImportEnrollmentConfigurationAsync(configuration, table);

        Assert.Equal("new-enroll", created.Id);
        Assert.NotNull(enrollmentService.LastCreatedConfiguration);
        Assert.Null(enrollmentService.LastCreatedConfiguration!.Id);
        Assert.Null(enrollmentService.LastCreatedConfiguration.CreatedDateTime);
        Assert.Null(enrollmentService.LastCreatedConfiguration.LastModifiedDateTime);
        Assert.Single(table.Entries);
        Assert.Equal("EnrollmentConfiguration", table.Entries[0].ObjectType);
        Assert.Equal("old-enroll", table.Entries[0].OriginalId);
        Assert.Equal("new-enroll", table.Entries[0].NewId);
    }

    [Fact]
    public async Task ImportAppProtectionPolicyAsync_UpdatesMigration()
    {
        var appProtectionService = new StubAppProtectionPolicyService
        {
            CreateResult = new AndroidManagedAppProtection { Id = "new-app-protect", DisplayName = "Created App Protection" }
        };
        var sut = new ImportService(new StubConfigurationService(), null, appProtectionPolicyService: appProtectionService);
        var table = new MigrationTable();

        var policy = new AndroidManagedAppProtection
        {
            Id = "old-app-protect",
            DisplayName = "Source App Protection"
        };

        var created = await sut.ImportAppProtectionPolicyAsync(policy, table);

        Assert.Equal("new-app-protect", created.Id);
        Assert.NotNull(appProtectionService.LastCreatedPolicy);
        Assert.Null(appProtectionService.LastCreatedPolicy!.Id);
        Assert.Single(table.Entries);
        Assert.Equal("AppProtectionPolicy", table.Entries[0].ObjectType);
        Assert.Equal("old-app-protect", table.Entries[0].OriginalId);
        Assert.Equal("new-app-protect", table.Entries[0].NewId);
    }

    [Fact]
    public async Task ImportManagedDeviceAppConfigurationAsync_UpdatesMigration()
    {
        var managedConfigService = new StubManagedAppConfigurationService
        {
            CreateManagedDeviceResult = new ManagedDeviceMobileAppConfiguration { Id = "new-mdac", DisplayName = "Created MDAC" }
        };
        var sut = new ImportService(new StubConfigurationService(), null, managedAppConfigurationService: managedConfigService);
        var table = new MigrationTable();

        var configuration = new ManagedDeviceMobileAppConfiguration
        {
            Id = "old-mdac",
            DisplayName = "Source MDAC",
            CreatedDateTime = DateTimeOffset.UtcNow,
            LastModifiedDateTime = DateTimeOffset.UtcNow,
            Version = 5
        };

        var created = await sut.ImportManagedDeviceAppConfigurationAsync(configuration, table);

        Assert.Equal("new-mdac", created.Id);
        Assert.NotNull(managedConfigService.LastCreatedManagedDeviceConfiguration);
        Assert.Null(managedConfigService.LastCreatedManagedDeviceConfiguration!.Id);
        Assert.Null(managedConfigService.LastCreatedManagedDeviceConfiguration.CreatedDateTime);
        Assert.Null(managedConfigService.LastCreatedManagedDeviceConfiguration.LastModifiedDateTime);
        Assert.Null(managedConfigService.LastCreatedManagedDeviceConfiguration.Version);
        Assert.Single(table.Entries);
        Assert.Equal("ManagedDeviceAppConfiguration", table.Entries[0].ObjectType);
        Assert.Equal("old-mdac", table.Entries[0].OriginalId);
        Assert.Equal("new-mdac", table.Entries[0].NewId);
    }

    [Fact]
    public async Task ImportTargetedManagedAppConfigurationAsync_UpdatesMigration()
    {
        var managedConfigService = new StubManagedAppConfigurationService
        {
            CreateTargetedResult = new TargetedManagedAppConfiguration { Id = "new-tmac", DisplayName = "Created TMAC" }
        };
        var sut = new ImportService(new StubConfigurationService(), null, managedAppConfigurationService: managedConfigService);
        var table = new MigrationTable();

        var configuration = new TargetedManagedAppConfiguration
        {
            Id = "old-tmac",
            DisplayName = "Source TMAC",
            CreatedDateTime = DateTimeOffset.UtcNow,
            LastModifiedDateTime = DateTimeOffset.UtcNow,
            Version = "7"
        };

        var created = await sut.ImportTargetedManagedAppConfigurationAsync(configuration, table);

        Assert.Equal("new-tmac", created.Id);
        Assert.NotNull(managedConfigService.LastCreatedTargetedConfiguration);
        Assert.Null(managedConfigService.LastCreatedTargetedConfiguration!.Id);
        Assert.Null(managedConfigService.LastCreatedTargetedConfiguration.CreatedDateTime);
        Assert.Null(managedConfigService.LastCreatedTargetedConfiguration.LastModifiedDateTime);
        Assert.Null(managedConfigService.LastCreatedTargetedConfiguration.Version);
        Assert.Single(table.Entries);
        Assert.Equal("TargetedManagedAppConfiguration", table.Entries[0].ObjectType);
        Assert.Equal("old-tmac", table.Entries[0].OriginalId);
        Assert.Equal("new-tmac", table.Entries[0].NewId);
    }

    [Fact]
    public async Task ImportTermsAndConditionsAsync_UpdatesMigration()
    {
        var termsService = new StubTermsAndConditionsService
        {
            CreateResult = new TermsAndConditions { Id = "new-terms", DisplayName = "Created Terms" }
        };
        var sut = new ImportService(new StubConfigurationService(), null, termsAndConditionsService: termsService);
        var table = new MigrationTable();

        var termsAndConditions = new TermsAndConditions
        {
            Id = "old-terms",
            DisplayName = "Source Terms",
            CreatedDateTime = DateTimeOffset.UtcNow,
            LastModifiedDateTime = DateTimeOffset.UtcNow,
            Version = 2
        };

        var created = await sut.ImportTermsAndConditionsAsync(termsAndConditions, table);

        Assert.Equal("new-terms", created.Id);
        Assert.NotNull(termsService.LastCreatedTerms);
        Assert.Null(termsService.LastCreatedTerms!.Id);
        Assert.Null(termsService.LastCreatedTerms.CreatedDateTime);
        Assert.Null(termsService.LastCreatedTerms.LastModifiedDateTime);
        Assert.Null(termsService.LastCreatedTerms.Version);
        Assert.Single(table.Entries);
        Assert.Equal("TermsAndConditions", table.Entries[0].ObjectType);
        Assert.Equal("old-terms", table.Entries[0].OriginalId);
        Assert.Equal("new-terms", table.Entries[0].NewId);
    }

    [Fact]
    public async Task ImportScopeTagAsync_UpdatesMigration()
    {
        var scopeTagService = new StubScopeTagService
        {
            CreateResult = new RoleScopeTag { Id = "new-scope", DisplayName = "Created Scope" }
        };
        var sut = new ImportService(new StubConfigurationService(), null, scopeTagService: scopeTagService);
        var table = new MigrationTable();

        var scopeTag = new RoleScopeTag
        {
            Id = "old-scope",
            DisplayName = "Source Scope"
        };

        var created = await sut.ImportScopeTagAsync(scopeTag, table);

        Assert.Equal("new-scope", created.Id);
        Assert.NotNull(scopeTagService.LastCreatedScopeTag);
        Assert.Null(scopeTagService.LastCreatedScopeTag!.Id);
        Assert.Single(table.Entries);
        Assert.Equal("ScopeTag", table.Entries[0].ObjectType);
        Assert.Equal("old-scope", table.Entries[0].OriginalId);
        Assert.Equal("new-scope", table.Entries[0].NewId);
    }

    [Fact]
    public async Task ImportRoleDefinitionAsync_UpdatesMigration()
    {
        var roleDefinitionService = new StubRoleDefinitionService
        {
            CreateResult = new RoleDefinition { Id = "new-role", DisplayName = "Created Role" }
        };
        var sut = new ImportService(new StubConfigurationService(), null, roleDefinitionService: roleDefinitionService);
        var table = new MigrationTable();

        var roleDefinition = new RoleDefinition
        {
            Id = "old-role",
            DisplayName = "Source Role"
        };

        var created = await sut.ImportRoleDefinitionAsync(roleDefinition, table);

        Assert.Equal("new-role", created.Id);
        Assert.NotNull(roleDefinitionService.LastCreatedRoleDefinition);
        Assert.Null(roleDefinitionService.LastCreatedRoleDefinition!.Id);
        Assert.Single(table.Entries);
        Assert.Equal("RoleDefinition", table.Entries[0].ObjectType);
        Assert.Equal("old-role", table.Entries[0].OriginalId);
        Assert.Equal("new-role", table.Entries[0].NewId);
    }

    [Fact]
    public async Task ImportIntuneBrandingProfileAsync_UpdatesMigration()
    {
        var brandingService = new StubIntuneBrandingService
        {
            CreateResult = new IntuneBrandingProfile { Id = "new-branding", ProfileName = "Created Branding" }
        };
        var sut = new ImportService(new StubConfigurationService(), null, intuneBrandingService: brandingService);
        var table = new MigrationTable();

        var profile = new IntuneBrandingProfile
        {
            Id = "old-branding",
            ProfileName = "Source Branding"
        };

        var created = await sut.ImportIntuneBrandingProfileAsync(profile, table);

        Assert.Equal("new-branding", created.Id);
        Assert.NotNull(brandingService.LastCreatedProfile);
        Assert.Null(brandingService.LastCreatedProfile!.Id);
        Assert.Single(table.Entries);
        Assert.Equal("IntuneBrandingProfile", table.Entries[0].ObjectType);
        Assert.Equal("old-branding", table.Entries[0].OriginalId);
        Assert.Equal("new-branding", table.Entries[0].NewId);
    }

    [Fact]
    public async Task ImportAzureBrandingLocalizationAsync_UpdatesMigration()
    {
        var azureBrandingService = new StubAzureBrandingService
        {
            CreateResult = new OrganizationalBrandingLocalization { Id = "new-locale" }
        };
        var sut = new ImportService(new StubConfigurationService(), null, azureBrandingService: azureBrandingService);
        var table = new MigrationTable();

        var localization = new OrganizationalBrandingLocalization
        {
            Id = "old-locale"
        };

        var created = await sut.ImportAzureBrandingLocalizationAsync(localization, table);

        Assert.Equal("new-locale", created.Id);
        Assert.NotNull(azureBrandingService.LastCreatedLocalization);
        Assert.Null(azureBrandingService.LastCreatedLocalization!.Id);
        Assert.Single(table.Entries);
        Assert.Equal("AzureBrandingLocalization", table.Entries[0].ObjectType);
        Assert.Equal("old-locale", table.Entries[0].OriginalId);
        Assert.Equal("new-locale", table.Entries[0].NewId);
    }

    [Fact]
    public async Task ImportAutopilotProfileAsync_UpdatesMigration()
    {
        var autopilotService = new StubAutopilotService
        {
            CreateResult = new WindowsAutopilotDeploymentProfile { Id = "new-autopilot", DisplayName = "Created Autopilot" }
        };
        var sut = new ImportService(new StubConfigurationService(), null, autopilotService: autopilotService);
        var table = new MigrationTable();

        var profile = new WindowsAutopilotDeploymentProfile
        {
            Id = "old-autopilot",
            DisplayName = "Source Autopilot"
        };

        var created = await sut.ImportAutopilotProfileAsync(profile, table);

        Assert.Equal("new-autopilot", created.Id);
        Assert.NotNull(autopilotService.LastCreatedProfile);
        Assert.Null(autopilotService.LastCreatedProfile!.Id);
        Assert.Single(table.Entries);
        Assert.Equal("AutopilotProfile", table.Entries[0].ObjectType);
        Assert.Equal("old-autopilot", table.Entries[0].OriginalId);
        Assert.Equal("new-autopilot", table.Entries[0].NewId);
    }

    [Fact]
    public async Task ImportTermsOfUseAgreementAsync_UpdatesMigration()
    {
        var termsOfUseService = new StubTermsOfUseService
        {
            CreateResult = new Agreement { Id = "new-tou", DisplayName = "Created Terms" }
        };
        var sut = new ImportService(new StubConfigurationService(), null, termsOfUseService: termsOfUseService);
        var table = new MigrationTable();

        var agreement = new Agreement
        {
            Id = "old-tou",
            DisplayName = "Source Terms"
        };

        var created = await sut.ImportTermsOfUseAgreementAsync(agreement, table);

        Assert.Equal("new-tou", created.Id);
        Assert.NotNull(termsOfUseService.LastCreatedAgreement);
        Assert.Null(termsOfUseService.LastCreatedAgreement!.Id);
        Assert.Single(table.Entries);
        Assert.Equal("TermsOfUseAgreement", table.Entries[0].ObjectType);
        Assert.Equal("old-tou", table.Entries[0].OriginalId);
        Assert.Equal("new-tou", table.Entries[0].NewId);
    }

    [Fact]
    public async Task ReadDeviceConfigurationAsync_MalformedJson_ThrowsJsonException()
    {
        var file = Path.Combine(_tempDir, "bad.json");
        await File.WriteAllTextAsync(file, "{ this is not valid json }}}");

        var sut = new ImportService(new StubConfigurationService());

        await Assert.ThrowsAsync<JsonException>(() => sut.ReadDeviceConfigurationAsync(file));
    }

    [Fact]
    public async Task ImportDeviceHealthScriptAsync_UpdatesMigration()
    {
        var healthScriptService = new StubDeviceHealthScriptService
        {
            CreateResult = new DeviceHealthScript { Id = "new-dhs", DisplayName = "Created Script" }
        };
        var sut = new ImportService(new StubConfigurationService(), null, deviceHealthScriptService: healthScriptService);
        var table = new MigrationTable();

        var script = new DeviceHealthScript
        {
            Id = "old-dhs",
            DisplayName = "Source Script",
            CreatedDateTime = DateTimeOffset.UtcNow,
            LastModifiedDateTime = DateTimeOffset.UtcNow
        };

        var created = await sut.ImportDeviceHealthScriptAsync(script, table);

        Assert.Equal("new-dhs", created.Id);
        Assert.NotNull(healthScriptService.LastCreatedScript);
        Assert.Null(healthScriptService.LastCreatedScript!.Id);
        Assert.Single(table.Entries);
        Assert.Equal("DeviceHealthScript", table.Entries[0].ObjectType);
        Assert.Equal("old-dhs", table.Entries[0].OriginalId);
        Assert.Equal("new-dhs", table.Entries[0].NewId);
    }

    [Fact]
    public async Task ImportMacCustomAttributeAsync_UpdatesMigration()
    {
        var macAttributeService = new StubMacCustomAttributeService
        {
            CreateResult = new DeviceCustomAttributeShellScript { Id = "new-mac", DisplayName = "Created Attr" }
        };
        var sut = new ImportService(new StubConfigurationService(), null, macCustomAttributeService: macAttributeService);
        var table = new MigrationTable();

        var script = new DeviceCustomAttributeShellScript
        {
            Id = "old-mac",
            DisplayName = "Source Attr",
            CreatedDateTime = DateTimeOffset.UtcNow,
            LastModifiedDateTime = DateTimeOffset.UtcNow
        };

        var created = await sut.ImportMacCustomAttributeAsync(script, table);

        Assert.Equal("new-mac", created.Id);
        Assert.NotNull(macAttributeService.LastCreatedScript);
        Assert.Null(macAttributeService.LastCreatedScript!.Id);
        Assert.Single(table.Entries);
        Assert.Equal("MacCustomAttribute", table.Entries[0].ObjectType);
        Assert.Equal("old-mac", table.Entries[0].OriginalId);
        Assert.Equal("new-mac", table.Entries[0].NewId);
    }

    [Fact]
    public async Task ImportFeatureUpdateProfileAsync_UpdatesMigration()
    {
        var featureUpdateService = new StubFeatureUpdateProfileService
        {
            CreateResult = new WindowsFeatureUpdateProfile { Id = "new-fup", DisplayName = "Created Feature Update" }
        };
        var sut = new ImportService(new StubConfigurationService(), null, featureUpdateProfileService: featureUpdateService);
        var table = new MigrationTable();

        var profile = new WindowsFeatureUpdateProfile
        {
            Id = "old-fup",
            DisplayName = "Source Feature Update",
            CreatedDateTime = DateTimeOffset.UtcNow,
            LastModifiedDateTime = DateTimeOffset.UtcNow
        };

        var created = await sut.ImportFeatureUpdateProfileAsync(profile, table);

        Assert.Equal("new-fup", created.Id);
        Assert.NotNull(featureUpdateService.LastCreatedProfile);
        Assert.Null(featureUpdateService.LastCreatedProfile!.Id);
        Assert.Single(table.Entries);
        Assert.Equal("FeatureUpdateProfile", table.Entries[0].ObjectType);
        Assert.Equal("old-fup", table.Entries[0].OriginalId);
        Assert.Equal("new-fup", table.Entries[0].NewId);
    }

    [Fact]
    public async Task ImportNamedLocationAsync_UpdatesMigration()
    {
        var namedLocationService = new StubNamedLocationService
        {
            CreateResult = new NamedLocation
            {
                Id = "new-nl",
                AdditionalData = new Dictionary<string, object> { ["displayName"] = "Created Location" }
            }
        };
        var sut = new ImportService(new StubConfigurationService(), null, namedLocationService: namedLocationService);
        var table = new MigrationTable();

        var namedLocation = new NamedLocation
        {
            Id = "old-nl",
            AdditionalData = new Dictionary<string, object> { ["displayName"] = "Source Location" }
        };

        var created = await sut.ImportNamedLocationAsync(namedLocation, table);

        Assert.Equal("new-nl", created.Id);
        Assert.NotNull(namedLocationService.LastCreatedNamedLocation);
        Assert.Null(namedLocationService.LastCreatedNamedLocation!.Id);
        Assert.Single(table.Entries);
        Assert.Equal("NamedLocation", table.Entries[0].ObjectType);
        Assert.Equal("old-nl", table.Entries[0].OriginalId);
        Assert.Equal("new-nl", table.Entries[0].NewId);
    }

    [Fact]
    public async Task ImportAuthenticationStrengthPolicyAsync_UpdatesMigration()
    {
        var authStrengthService = new StubAuthenticationStrengthService
        {
            CreateResult = new AuthenticationStrengthPolicy { Id = "new-asp", DisplayName = "Created Strength" }
        };
        var sut = new ImportService(new StubConfigurationService(), null, authenticationStrengthService: authStrengthService);
        var table = new MigrationTable();

        var policy = new AuthenticationStrengthPolicy
        {
            Id = "old-asp",
            DisplayName = "Source Strength"
        };

        var created = await sut.ImportAuthenticationStrengthPolicyAsync(policy, table);

        Assert.Equal("new-asp", created.Id);
        Assert.NotNull(authStrengthService.LastCreatedPolicy);
        Assert.Null(authStrengthService.LastCreatedPolicy!.Id);
        Assert.Single(table.Entries);
        Assert.Equal("AuthenticationStrengthPolicy", table.Entries[0].ObjectType);
        Assert.Equal("old-asp", table.Entries[0].OriginalId);
        Assert.Equal("new-asp", table.Entries[0].NewId);
    }

    [Fact]
    public async Task ImportAuthenticationContextAsync_UpdatesMigration()
    {
        var authContextService = new StubAuthenticationContextService
        {
            CreateResult = new AuthenticationContextClassReference { Id = "new-ctx", DisplayName = "Created Context" }
        };
        var sut = new ImportService(new StubConfigurationService(), null, authenticationContextService: authContextService);
        var table = new MigrationTable();

        var context = new AuthenticationContextClassReference
        {
            Id = "old-ctx",
            DisplayName = "Source Context"
        };

        var created = await sut.ImportAuthenticationContextAsync(context, table);

        Assert.Equal("new-ctx", created.Id);
        Assert.NotNull(authContextService.LastCreatedContext);
        Assert.Null(authContextService.LastCreatedContext!.Id);
        Assert.Single(table.Entries);
        Assert.Equal("AuthenticationContext", table.Entries[0].ObjectType);
        Assert.Equal("old-ctx", table.Entries[0].OriginalId);
        Assert.Equal("new-ctx", table.Entries[0].NewId);
    }

    // ---------- Legacy 12-param constructor ----------

    [Fact]
    public void LegacyConstructor_12Params_IsCallable()
    {
        var sut = new ImportService(
            new StubConfigurationService(), null, null, null, null, null, null, null, null, null, null, null);
        Assert.NotNull(sut);
    }

    // ---------- ReadFromFolder – missing folder → empty ----------

    [Fact]
    public async Task ReadEndpointSecurityIntentsFromFolderAsync_MissingFolder_ReturnsEmpty()
    {
        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadEndpointSecurityIntentsFromFolderAsync(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadAdministrativeTemplatesFromFolderAsync_MissingFolder_ReturnsEmpty()
    {
        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadAdministrativeTemplatesFromFolderAsync(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadEnrollmentConfigurationsFromFolderAsync_MissingFolder_ReturnsEmpty()
    {
        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadEnrollmentConfigurationsFromFolderAsync(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadAppProtectionPoliciesFromFolderAsync_MissingFolder_ReturnsEmpty()
    {
        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadAppProtectionPoliciesFromFolderAsync(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadManagedDeviceAppConfigurationsFromFolderAsync_MissingFolder_ReturnsEmpty()
    {
        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadManagedDeviceAppConfigurationsFromFolderAsync(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadTargetedManagedAppConfigurationsFromFolderAsync_MissingFolder_ReturnsEmpty()
    {
        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadTargetedManagedAppConfigurationsFromFolderAsync(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadTermsAndConditionsFromFolderAsync_MissingFolder_ReturnsEmpty()
    {
        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadTermsAndConditionsFromFolderAsync(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadScopeTagsFromFolderAsync_MissingFolder_ReturnsEmpty()
    {
        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadScopeTagsFromFolderAsync(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadRoleDefinitionsFromFolderAsync_MissingFolder_ReturnsEmpty()
    {
        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadRoleDefinitionsFromFolderAsync(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadIntuneBrandingProfilesFromFolderAsync_MissingFolder_ReturnsEmpty()
    {
        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadIntuneBrandingProfilesFromFolderAsync(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadAzureBrandingLocalizationsFromFolderAsync_MissingFolder_ReturnsEmpty()
    {
        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadAzureBrandingLocalizationsFromFolderAsync(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadAutopilotProfilesFromFolderAsync_MissingFolder_ReturnsEmpty()
    {
        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadAutopilotProfilesFromFolderAsync(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadDeviceHealthScriptsFromFolderAsync_MissingFolder_ReturnsEmpty()
    {
        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadDeviceHealthScriptsFromFolderAsync(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadMacCustomAttributesFromFolderAsync_MissingFolder_ReturnsEmpty()
    {
        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadMacCustomAttributesFromFolderAsync(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadFeatureUpdateProfilesFromFolderAsync_MissingFolder_ReturnsEmpty()
    {
        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadFeatureUpdateProfilesFromFolderAsync(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadNamedLocationsFromFolderAsync_MissingFolder_ReturnsEmpty()
    {
        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadNamedLocationsFromFolderAsync(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadAuthenticationStrengthPoliciesFromFolderAsync_MissingFolder_ReturnsEmpty()
    {
        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadAuthenticationStrengthPoliciesFromFolderAsync(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadAuthenticationContextsFromFolderAsync_MissingFolder_ReturnsEmpty()
    {
        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadAuthenticationContextsFromFolderAsync(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadTermsOfUseAgreementsFromFolderAsync_MissingFolder_ReturnsEmpty()
    {
        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadTermsOfUseAgreementsFromFolderAsync(_tempDir);
        Assert.Empty(result);
    }

    // ---------- ReadFromFolder – folder with files → reads all ----------

    [Fact]
    public async Task ReadEndpointSecurityIntentsFromFolderAsync_ReadsAllJsonFiles()
    {
        var folder = Path.Combine(_tempDir, "EndpointSecurity");
        Directory.CreateDirectory(folder);

        var e1 = new EndpointSecurityExport { Intent = new DeviceManagementIntent { Id = "e1", DisplayName = "E1" } };
        var e2 = new EndpointSecurityExport { Intent = new DeviceManagementIntent { Id = "e2", DisplayName = "E2" } };
        await File.WriteAllTextAsync(Path.Combine(folder, "e1.json"), System.Text.Json.JsonSerializer.Serialize(e1));
        await File.WriteAllTextAsync(Path.Combine(folder, "e2.json"), System.Text.Json.JsonSerializer.Serialize(e2));

        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadEndpointSecurityIntentsFromFolderAsync(_tempDir);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ReadAdministrativeTemplatesFromFolderAsync_ReadsAllJsonFiles()
    {
        var folder = Path.Combine(_tempDir, "AdministrativeTemplates");
        Directory.CreateDirectory(folder);

        var t1 = new AdministrativeTemplateExport { Template = new GroupPolicyConfiguration { Id = "t1", DisplayName = "T1" } };
        var t2 = new AdministrativeTemplateExport { Template = new GroupPolicyConfiguration { Id = "t2", DisplayName = "T2" } };
        await File.WriteAllTextAsync(Path.Combine(folder, "t1.json"), System.Text.Json.JsonSerializer.Serialize(t1));
        await File.WriteAllTextAsync(Path.Combine(folder, "t2.json"), System.Text.Json.JsonSerializer.Serialize(t2));

        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadAdministrativeTemplatesFromFolderAsync(_tempDir);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ReadEnrollmentConfigurationsFromFolderAsync_ReadsAllJsonFiles()
    {
        var folder = Path.Combine(_tempDir, "EnrollmentConfigurations");
        Directory.CreateDirectory(folder);

        var c1 = new DeviceEnrollmentConfiguration { Id = "ec1", DisplayName = "EC1" };
        var c2 = new DeviceEnrollmentConfiguration { Id = "ec2", DisplayName = "EC2" };
        await File.WriteAllTextAsync(Path.Combine(folder, "ec1.json"), System.Text.Json.JsonSerializer.Serialize(c1));
        await File.WriteAllTextAsync(Path.Combine(folder, "ec2.json"), System.Text.Json.JsonSerializer.Serialize(c2));

        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadEnrollmentConfigurationsFromFolderAsync(_tempDir);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ReadAppProtectionPoliciesFromFolderAsync_ReadsAllJsonFiles()
    {
        var folder = Path.Combine(_tempDir, "AppProtectionPolicies");
        Directory.CreateDirectory(folder);

        var p1 = new AndroidManagedAppProtection { Id = "app1", DisplayName = "App1" };
        var p2 = new AndroidManagedAppProtection { Id = "app2", DisplayName = "App2" };
        await File.WriteAllTextAsync(Path.Combine(folder, "p1.json"), System.Text.Json.JsonSerializer.Serialize(p1));
        await File.WriteAllTextAsync(Path.Combine(folder, "p2.json"), System.Text.Json.JsonSerializer.Serialize(p2));

        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadAppProtectionPoliciesFromFolderAsync(_tempDir);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ReadManagedDeviceAppConfigurationsFromFolderAsync_ReadsAllJsonFiles()
    {
        var folder = Path.Combine(_tempDir, "ManagedDeviceAppConfigurations");
        Directory.CreateDirectory(folder);

        var c1 = new ManagedDeviceMobileAppConfiguration { Id = "mdac1", DisplayName = "MDAC1" };
        var c2 = new ManagedDeviceMobileAppConfiguration { Id = "mdac2", DisplayName = "MDAC2" };
        await File.WriteAllTextAsync(Path.Combine(folder, "c1.json"), System.Text.Json.JsonSerializer.Serialize(c1));
        await File.WriteAllTextAsync(Path.Combine(folder, "c2.json"), System.Text.Json.JsonSerializer.Serialize(c2));

        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadManagedDeviceAppConfigurationsFromFolderAsync(_tempDir);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ReadTargetedManagedAppConfigurationsFromFolderAsync_ReadsAllJsonFiles()
    {
        var folder = Path.Combine(_tempDir, "TargetedManagedAppConfigurations");
        Directory.CreateDirectory(folder);

        var c1 = new TargetedManagedAppConfiguration { Id = "tmac1", DisplayName = "TMAC1" };
        var c2 = new TargetedManagedAppConfiguration { Id = "tmac2", DisplayName = "TMAC2" };
        await File.WriteAllTextAsync(Path.Combine(folder, "c1.json"), System.Text.Json.JsonSerializer.Serialize(c1));
        await File.WriteAllTextAsync(Path.Combine(folder, "c2.json"), System.Text.Json.JsonSerializer.Serialize(c2));

        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadTargetedManagedAppConfigurationsFromFolderAsync(_tempDir);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ReadTermsAndConditionsFromFolderAsync_ReadsAllJsonFiles()
    {
        var folder = Path.Combine(_tempDir, "TermsAndConditions");
        Directory.CreateDirectory(folder);

        var t1 = new TermsAndConditions { Id = "tnc1", DisplayName = "TNC1" };
        var t2 = new TermsAndConditions { Id = "tnc2", DisplayName = "TNC2" };
        await File.WriteAllTextAsync(Path.Combine(folder, "t1.json"), System.Text.Json.JsonSerializer.Serialize(t1));
        await File.WriteAllTextAsync(Path.Combine(folder, "t2.json"), System.Text.Json.JsonSerializer.Serialize(t2));

        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadTermsAndConditionsFromFolderAsync(_tempDir);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ReadScopeTagsFromFolderAsync_ReadsAllJsonFiles()
    {
        var folder = Path.Combine(_tempDir, "ScopeTags");
        Directory.CreateDirectory(folder);

        var s1 = new RoleScopeTag { Id = "st1", DisplayName = "ST1" };
        var s2 = new RoleScopeTag { Id = "st2", DisplayName = "ST2" };
        await File.WriteAllTextAsync(Path.Combine(folder, "s1.json"), System.Text.Json.JsonSerializer.Serialize(s1));
        await File.WriteAllTextAsync(Path.Combine(folder, "s2.json"), System.Text.Json.JsonSerializer.Serialize(s2));

        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadScopeTagsFromFolderAsync(_tempDir);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ReadRoleDefinitionsFromFolderAsync_ReadsAllJsonFiles()
    {
        var folder = Path.Combine(_tempDir, "RoleDefinitions");
        Directory.CreateDirectory(folder);

        var r1 = new RoleDefinition { Id = "rd1", DisplayName = "RD1" };
        var r2 = new RoleDefinition { Id = "rd2", DisplayName = "RD2" };
        await File.WriteAllTextAsync(Path.Combine(folder, "r1.json"), System.Text.Json.JsonSerializer.Serialize(r1));
        await File.WriteAllTextAsync(Path.Combine(folder, "r2.json"), System.Text.Json.JsonSerializer.Serialize(r2));

        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadRoleDefinitionsFromFolderAsync(_tempDir);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ReadIntuneBrandingProfilesFromFolderAsync_ReadsAllJsonFiles()
    {
        var folder = Path.Combine(_tempDir, "IntuneBrandingProfiles");
        Directory.CreateDirectory(folder);

        var b1 = new IntuneBrandingProfile { Id = "ibp1", ProfileName = "IBP1" };
        var b2 = new IntuneBrandingProfile { Id = "ibp2", ProfileName = "IBP2" };
        await File.WriteAllTextAsync(Path.Combine(folder, "b1.json"), System.Text.Json.JsonSerializer.Serialize(b1));
        await File.WriteAllTextAsync(Path.Combine(folder, "b2.json"), System.Text.Json.JsonSerializer.Serialize(b2));

        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadIntuneBrandingProfilesFromFolderAsync(_tempDir);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ReadAzureBrandingLocalizationsFromFolderAsync_ReadsAllJsonFiles()
    {
        var folder = Path.Combine(_tempDir, "AzureBrandingLocalizations");
        Directory.CreateDirectory(folder);

        var l1 = new OrganizationalBrandingLocalization { Id = "loc1" };
        var l2 = new OrganizationalBrandingLocalization { Id = "loc2" };
        await File.WriteAllTextAsync(Path.Combine(folder, "l1.json"), System.Text.Json.JsonSerializer.Serialize(l1));
        await File.WriteAllTextAsync(Path.Combine(folder, "l2.json"), System.Text.Json.JsonSerializer.Serialize(l2));

        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadAzureBrandingLocalizationsFromFolderAsync(_tempDir);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ReadAutopilotProfilesFromFolderAsync_ReadsAllJsonFiles()
    {
        var folder = Path.Combine(_tempDir, "AutopilotProfiles");
        Directory.CreateDirectory(folder);

        var p1 = new WindowsAutopilotDeploymentProfile { Id = "ap1", DisplayName = "AP1" };
        var p2 = new WindowsAutopilotDeploymentProfile { Id = "ap2", DisplayName = "AP2" };
        await File.WriteAllTextAsync(Path.Combine(folder, "p1.json"), System.Text.Json.JsonSerializer.Serialize(p1));
        await File.WriteAllTextAsync(Path.Combine(folder, "p2.json"), System.Text.Json.JsonSerializer.Serialize(p2));

        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadAutopilotProfilesFromFolderAsync(_tempDir);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ReadDeviceHealthScriptsFromFolderAsync_ReadsAllJsonFiles()
    {
        var folder = Path.Combine(_tempDir, "DeviceHealthScripts");
        Directory.CreateDirectory(folder);

        var s1 = new DeviceHealthScript { Id = "dhs1", DisplayName = "DHS1" };
        var s2 = new DeviceHealthScript { Id = "dhs2", DisplayName = "DHS2" };
        await File.WriteAllTextAsync(Path.Combine(folder, "s1.json"), System.Text.Json.JsonSerializer.Serialize(s1));
        await File.WriteAllTextAsync(Path.Combine(folder, "s2.json"), System.Text.Json.JsonSerializer.Serialize(s2));

        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadDeviceHealthScriptsFromFolderAsync(_tempDir);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ReadMacCustomAttributesFromFolderAsync_ReadsAllJsonFiles()
    {
        var folder = Path.Combine(_tempDir, "MacCustomAttributes");
        Directory.CreateDirectory(folder);

        var a1 = new DeviceCustomAttributeShellScript { Id = "mca1", DisplayName = "MCA1" };
        var a2 = new DeviceCustomAttributeShellScript { Id = "mca2", DisplayName = "MCA2" };
        await File.WriteAllTextAsync(Path.Combine(folder, "a1.json"), System.Text.Json.JsonSerializer.Serialize(a1));
        await File.WriteAllTextAsync(Path.Combine(folder, "a2.json"), System.Text.Json.JsonSerializer.Serialize(a2));

        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadMacCustomAttributesFromFolderAsync(_tempDir);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ReadFeatureUpdateProfilesFromFolderAsync_ReadsAllJsonFiles()
    {
        var folder = Path.Combine(_tempDir, "FeatureUpdates");
        Directory.CreateDirectory(folder);

        var p1 = new WindowsFeatureUpdateProfile { Id = "fup1", DisplayName = "FUP1" };
        var p2 = new WindowsFeatureUpdateProfile { Id = "fup2", DisplayName = "FUP2" };
        await File.WriteAllTextAsync(Path.Combine(folder, "p1.json"), System.Text.Json.JsonSerializer.Serialize(p1));
        await File.WriteAllTextAsync(Path.Combine(folder, "p2.json"), System.Text.Json.JsonSerializer.Serialize(p2));

        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadFeatureUpdateProfilesFromFolderAsync(_tempDir);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ReadNamedLocationsFromFolderAsync_ReadsAllJsonFiles()
    {
        var folder = Path.Combine(_tempDir, "NamedLocations");
        Directory.CreateDirectory(folder);

        var n1 = new NamedLocation { Id = "nl1" };
        var n2 = new NamedLocation { Id = "nl2" };
        await File.WriteAllTextAsync(Path.Combine(folder, "n1.json"), System.Text.Json.JsonSerializer.Serialize(n1));
        await File.WriteAllTextAsync(Path.Combine(folder, "n2.json"), System.Text.Json.JsonSerializer.Serialize(n2));

        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadNamedLocationsFromFolderAsync(_tempDir);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ReadAuthenticationStrengthPoliciesFromFolderAsync_ReadsAllJsonFiles()
    {
        var folder = Path.Combine(_tempDir, "AuthenticationStrengths");
        Directory.CreateDirectory(folder);

        var p1 = new AuthenticationStrengthPolicy { Id = "asp1", DisplayName = "ASP1" };
        var p2 = new AuthenticationStrengthPolicy { Id = "asp2", DisplayName = "ASP2" };
        await File.WriteAllTextAsync(Path.Combine(folder, "p1.json"), System.Text.Json.JsonSerializer.Serialize(p1));
        await File.WriteAllTextAsync(Path.Combine(folder, "p2.json"), System.Text.Json.JsonSerializer.Serialize(p2));

        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadAuthenticationStrengthPoliciesFromFolderAsync(_tempDir);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ReadAuthenticationContextsFromFolderAsync_ReadsAllJsonFiles()
    {
        var folder = Path.Combine(_tempDir, "AuthenticationContexts");
        Directory.CreateDirectory(folder);

        var c1 = new AuthenticationContextClassReference { Id = "ctx1", DisplayName = "CTX1" };
        var c2 = new AuthenticationContextClassReference { Id = "ctx2", DisplayName = "CTX2" };
        await File.WriteAllTextAsync(Path.Combine(folder, "c1.json"), System.Text.Json.JsonSerializer.Serialize(c1));
        await File.WriteAllTextAsync(Path.Combine(folder, "c2.json"), System.Text.Json.JsonSerializer.Serialize(c2));

        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadAuthenticationContextsFromFolderAsync(_tempDir);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ReadTermsOfUseAgreementsFromFolderAsync_ReadsAllJsonFiles()
    {
        var folder = Path.Combine(_tempDir, "TermsOfUse");
        Directory.CreateDirectory(folder);

        var a1 = new Agreement { Id = "tou1", DisplayName = "TOU1" };
        var a2 = new Agreement { Id = "tou2", DisplayName = "TOU2" };
        await File.WriteAllTextAsync(Path.Combine(folder, "a1.json"), System.Text.Json.JsonSerializer.Serialize(a1));
        await File.WriteAllTextAsync(Path.Combine(folder, "a2.json"), System.Text.Json.JsonSerializer.Serialize(a2));

        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadTermsOfUseAgreementsFromFolderAsync(_tempDir);

        Assert.Equal(2, result.Count);
    }

    // ---------- ImportNamedLocationAsync edge case: null AdditionalData ----------

    [Fact]
    public async Task ImportNamedLocationAsync_NullAdditionalData_UsesUnknownName()
    {
        var namedLocationService = new StubNamedLocationService
        {
            CreateResult = new NamedLocation { Id = "created-nl", AdditionalData = null }
        };
        var sut = new ImportService(new StubConfigurationService(), null, namedLocationService: namedLocationService);
        var table = new MigrationTable();

        var namedLocation = new NamedLocation { Id = "orig-nl" };
        await sut.ImportNamedLocationAsync(namedLocation, table);

        Assert.Single(table.Entries);
        Assert.Equal("Unknown", table.Entries[0].Name);
    }

    [Fact]
    public async Task ReadDeviceManagementScriptsFromFolderAsync_MissingFolder_ReturnsEmpty()
    {
        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadDeviceManagementScriptsFromFolderAsync(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadDeviceManagementScriptsFromFolderAsync_ReadsAllJsonFiles()
    {
        var folder = Path.Combine(_tempDir, "DeviceManagementScripts");
        Directory.CreateDirectory(folder);

        var s1 = new DeviceManagementScript { Id = "dms1", DisplayName = "Script1" };
        var s2 = new DeviceManagementScript { Id = "dms2", DisplayName = "Script2" };
        await File.WriteAllTextAsync(Path.Combine(folder, "s1.json"), System.Text.Json.JsonSerializer.Serialize(s1));
        await File.WriteAllTextAsync(Path.Combine(folder, "s2.json"), System.Text.Json.JsonSerializer.Serialize(s2));

        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadDeviceManagementScriptsFromFolderAsync(_tempDir);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ImportDeviceManagementScriptAsync_UpdatesMigration()
    {
        var scriptService = new StubDeviceManagementScriptService
        {
            CreateResult = new DeviceManagementScript { Id = "new-dms", DisplayName = "Created Script" }
        };
        var sut = new ImportService(new StubConfigurationService(), null, deviceManagementScriptService: scriptService);
        var table = new MigrationTable();

        var script = new DeviceManagementScript
        {
            Id = "old-dms",
            DisplayName = "Source Script",
            CreatedDateTime = DateTimeOffset.UtcNow,
            LastModifiedDateTime = DateTimeOffset.UtcNow
        };

        var created = await sut.ImportDeviceManagementScriptAsync(script, table);

        Assert.Equal("new-dms", created.Id);
        Assert.NotNull(scriptService.LastCreatedScript);
        Assert.Null(scriptService.LastCreatedScript!.Id);
        Assert.Single(table.Entries);
        Assert.Equal("DeviceManagementScript", table.Entries[0].ObjectType);
        Assert.Equal("old-dms", table.Entries[0].OriginalId);
        Assert.Equal("new-dms", table.Entries[0].NewId);
    }

    [Fact]
    public async Task ReadDeviceShellScriptsFromFolderAsync_MissingFolder_ReturnsEmpty()
    {
        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadDeviceShellScriptsFromFolderAsync(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadDeviceShellScriptsFromFolderAsync_ReadsAllJsonFiles()
    {
        var folder = Path.Combine(_tempDir, "DeviceShellScripts");
        Directory.CreateDirectory(folder);

        var s1 = new DeviceShellScript { Id = "dss1", DisplayName = "Shell1" };
        var s2 = new DeviceShellScript { Id = "dss2", DisplayName = "Shell2" };
        await File.WriteAllTextAsync(Path.Combine(folder, "s1.json"), System.Text.Json.JsonSerializer.Serialize(s1));
        await File.WriteAllTextAsync(Path.Combine(folder, "s2.json"), System.Text.Json.JsonSerializer.Serialize(s2));

        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadDeviceShellScriptsFromFolderAsync(_tempDir);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ImportDeviceShellScriptAsync_UpdatesMigration()
    {
        var scriptService = new StubDeviceShellScriptService
        {
            CreateResult = new DeviceShellScript { Id = "new-dss", DisplayName = "Created Shell" }
        };
        var sut = new ImportService(new StubConfigurationService(), null, deviceShellScriptService: scriptService);
        var table = new MigrationTable();

        var script = new DeviceShellScript
        {
            Id = "old-dss",
            DisplayName = "Source Shell",
            CreatedDateTime = DateTimeOffset.UtcNow,
            LastModifiedDateTime = DateTimeOffset.UtcNow
        };

        var created = await sut.ImportDeviceShellScriptAsync(script, table);

        Assert.Equal("new-dss", created.Id);
        Assert.NotNull(scriptService.LastCreatedScript);
        Assert.Null(scriptService.LastCreatedScript!.Id);
        Assert.Single(table.Entries);
        Assert.Equal("DeviceShellScript", table.Entries[0].ObjectType);
        Assert.Equal("old-dss", table.Entries[0].OriginalId);
        Assert.Equal("new-dss", table.Entries[0].NewId);
    }

    [Fact]
    public async Task ReadComplianceScriptsFromFolderAsync_MissingFolder_ReturnsEmpty()
    {
        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadComplianceScriptsFromFolderAsync(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadComplianceScriptsFromFolderAsync_ReadsAllJsonFiles()
    {
        var folder = Path.Combine(_tempDir, "ComplianceScripts");
        Directory.CreateDirectory(folder);

        var s1 = new DeviceComplianceScript { Id = "cs1", DisplayName = "CS1" };
        var s2 = new DeviceComplianceScript { Id = "cs2", DisplayName = "CS2" };
        await File.WriteAllTextAsync(Path.Combine(folder, "s1.json"), System.Text.Json.JsonSerializer.Serialize(s1));
        await File.WriteAllTextAsync(Path.Combine(folder, "s2.json"), System.Text.Json.JsonSerializer.Serialize(s2));

        var sut = new ImportService(new StubConfigurationService());
        var result = await sut.ReadComplianceScriptsFromFolderAsync(_tempDir);

        Assert.Equal(2, result.Count);
    }

    private sealed class StubConfigurationService : IConfigurationProfileService
    {
        public DeviceConfiguration? LastCreatedConfig { get; private set; }
        public DeviceConfiguration CreateResult { get; set; } = new() { Id = "created", DisplayName = "Created" };

        public Task<List<DeviceConfiguration>> ListDeviceConfigurationsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<DeviceConfiguration>());

        public Task<DeviceConfiguration?> GetDeviceConfigurationAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<DeviceConfiguration?>(null);

        public Task<DeviceConfiguration> CreateDeviceConfigurationAsync(DeviceConfiguration config, CancellationToken cancellationToken = default)
        {
            LastCreatedConfig = config;
            return Task.FromResult(CreateResult);
        }

        public Task<DeviceConfiguration> UpdateDeviceConfigurationAsync(DeviceConfiguration config, CancellationToken cancellationToken = default)
            => Task.FromResult(config);

        public Task DeleteDeviceConfigurationAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<List<DeviceConfigurationAssignment>> GetAssignmentsAsync(string configId, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<DeviceConfigurationAssignment>());
    }

    private sealed class StubComplianceService : ICompliancePolicyService
    {
        public DeviceCompliancePolicy? LastCreatedPolicy { get; private set; }
        public DeviceCompliancePolicy CreateResult { get; set; } = new() { Id = "created-policy", DisplayName = "Created" };

        public bool AssignCalled { get; private set; }
        public string? AssignedPolicyId { get; private set; }
        public List<DeviceCompliancePolicyAssignment>? AssignedAssignments { get; private set; }

        public Task<List<DeviceCompliancePolicy>> ListCompliancePoliciesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<DeviceCompliancePolicy>());

        public Task<DeviceCompliancePolicy?> GetCompliancePolicyAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<DeviceCompliancePolicy?>(null);

        public Task<DeviceCompliancePolicy> CreateCompliancePolicyAsync(DeviceCompliancePolicy policy, CancellationToken cancellationToken = default)
        {
            LastCreatedPolicy = policy;
            return Task.FromResult(CreateResult);
        }

        public Task<DeviceCompliancePolicy> UpdateCompliancePolicyAsync(DeviceCompliancePolicy policy, CancellationToken cancellationToken = default)
            => Task.FromResult(policy);

        public Task DeleteCompliancePolicyAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<List<DeviceCompliancePolicyAssignment>> GetAssignmentsAsync(string policyId, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<DeviceCompliancePolicyAssignment>());

        public Task AssignPolicyAsync(string policyId, List<DeviceCompliancePolicyAssignment> assignments, CancellationToken cancellationToken = default)
        {
            AssignCalled = true;
            AssignedPolicyId = policyId;
            AssignedAssignments = assignments;
            return Task.CompletedTask;
        }
    }

    private sealed class StubEndpointSecurityService : IEndpointSecurityService
    {
        public DeviceManagementIntent? LastCreatedIntent { get; private set; }
        public DeviceManagementIntent CreateResult { get; set; } = new() { Id = "created-intent", DisplayName = "Created" };

        public bool AssignCalled { get; private set; }
        public string? AssignedIntentId { get; private set; }
        public List<DeviceManagementIntentAssignment>? AssignedAssignments { get; private set; }

        public Task<List<DeviceManagementIntent>> ListEndpointSecurityIntentsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<DeviceManagementIntent>());

        public Task<DeviceManagementIntent?> GetEndpointSecurityIntentAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<DeviceManagementIntent?>(null);

        public Task<DeviceManagementIntent> CreateEndpointSecurityIntentAsync(DeviceManagementIntent intent, CancellationToken cancellationToken = default)
        {
            LastCreatedIntent = intent;
            return Task.FromResult(CreateResult);
        }

        public Task<DeviceManagementIntent> UpdateEndpointSecurityIntentAsync(DeviceManagementIntent intent, CancellationToken cancellationToken = default)
            => Task.FromResult(intent);

        public Task DeleteEndpointSecurityIntentAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<List<DeviceManagementIntentAssignment>> GetAssignmentsAsync(string intentId, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<DeviceManagementIntentAssignment>());

        public Task AssignIntentAsync(string intentId, List<DeviceManagementIntentAssignment> assignments, CancellationToken cancellationToken = default)
        {
            AssignCalled = true;
            AssignedIntentId = intentId;
            AssignedAssignments = assignments;
            return Task.CompletedTask;
        }
    }

    private sealed class StubAdministrativeTemplateService : IAdministrativeTemplateService
    {
        public GroupPolicyConfiguration? LastCreatedTemplate { get; private set; }
        public GroupPolicyConfiguration CreateResult { get; set; } = new() { Id = "created-template", DisplayName = "Created" };

        public bool AssignCalled { get; private set; }
        public string? AssignedTemplateId { get; private set; }
        public List<GroupPolicyConfigurationAssignment>? AssignedAssignments { get; private set; }

        public Task<List<GroupPolicyConfiguration>> ListAdministrativeTemplatesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<GroupPolicyConfiguration>());

        public Task<GroupPolicyConfiguration?> GetAdministrativeTemplateAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<GroupPolicyConfiguration?>(null);

        public Task<GroupPolicyConfiguration> CreateAdministrativeTemplateAsync(GroupPolicyConfiguration template, CancellationToken cancellationToken = default)
        {
            LastCreatedTemplate = template;
            return Task.FromResult(CreateResult);
        }

        public Task<GroupPolicyConfiguration> UpdateAdministrativeTemplateAsync(GroupPolicyConfiguration template, CancellationToken cancellationToken = default)
            => Task.FromResult(template);

        public Task DeleteAdministrativeTemplateAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<List<GroupPolicyConfigurationAssignment>> GetAssignmentsAsync(string templateId, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<GroupPolicyConfigurationAssignment>());

        public Task AssignAdministrativeTemplateAsync(string templateId, List<GroupPolicyConfigurationAssignment> assignments, CancellationToken cancellationToken = default)
        {
            AssignCalled = true;
            AssignedTemplateId = templateId;
            AssignedAssignments = assignments;
            return Task.CompletedTask;
        }
    }

    private sealed class StubEnrollmentConfigurationService : IEnrollmentConfigurationService
    {
        public DeviceEnrollmentConfiguration? LastCreatedConfiguration { get; private set; }
        public DeviceEnrollmentConfiguration CreateResult { get; set; } = new() { Id = "created-enroll", DisplayName = "Created" };

        public Task<List<DeviceEnrollmentConfiguration>> ListEnrollmentConfigurationsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<DeviceEnrollmentConfiguration>());

        public Task<List<DeviceEnrollmentConfiguration>> ListEnrollmentStatusPagesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<DeviceEnrollmentConfiguration>());

        public Task<List<DeviceEnrollmentConfiguration>> ListEnrollmentRestrictionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<DeviceEnrollmentConfiguration>());

        public Task<List<DeviceEnrollmentConfiguration>> ListCoManagementSettingsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<DeviceEnrollmentConfiguration>());

        public Task<DeviceEnrollmentConfiguration?> GetEnrollmentConfigurationAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<DeviceEnrollmentConfiguration?>(null);

        public Task<DeviceEnrollmentConfiguration> CreateEnrollmentConfigurationAsync(DeviceEnrollmentConfiguration configuration, CancellationToken cancellationToken = default)
        {
            LastCreatedConfiguration = configuration;
            return Task.FromResult(CreateResult);
        }

        public Task<DeviceEnrollmentConfiguration> UpdateEnrollmentConfigurationAsync(DeviceEnrollmentConfiguration configuration, CancellationToken cancellationToken = default)
            => Task.FromResult(configuration);

        public Task DeleteEnrollmentConfigurationAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubAppProtectionPolicyService : IAppProtectionPolicyService
    {
        public ManagedAppPolicy? LastCreatedPolicy { get; private set; }
        public ManagedAppPolicy CreateResult { get; set; } = new AndroidManagedAppProtection { Id = "created-app-protect", DisplayName = "Created" };

        public Task<List<ManagedAppPolicy>> ListAppProtectionPoliciesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<ManagedAppPolicy>());

        public Task<ManagedAppPolicy?> GetAppProtectionPolicyAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<ManagedAppPolicy?>(null);

        public Task<ManagedAppPolicy> CreateAppProtectionPolicyAsync(ManagedAppPolicy policy, CancellationToken cancellationToken = default)
        {
            LastCreatedPolicy = policy;
            return Task.FromResult(CreateResult);
        }

        public Task<ManagedAppPolicy> UpdateAppProtectionPolicyAsync(ManagedAppPolicy policy, CancellationToken cancellationToken = default)
            => Task.FromResult(policy);

        public Task DeleteAppProtectionPolicyAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubManagedAppConfigurationService : IManagedAppConfigurationService
    {
        public ManagedDeviceMobileAppConfiguration? LastCreatedManagedDeviceConfiguration { get; private set; }
        public TargetedManagedAppConfiguration? LastCreatedTargetedConfiguration { get; private set; }
        public ManagedDeviceMobileAppConfiguration CreateManagedDeviceResult { get; set; } = new() { Id = "created-mdac", DisplayName = "Created" };
        public TargetedManagedAppConfiguration CreateTargetedResult { get; set; } = new() { Id = "created-tmac", DisplayName = "Created" };

        public Task<List<ManagedDeviceMobileAppConfiguration>> ListManagedDeviceAppConfigurationsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<ManagedDeviceMobileAppConfiguration>());

        public Task<ManagedDeviceMobileAppConfiguration?> GetManagedDeviceAppConfigurationAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<ManagedDeviceMobileAppConfiguration?>(null);

        public Task<ManagedDeviceMobileAppConfiguration> CreateManagedDeviceAppConfigurationAsync(ManagedDeviceMobileAppConfiguration configuration, CancellationToken cancellationToken = default)
        {
            LastCreatedManagedDeviceConfiguration = configuration;
            return Task.FromResult(CreateManagedDeviceResult);
        }

        public Task<ManagedDeviceMobileAppConfiguration> UpdateManagedDeviceAppConfigurationAsync(ManagedDeviceMobileAppConfiguration configuration, CancellationToken cancellationToken = default)
            => Task.FromResult(configuration);

        public Task DeleteManagedDeviceAppConfigurationAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<List<TargetedManagedAppConfiguration>> ListTargetedManagedAppConfigurationsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<TargetedManagedAppConfiguration>());

        public Task<TargetedManagedAppConfiguration?> GetTargetedManagedAppConfigurationAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<TargetedManagedAppConfiguration?>(null);

        public Task<TargetedManagedAppConfiguration> CreateTargetedManagedAppConfigurationAsync(TargetedManagedAppConfiguration configuration, CancellationToken cancellationToken = default)
        {
            LastCreatedTargetedConfiguration = configuration;
            return Task.FromResult(CreateTargetedResult);
        }

        public Task<TargetedManagedAppConfiguration> UpdateTargetedManagedAppConfigurationAsync(TargetedManagedAppConfiguration configuration, CancellationToken cancellationToken = default)
            => Task.FromResult(configuration);

        public Task DeleteTargetedManagedAppConfigurationAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubTermsAndConditionsService : ITermsAndConditionsService
    {
        public TermsAndConditions? LastCreatedTerms { get; private set; }
        public TermsAndConditions CreateResult { get; set; } = new() { Id = "created-terms", DisplayName = "Created" };

        public Task<List<TermsAndConditions>> ListTermsAndConditionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<TermsAndConditions>());

        public Task<TermsAndConditions?> GetTermsAndConditionsAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<TermsAndConditions?>(null);

        public Task<TermsAndConditions> CreateTermsAndConditionsAsync(TermsAndConditions termsAndConditions, CancellationToken cancellationToken = default)
        {
            LastCreatedTerms = termsAndConditions;
            return Task.FromResult(CreateResult);
        }

        public Task<TermsAndConditions> UpdateTermsAndConditionsAsync(TermsAndConditions termsAndConditions, CancellationToken cancellationToken = default)
            => Task.FromResult(termsAndConditions);

        public Task DeleteTermsAndConditionsAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubScopeTagService : IScopeTagService
    {
        public RoleScopeTag? LastCreatedScopeTag { get; private set; }
        public RoleScopeTag CreateResult { get; set; } = new() { Id = "created-scope", DisplayName = "Created" };

        public Task<List<RoleScopeTag>> ListScopeTagsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<RoleScopeTag>());

        public Task<RoleScopeTag?> GetScopeTagAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<RoleScopeTag?>(null);

        public Task<RoleScopeTag> CreateScopeTagAsync(RoleScopeTag scopeTag, CancellationToken cancellationToken = default)
        {
            LastCreatedScopeTag = scopeTag;
            return Task.FromResult(CreateResult);
        }

        public Task<RoleScopeTag> UpdateScopeTagAsync(RoleScopeTag scopeTag, CancellationToken cancellationToken = default)
            => Task.FromResult(scopeTag);

        public Task DeleteScopeTagAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubRoleDefinitionService : IRoleDefinitionService
    {
        public RoleDefinition? LastCreatedRoleDefinition { get; private set; }
        public RoleDefinition CreateResult { get; set; } = new() { Id = "created-role", DisplayName = "Created" };

        public Task<List<RoleDefinition>> ListRoleDefinitionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<RoleDefinition>());

        public Task<RoleDefinition?> GetRoleDefinitionAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<RoleDefinition?>(null);

        public Task<RoleDefinition> CreateRoleDefinitionAsync(RoleDefinition roleDefinition, CancellationToken cancellationToken = default)
        {
            LastCreatedRoleDefinition = roleDefinition;
            return Task.FromResult(CreateResult);
        }

        public Task<RoleDefinition> UpdateRoleDefinitionAsync(RoleDefinition roleDefinition, CancellationToken cancellationToken = default)
            => Task.FromResult(roleDefinition);

        public Task DeleteRoleDefinitionAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubIntuneBrandingService : IIntuneBrandingService
    {
        public IntuneBrandingProfile? LastCreatedProfile { get; private set; }
        public IntuneBrandingProfile CreateResult { get; set; } = new() { Id = "created-branding", ProfileName = "Created" };

        public Task<List<IntuneBrandingProfile>> ListIntuneBrandingProfilesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<IntuneBrandingProfile>());

        public Task<IntuneBrandingProfile?> GetIntuneBrandingProfileAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<IntuneBrandingProfile?>(null);

        public Task<IntuneBrandingProfile> CreateIntuneBrandingProfileAsync(IntuneBrandingProfile profile, CancellationToken cancellationToken = default)
        {
            LastCreatedProfile = profile;
            return Task.FromResult(CreateResult);
        }

        public Task<IntuneBrandingProfile> UpdateIntuneBrandingProfileAsync(IntuneBrandingProfile profile, CancellationToken cancellationToken = default)
            => Task.FromResult(profile);

        public Task DeleteIntuneBrandingProfileAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubAzureBrandingService : IAzureBrandingService
    {
        public OrganizationalBrandingLocalization? LastCreatedLocalization { get; private set; }
        public OrganizationalBrandingLocalization CreateResult { get; set; } = new() { Id = "created-locale" };

        public Task<List<OrganizationalBrandingLocalization>> ListBrandingLocalizationsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<OrganizationalBrandingLocalization>());

        public Task<OrganizationalBrandingLocalization?> GetBrandingLocalizationAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<OrganizationalBrandingLocalization?>(null);

        public Task<OrganizationalBrandingLocalization> CreateBrandingLocalizationAsync(OrganizationalBrandingLocalization localization, CancellationToken cancellationToken = default)
        {
            LastCreatedLocalization = localization;
            return Task.FromResult(CreateResult);
        }

        public Task<OrganizationalBrandingLocalization> UpdateBrandingLocalizationAsync(OrganizationalBrandingLocalization localization, CancellationToken cancellationToken = default)
            => Task.FromResult(localization);

        public Task DeleteBrandingLocalizationAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubAutopilotService : IAutopilotService
    {
        public WindowsAutopilotDeploymentProfile? LastCreatedProfile { get; private set; }
        public WindowsAutopilotDeploymentProfile CreateResult { get; set; } = new() { Id = "created-autopilot", DisplayName = "Created" };

        public Task<List<WindowsAutopilotDeploymentProfile>> ListAutopilotProfilesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<WindowsAutopilotDeploymentProfile>());

        public Task<WindowsAutopilotDeploymentProfile?> GetAutopilotProfileAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<WindowsAutopilotDeploymentProfile?>(null);

        public Task<WindowsAutopilotDeploymentProfile> CreateAutopilotProfileAsync(WindowsAutopilotDeploymentProfile profile, CancellationToken cancellationToken = default)
        {
            LastCreatedProfile = profile;
            return Task.FromResult(CreateResult);
        }

        public Task<WindowsAutopilotDeploymentProfile> UpdateAutopilotProfileAsync(WindowsAutopilotDeploymentProfile profile, CancellationToken cancellationToken = default)
            => Task.FromResult(profile);

        public Task DeleteAutopilotProfileAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubTermsOfUseService : ITermsOfUseService
    {
        public Agreement? LastCreatedAgreement { get; private set; }
        public Agreement CreateResult { get; set; } = new() { Id = "created-tou", DisplayName = "Created" };

        public Task<List<Agreement>> ListTermsOfUseAgreementsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<Agreement>());

        public Task<Agreement?> GetTermsOfUseAgreementAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<Agreement?>(null);

        public Task<Agreement> CreateTermsOfUseAgreementAsync(Agreement agreement, CancellationToken cancellationToken = default)
        {
            LastCreatedAgreement = agreement;
            return Task.FromResult(CreateResult);
        }

        public Task<Agreement> UpdateTermsOfUseAgreementAsync(Agreement agreement, CancellationToken cancellationToken = default)
            => Task.FromResult(agreement);

        public Task DeleteTermsOfUseAgreementAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubDeviceHealthScriptService : IDeviceHealthScriptService
    {
        public DeviceHealthScript? LastCreatedScript { get; private set; }
        public DeviceHealthScript CreateResult { get; set; } = new() { Id = "created-dhs", DisplayName = "Created" };

        public Task<List<DeviceHealthScript>> ListDeviceHealthScriptsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<DeviceHealthScript>());

        public Task<DeviceHealthScript?> GetDeviceHealthScriptAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<DeviceHealthScript?>(null);

        public Task<DeviceHealthScript> CreateDeviceHealthScriptAsync(DeviceHealthScript script, CancellationToken cancellationToken = default)
        {
            LastCreatedScript = script;
            return Task.FromResult(CreateResult);
        }

        public Task<DeviceHealthScript> UpdateDeviceHealthScriptAsync(DeviceHealthScript script, CancellationToken cancellationToken = default)
            => Task.FromResult(script);

        public Task DeleteDeviceHealthScriptAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubMacCustomAttributeService : IMacCustomAttributeService
    {
        public DeviceCustomAttributeShellScript? LastCreatedScript { get; private set; }
        public DeviceCustomAttributeShellScript CreateResult { get; set; } = new() { Id = "created-mac", DisplayName = "Created" };

        public Task<List<DeviceCustomAttributeShellScript>> ListMacCustomAttributesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<DeviceCustomAttributeShellScript>());

        public Task<DeviceCustomAttributeShellScript?> GetMacCustomAttributeAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<DeviceCustomAttributeShellScript?>(null);

        public Task<DeviceCustomAttributeShellScript> CreateMacCustomAttributeAsync(DeviceCustomAttributeShellScript script, CancellationToken cancellationToken = default)
        {
            LastCreatedScript = script;
            return Task.FromResult(CreateResult);
        }

        public Task<DeviceCustomAttributeShellScript> UpdateMacCustomAttributeAsync(DeviceCustomAttributeShellScript script, CancellationToken cancellationToken = default)
            => Task.FromResult(script);

        public Task DeleteMacCustomAttributeAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubFeatureUpdateProfileService : IFeatureUpdateProfileService
    {
        public WindowsFeatureUpdateProfile? LastCreatedProfile { get; private set; }
        public WindowsFeatureUpdateProfile CreateResult { get; set; } = new() { Id = "created-fup", DisplayName = "Created" };

        public Task<List<WindowsFeatureUpdateProfile>> ListFeatureUpdateProfilesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<WindowsFeatureUpdateProfile>());

        public Task<WindowsFeatureUpdateProfile?> GetFeatureUpdateProfileAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<WindowsFeatureUpdateProfile?>(null);

        public Task<WindowsFeatureUpdateProfile> CreateFeatureUpdateProfileAsync(WindowsFeatureUpdateProfile profile, CancellationToken cancellationToken = default)
        {
            LastCreatedProfile = profile;
            return Task.FromResult(CreateResult);
        }

        public Task<WindowsFeatureUpdateProfile> UpdateFeatureUpdateProfileAsync(WindowsFeatureUpdateProfile profile, CancellationToken cancellationToken = default)
            => Task.FromResult(profile);

        public Task DeleteFeatureUpdateProfileAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubNamedLocationService : INamedLocationService
    {
        public NamedLocation? LastCreatedNamedLocation { get; private set; }
        public NamedLocation CreateResult { get; set; } = new() { Id = "created-nl", AdditionalData = new Dictionary<string, object> { ["displayName"] = "Created" } };

        public Task<List<NamedLocation>> ListNamedLocationsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<NamedLocation>());

        public Task<NamedLocation?> GetNamedLocationAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<NamedLocation?>(null);

        public Task<NamedLocation> CreateNamedLocationAsync(NamedLocation namedLocation, CancellationToken cancellationToken = default)
        {
            LastCreatedNamedLocation = namedLocation;
            return Task.FromResult(CreateResult);
        }

        public Task<NamedLocation> UpdateNamedLocationAsync(NamedLocation namedLocation, CancellationToken cancellationToken = default)
            => Task.FromResult(namedLocation);

        public Task DeleteNamedLocationAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubAuthenticationStrengthService : IAuthenticationStrengthService
    {
        public AuthenticationStrengthPolicy? LastCreatedPolicy { get; private set; }
        public AuthenticationStrengthPolicy CreateResult { get; set; } = new() { Id = "created-asp", DisplayName = "Created" };

        public Task<List<AuthenticationStrengthPolicy>> ListAuthenticationStrengthPoliciesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<AuthenticationStrengthPolicy>());

        public Task<AuthenticationStrengthPolicy?> GetAuthenticationStrengthPolicyAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<AuthenticationStrengthPolicy?>(null);

        public Task<AuthenticationStrengthPolicy> CreateAuthenticationStrengthPolicyAsync(AuthenticationStrengthPolicy policy, CancellationToken cancellationToken = default)
        {
            LastCreatedPolicy = policy;
            return Task.FromResult(CreateResult);
        }

        public Task<AuthenticationStrengthPolicy> UpdateAuthenticationStrengthPolicyAsync(AuthenticationStrengthPolicy policy, CancellationToken cancellationToken = default)
            => Task.FromResult(policy);

        public Task DeleteAuthenticationStrengthPolicyAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubAuthenticationContextService : IAuthenticationContextService
    {
        public AuthenticationContextClassReference? LastCreatedContext { get; private set; }
        public AuthenticationContextClassReference CreateResult { get; set; } = new() { Id = "created-ctx", DisplayName = "Created" };

        public Task<List<AuthenticationContextClassReference>> ListAuthenticationContextsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<AuthenticationContextClassReference>());

        public Task<AuthenticationContextClassReference?> GetAuthenticationContextAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<AuthenticationContextClassReference?>(null);

        public Task<AuthenticationContextClassReference> CreateAuthenticationContextAsync(AuthenticationContextClassReference contextClassReference, CancellationToken cancellationToken = default)
        {
            LastCreatedContext = contextClassReference;
            return Task.FromResult(CreateResult);
        }

        public Task<AuthenticationContextClassReference> UpdateAuthenticationContextAsync(AuthenticationContextClassReference contextClassReference, CancellationToken cancellationToken = default)
            => Task.FromResult(contextClassReference);

        public Task DeleteAuthenticationContextAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubDeviceManagementScriptService : IDeviceManagementScriptService
    {
        public DeviceManagementScript? LastCreatedScript { get; private set; }
        public DeviceManagementScript CreateResult { get; set; } = new() { Id = "created-dms", DisplayName = "Created" };

        public Task<List<DeviceManagementScript>> ListDeviceManagementScriptsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<DeviceManagementScript>());

        public Task<DeviceManagementScript?> GetDeviceManagementScriptAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<DeviceManagementScript?>(null);

        public Task<DeviceManagementScript> CreateDeviceManagementScriptAsync(DeviceManagementScript script, CancellationToken cancellationToken = default)
        {
            LastCreatedScript = script;
            return Task.FromResult(CreateResult);
        }

        public Task<DeviceManagementScript> UpdateDeviceManagementScriptAsync(DeviceManagementScript script, CancellationToken cancellationToken = default)
            => Task.FromResult(script);

        public Task DeleteDeviceManagementScriptAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<List<DeviceManagementScriptAssignment>> GetAssignmentsAsync(string scriptId, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<DeviceManagementScriptAssignment>());
    }

    private sealed class StubDeviceShellScriptService : IDeviceShellScriptService
    {
        public DeviceShellScript? LastCreatedScript { get; private set; }
        public DeviceShellScript CreateResult { get; set; } = new() { Id = "created-dss", DisplayName = "Created" };

        public Task<List<DeviceShellScript>> ListDeviceShellScriptsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<DeviceShellScript>());

        public Task<DeviceShellScript?> GetDeviceShellScriptAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<DeviceShellScript?>(null);

        public Task<DeviceShellScript> CreateDeviceShellScriptAsync(DeviceShellScript script, CancellationToken cancellationToken = default)
        {
            LastCreatedScript = script;
            return Task.FromResult(CreateResult);
        }

        public Task<DeviceShellScript> UpdateDeviceShellScriptAsync(DeviceShellScript script, CancellationToken cancellationToken = default)
            => Task.FromResult(script);

        public Task DeleteDeviceShellScriptAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<List<DeviceManagementScriptAssignment>> GetAssignmentsAsync(string scriptId, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<DeviceManagementScriptAssignment>());
    }
}
