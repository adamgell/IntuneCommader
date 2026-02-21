using Intune.Commander.Core.Models;
using Intune.Commander.Core.Services;
using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Tests.Services;

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

    [Fact]
    public async Task ExportEndpointSecurityIntent_CreatesJsonFile()
    {
        var intent = new DeviceManagementIntent
        {
            Id = "intent-id",
            DisplayName = "Endpoint Intent"
        };
        var assignments = new List<DeviceManagementIntentAssignment>();
        var table = new MigrationTable();

        await _service.ExportEndpointSecurityIntentAsync(intent, assignments, _tempDir, table);

        var expectedPath = Path.Combine(_tempDir, "EndpointSecurity", "Endpoint Intent.json");
        Assert.True(File.Exists(expectedPath));
        Assert.Contains(table.Entries, e => e.ObjectType == "EndpointSecurityIntent" && e.OriginalId == "intent-id");
    }

    [Fact]
    public async Task ExportAdministrativeTemplate_CreatesJsonFile()
    {
        var template = new GroupPolicyConfiguration
        {
            Id = "template-id",
            DisplayName = "Admin Template"
        };
        var assignments = new List<GroupPolicyConfigurationAssignment>();
        var table = new MigrationTable();

        await _service.ExportAdministrativeTemplateAsync(template, assignments, _tempDir, table);

        var expectedPath = Path.Combine(_tempDir, "AdministrativeTemplates", "Admin Template.json");
        Assert.True(File.Exists(expectedPath));
        Assert.Contains(table.Entries, e => e.ObjectType == "AdministrativeTemplate" && e.OriginalId == "template-id");
    }

    [Fact]
    public async Task ExportEnrollmentConfiguration_CreatesJsonFile()
    {
        var configuration = new DeviceEnrollmentConfiguration
        {
            Id = "enroll-id",
            DisplayName = "Enrollment Profile"
        };
        var table = new MigrationTable();

        await _service.ExportEnrollmentConfigurationAsync(configuration, _tempDir, table);

        var expectedPath = Path.Combine(_tempDir, "EnrollmentConfigurations", "Enrollment Profile.json");
        Assert.True(File.Exists(expectedPath));
        Assert.Contains(table.Entries, e => e.ObjectType == "EnrollmentConfiguration" && e.OriginalId == "enroll-id");
    }

    [Fact]
    public async Task ExportAppProtectionPolicy_CreatesJsonFile()
    {
        var policy = new AndroidManagedAppProtection
        {
            Id = "app-protect-id",
            DisplayName = "Android App Protection"
        };
        var table = new MigrationTable();

        await _service.ExportAppProtectionPolicyAsync(policy, _tempDir, table);

        var expectedPath = Path.Combine(_tempDir, "AppProtectionPolicies", "Android App Protection.json");
        Assert.True(File.Exists(expectedPath));
        Assert.Contains(table.Entries, e => e.ObjectType == "AppProtectionPolicy" && e.OriginalId == "app-protect-id");
    }

    [Fact]
    public async Task ExportManagedDeviceAppConfiguration_CreatesJsonFile()
    {
        var configuration = new ManagedDeviceMobileAppConfiguration
        {
            Id = "mdac-id",
            DisplayName = "Managed Device Config"
        };
        var table = new MigrationTable();

        await _service.ExportManagedDeviceAppConfigurationAsync(configuration, _tempDir, table);

        var expectedPath = Path.Combine(_tempDir, "ManagedDeviceAppConfigurations", "Managed Device Config.json");
        Assert.True(File.Exists(expectedPath));
        Assert.Contains(table.Entries, e => e.ObjectType == "ManagedDeviceAppConfiguration" && e.OriginalId == "mdac-id");
    }

    [Fact]
    public async Task ExportTargetedManagedAppConfiguration_CreatesJsonFile()
    {
        var configuration = new TargetedManagedAppConfiguration
        {
            Id = "tmac-id",
            DisplayName = "Targeted App Config"
        };
        var table = new MigrationTable();

        await _service.ExportTargetedManagedAppConfigurationAsync(configuration, _tempDir, table);

        var expectedPath = Path.Combine(_tempDir, "TargetedManagedAppConfigurations", "Targeted App Config.json");
        Assert.True(File.Exists(expectedPath));
        Assert.Contains(table.Entries, e => e.ObjectType == "TargetedManagedAppConfiguration" && e.OriginalId == "tmac-id");
    }

    [Fact]
    public async Task ExportTermsAndConditions_CreatesJsonFile()
    {
        var termsAndConditions = new TermsAndConditions
        {
            Id = "terms-id",
            DisplayName = "Tenant Terms"
        };
        var table = new MigrationTable();

        await _service.ExportTermsAndConditionsAsync(termsAndConditions, _tempDir, table);

        var expectedPath = Path.Combine(_tempDir, "TermsAndConditions", "Tenant Terms.json");
        Assert.True(File.Exists(expectedPath));
        Assert.Contains(table.Entries, e => e.ObjectType == "TermsAndConditions" && e.OriginalId == "terms-id");
    }

    [Fact]
    public async Task ExportScopeTag_CreatesJsonFile()
    {
        var scopeTag = new RoleScopeTag
        {
            Id = "scope-id",
            DisplayName = "Scope Tag One"
        };
        var table = new MigrationTable();

        await _service.ExportScopeTagAsync(scopeTag, _tempDir, table);

        var expectedPath = Path.Combine(_tempDir, "ScopeTags", "Scope Tag One.json");
        Assert.True(File.Exists(expectedPath));
        Assert.Contains(table.Entries, e => e.ObjectType == "ScopeTag" && e.OriginalId == "scope-id");
    }

    [Fact]
    public async Task ExportRoleDefinition_CreatesJsonFile()
    {
        var roleDefinition = new RoleDefinition
        {
            Id = "role-id",
            DisplayName = "Role Definition One"
        };
        var table = new MigrationTable();

        await _service.ExportRoleDefinitionAsync(roleDefinition, _tempDir, table);

        var expectedPath = Path.Combine(_tempDir, "RoleDefinitions", "Role Definition One.json");
        Assert.True(File.Exists(expectedPath));
        Assert.Contains(table.Entries, e => e.ObjectType == "RoleDefinition" && e.OriginalId == "role-id");
    }

    [Fact]
    public async Task ExportIntuneBrandingProfile_CreatesJsonFile()
    {
        var profile = new IntuneBrandingProfile
        {
            Id = "branding-id",
            ProfileName = "Branding Profile One"
        };
        var table = new MigrationTable();

        await _service.ExportIntuneBrandingProfileAsync(profile, _tempDir, table);

        var expectedPath = Path.Combine(_tempDir, "IntuneBrandingProfiles", "Branding Profile One.json");
        Assert.True(File.Exists(expectedPath));
        Assert.Contains(table.Entries, e => e.ObjectType == "IntuneBrandingProfile" && e.OriginalId == "branding-id");
    }

    [Fact]
    public async Task ExportAzureBrandingLocalization_CreatesJsonFile()
    {
        var localization = new OrganizationalBrandingLocalization
        {
            Id = "en-US"
        };
        var table = new MigrationTable();

        await _service.ExportAzureBrandingLocalizationAsync(localization, _tempDir, table);

        var expectedPath = Path.Combine(_tempDir, "AzureBrandingLocalizations", "en-US.json");
        Assert.True(File.Exists(expectedPath));
        Assert.Contains(table.Entries, e => e.ObjectType == "AzureBrandingLocalization" && e.OriginalId == "en-US");
    }

    [Fact]
    public async Task ExportEndpointSecurityIntents_ExportsMultipleAndWritesMigrationTable()
    {
        var intents = new (DeviceManagementIntent Intent, IReadOnlyList<DeviceManagementIntentAssignment> Assignments)[]
        {
            (new DeviceManagementIntent { Id = "intent-1", DisplayName = "Endpoint One" }, Array.Empty<DeviceManagementIntentAssignment>()),
            (new DeviceManagementIntent { Id = "intent-2", DisplayName = "Endpoint Two" }, Array.Empty<DeviceManagementIntentAssignment>())
        };

        await _service.ExportEndpointSecurityIntentsAsync(intents, _tempDir);

        var folder = Path.Combine(_tempDir, "EndpointSecurity");
        Assert.Equal(2, Directory.GetFiles(folder, "*.json").Length);
        Assert.True(File.Exists(Path.Combine(_tempDir, "migration-table.json")));
    }

    [Fact]
    public async Task ExportAdministrativeTemplates_ExportsMultipleAndWritesMigrationTable()
    {
        var templates = new (GroupPolicyConfiguration Template, IReadOnlyList<GroupPolicyConfigurationAssignment> Assignments)[]
        {
            (new GroupPolicyConfiguration { Id = "template-1", DisplayName = "Template One" }, Array.Empty<GroupPolicyConfigurationAssignment>()),
            (new GroupPolicyConfiguration { Id = "template-2", DisplayName = "Template Two" }, Array.Empty<GroupPolicyConfigurationAssignment>())
        };

        await _service.ExportAdministrativeTemplatesAsync(templates, _tempDir);

        var folder = Path.Combine(_tempDir, "AdministrativeTemplates");
        Assert.Equal(2, Directory.GetFiles(folder, "*.json").Length);
        Assert.True(File.Exists(Path.Combine(_tempDir, "migration-table.json")));
    }

    [Fact]
    public async Task ExportEnrollmentConfigurations_ExportsMultipleAndWritesMigrationTable()
    {
        var configurations = new[]
        {
            new DeviceEnrollmentConfiguration { Id = "enroll-1", DisplayName = "Enrollment One" },
            new DeviceEnrollmentConfiguration { Id = "enroll-2", DisplayName = "Enrollment Two" }
        };

        await _service.ExportEnrollmentConfigurationsAsync(configurations, _tempDir);

        var folder = Path.Combine(_tempDir, "EnrollmentConfigurations");
        Assert.Equal(2, Directory.GetFiles(folder, "*.json").Length);
        Assert.True(File.Exists(Path.Combine(_tempDir, "migration-table.json")));
    }

    [Fact]
    public async Task ExportAppProtectionPolicies_ExportsMultipleAndWritesMigrationTable()
    {
        var policies = new ManagedAppPolicy[]
        {
            new AndroidManagedAppProtection { Id = "app-protect-1", DisplayName = "Policy One" },
            new AndroidManagedAppProtection { Id = "app-protect-2", DisplayName = "Policy Two" }
        };

        await _service.ExportAppProtectionPoliciesAsync(policies, _tempDir);

        var folder = Path.Combine(_tempDir, "AppProtectionPolicies");
        Assert.Equal(2, Directory.GetFiles(folder, "*.json").Length);
        Assert.True(File.Exists(Path.Combine(_tempDir, "migration-table.json")));
    }

    [Fact]
    public async Task ExportManagedDeviceAppConfigurations_ExportsMultipleAndWritesMigrationTable()
    {
        var configurations = new[]
        {
            new ManagedDeviceMobileAppConfiguration { Id = "mdac-1", DisplayName = "Managed One" },
            new ManagedDeviceMobileAppConfiguration { Id = "mdac-2", DisplayName = "Managed Two" }
        };

        await _service.ExportManagedDeviceAppConfigurationsAsync(configurations, _tempDir);

        var folder = Path.Combine(_tempDir, "ManagedDeviceAppConfigurations");
        Assert.Equal(2, Directory.GetFiles(folder, "*.json").Length);
        Assert.True(File.Exists(Path.Combine(_tempDir, "migration-table.json")));
    }

    [Fact]
    public async Task ExportTargetedManagedAppConfigurations_ExportsMultipleAndWritesMigrationTable()
    {
        var configurations = new[]
        {
            new TargetedManagedAppConfiguration { Id = "tmac-1", DisplayName = "Targeted One" },
            new TargetedManagedAppConfiguration { Id = "tmac-2", DisplayName = "Targeted Two" }
        };

        await _service.ExportTargetedManagedAppConfigurationsAsync(configurations, _tempDir);

        var folder = Path.Combine(_tempDir, "TargetedManagedAppConfigurations");
        Assert.Equal(2, Directory.GetFiles(folder, "*.json").Length);
        Assert.True(File.Exists(Path.Combine(_tempDir, "migration-table.json")));
    }

    [Fact]
    public async Task ExportTermsAndConditionsCollection_ExportsMultipleAndWritesMigrationTable()
    {
        var termsCollection = new[]
        {
            new TermsAndConditions { Id = "terms-1", DisplayName = "Terms One" },
            new TermsAndConditions { Id = "terms-2", DisplayName = "Terms Two" }
        };

        await _service.ExportTermsAndConditionsCollectionAsync(termsCollection, _tempDir);

        var folder = Path.Combine(_tempDir, "TermsAndConditions");
        Assert.Equal(2, Directory.GetFiles(folder, "*.json").Length);
        Assert.True(File.Exists(Path.Combine(_tempDir, "migration-table.json")));
    }

    [Fact]
    public async Task ExportScopeTags_ExportsMultipleAndWritesMigrationTable()
    {
        var scopeTags = new[]
        {
            new RoleScopeTag { Id = "scope-1", DisplayName = "Scope One" },
            new RoleScopeTag { Id = "scope-2", DisplayName = "Scope Two" }
        };

        await _service.ExportScopeTagsAsync(scopeTags, _tempDir);

        var folder = Path.Combine(_tempDir, "ScopeTags");
        Assert.Equal(2, Directory.GetFiles(folder, "*.json").Length);
        Assert.True(File.Exists(Path.Combine(_tempDir, "migration-table.json")));
    }

    [Fact]
    public async Task ExportRoleDefinitions_ExportsMultipleAndWritesMigrationTable()
    {
        var roleDefinitions = new[]
        {
            new RoleDefinition { Id = "role-1", DisplayName = "Role One" },
            new RoleDefinition { Id = "role-2", DisplayName = "Role Two" }
        };

        await _service.ExportRoleDefinitionsAsync(roleDefinitions, _tempDir);

        var folder = Path.Combine(_tempDir, "RoleDefinitions");
        Assert.Equal(2, Directory.GetFiles(folder, "*.json").Length);
        Assert.True(File.Exists(Path.Combine(_tempDir, "migration-table.json")));
    }

    [Fact]
    public async Task ExportIntuneBrandingProfiles_ExportsMultipleAndWritesMigrationTable()
    {
        var profiles = new[]
        {
            new IntuneBrandingProfile { Id = "branding-1", ProfileName = "Branding One" },
            new IntuneBrandingProfile { Id = "branding-2", ProfileName = "Branding Two" }
        };

        await _service.ExportIntuneBrandingProfilesAsync(profiles, _tempDir);

        var folder = Path.Combine(_tempDir, "IntuneBrandingProfiles");
        Assert.Equal(2, Directory.GetFiles(folder, "*.json").Length);
        Assert.True(File.Exists(Path.Combine(_tempDir, "migration-table.json")));
    }

    [Fact]
    public async Task ExportAzureBrandingLocalizations_ExportsMultipleAndWritesMigrationTable()
    {
        var localizations = new[]
        {
            new OrganizationalBrandingLocalization { Id = "en-US" },
            new OrganizationalBrandingLocalization { Id = "fr-FR" }
        };

        await _service.ExportAzureBrandingLocalizationsAsync(localizations, _tempDir);

        var folder = Path.Combine(_tempDir, "AzureBrandingLocalizations");
        Assert.Equal(2, Directory.GetFiles(folder, "*.json").Length);
        Assert.True(File.Exists(Path.Combine(_tempDir, "migration-table.json")));
    }

    [Fact]
    public async Task ExportAutopilotProfile_CreatesJsonFile()
    {
        var profile = new WindowsAutopilotDeploymentProfile { Id = "autopilot-id", DisplayName = "Autopilot One" };
        var table = new MigrationTable();

        await _service.ExportAutopilotProfileAsync(profile, _tempDir, table);

        Assert.True(File.Exists(Path.Combine(_tempDir, "AutopilotProfiles", "Autopilot One.json")));
        Assert.Contains(table.Entries, e => e.ObjectType == "AutopilotProfile" && e.OriginalId == "autopilot-id");
    }

    [Fact]
    public async Task ExportDeviceHealthScript_CreatesJsonFile()
    {
        var script = new DeviceHealthScript { Id = "dhs-id", DisplayName = "Health Script" };
        var table = new MigrationTable();

        await _service.ExportDeviceHealthScriptAsync(script, _tempDir, table);

        Assert.True(File.Exists(Path.Combine(_tempDir, "DeviceHealthScripts", "Health Script.json")));
        Assert.Contains(table.Entries, e => e.ObjectType == "DeviceHealthScript" && e.OriginalId == "dhs-id");
    }

    [Fact]
    public async Task ExportMacCustomAttribute_CreatesJsonFile()
    {
        var script = new DeviceCustomAttributeShellScript { Id = "mac-id", DisplayName = "Mac Attr" };
        var table = new MigrationTable();

        await _service.ExportMacCustomAttributeAsync(script, _tempDir, table);

        Assert.True(File.Exists(Path.Combine(_tempDir, "MacCustomAttributes", "Mac Attr.json")));
        Assert.Contains(table.Entries, e => e.ObjectType == "MacCustomAttribute" && e.OriginalId == "mac-id");
    }

    [Fact]
    public async Task ExportFeatureUpdateProfile_CreatesJsonFile()
    {
        var profile = new WindowsFeatureUpdateProfile { Id = "fup-id", DisplayName = "Feature Update" };
        var table = new MigrationTable();

        await _service.ExportFeatureUpdateProfileAsync(profile, _tempDir, table);

        Assert.True(File.Exists(Path.Combine(_tempDir, "FeatureUpdates", "Feature Update.json")));
        Assert.Contains(table.Entries, e => e.ObjectType == "FeatureUpdateProfile" && e.OriginalId == "fup-id");
    }

    [Fact]
    public async Task ExportNamedLocation_CreatesJsonFile()
    {
        var namedLocation = new NamedLocation
        {
            Id = "named-loc-id",
            AdditionalData = new Dictionary<string, object> { ["displayName"] = "HQ" }
        };
        var table = new MigrationTable();

        await _service.ExportNamedLocationAsync(namedLocation, _tempDir, table);

        Assert.True(File.Exists(Path.Combine(_tempDir, "NamedLocations", "HQ.json")));
        Assert.Contains(table.Entries, e => e.ObjectType == "NamedLocation" && e.OriginalId == "named-loc-id");
    }

    [Fact]
    public async Task ExportAuthenticationStrengthPolicy_CreatesJsonFile()
    {
        var policy = new AuthenticationStrengthPolicy { Id = "asp-id", DisplayName = "Strong MFA" };
        var table = new MigrationTable();

        await _service.ExportAuthenticationStrengthPolicyAsync(policy, _tempDir, table);

        Assert.True(File.Exists(Path.Combine(_tempDir, "AuthenticationStrengths", "Strong MFA.json")));
        Assert.Contains(table.Entries, e => e.ObjectType == "AuthenticationStrengthPolicy" && e.OriginalId == "asp-id");
    }

    [Fact]
    public async Task ExportAuthenticationContext_CreatesJsonFile()
    {
        var context = new AuthenticationContextClassReference { Id = "ctx-id", DisplayName = "Context One" };
        var table = new MigrationTable();

        await _service.ExportAuthenticationContextAsync(context, _tempDir, table);

        Assert.True(File.Exists(Path.Combine(_tempDir, "AuthenticationContexts", "Context One.json")));
        Assert.Contains(table.Entries, e => e.ObjectType == "AuthenticationContext" && e.OriginalId == "ctx-id");
    }

    [Fact]
    public async Task ExportTermsOfUseAgreement_CreatesJsonFile()
    {
        var agreement = new Agreement { Id = "tou-id", DisplayName = "Employee Terms" };
        var table = new MigrationTable();

        await _service.ExportTermsOfUseAgreementAsync(agreement, _tempDir, table);

        Assert.True(File.Exists(Path.Combine(_tempDir, "TermsOfUse", "Employee Terms.json")));
        Assert.Contains(table.Entries, e => e.ObjectType == "TermsOfUseAgreement" && e.OriginalId == "tou-id");
    }

    [Fact]
    public async Task ExportAutopilotProfiles_ExportsMultipleAndWritesMigrationTable()
    {
        var profiles = new[]
        {
            new WindowsAutopilotDeploymentProfile { Id = "ap-1", DisplayName = "Autopilot One" },
            new WindowsAutopilotDeploymentProfile { Id = "ap-2", DisplayName = "Autopilot Two" }
        };

        await _service.ExportAutopilotProfilesAsync(profiles, _tempDir);

        var folder = Path.Combine(_tempDir, "AutopilotProfiles");
        Assert.Equal(2, Directory.GetFiles(folder, "*.json").Length);
        Assert.True(File.Exists(Path.Combine(_tempDir, "migration-table.json")));
    }

    [Fact]
    public async Task ExportDeviceHealthScripts_ExportsMultipleAndWritesMigrationTable()
    {
        var scripts = new[]
        {
            new DeviceHealthScript { Id = "dhs-1", DisplayName = "Script One" },
            new DeviceHealthScript { Id = "dhs-2", DisplayName = "Script Two" }
        };

        await _service.ExportDeviceHealthScriptsAsync(scripts, _tempDir);

        var folder = Path.Combine(_tempDir, "DeviceHealthScripts");
        Assert.Equal(2, Directory.GetFiles(folder, "*.json").Length);
        Assert.True(File.Exists(Path.Combine(_tempDir, "migration-table.json")));
    }

    [Fact]
    public async Task ExportMacCustomAttributes_ExportsMultipleAndWritesMigrationTable()
    {
        var scripts = new[]
        {
            new DeviceCustomAttributeShellScript { Id = "mac-1", DisplayName = "Attr One" },
            new DeviceCustomAttributeShellScript { Id = "mac-2", DisplayName = "Attr Two" }
        };

        await _service.ExportMacCustomAttributesAsync(scripts, _tempDir);

        var folder = Path.Combine(_tempDir, "MacCustomAttributes");
        Assert.Equal(2, Directory.GetFiles(folder, "*.json").Length);
        Assert.True(File.Exists(Path.Combine(_tempDir, "migration-table.json")));
    }

    [Fact]
    public async Task ExportFeatureUpdateProfiles_ExportsMultipleAndWritesMigrationTable()
    {
        var profiles = new[]
        {
            new WindowsFeatureUpdateProfile { Id = "fup-1", DisplayName = "Feature One" },
            new WindowsFeatureUpdateProfile { Id = "fup-2", DisplayName = "Feature Two" }
        };

        await _service.ExportFeatureUpdateProfilesAsync(profiles, _tempDir);

        var folder = Path.Combine(_tempDir, "FeatureUpdates");
        Assert.Equal(2, Directory.GetFiles(folder, "*.json").Length);
        Assert.True(File.Exists(Path.Combine(_tempDir, "migration-table.json")));
    }

    [Fact]
    public async Task ExportNamedLocations_ExportsMultipleAndWritesMigrationTable()
    {
        var namedLocations = new[]
        {
            new NamedLocation { Id = "nl-1", AdditionalData = new Dictionary<string, object> { ["displayName"] = "Location One" } },
            new NamedLocation { Id = "nl-2", AdditionalData = new Dictionary<string, object> { ["displayName"] = "Location Two" } }
        };

        await _service.ExportNamedLocationsAsync(namedLocations, _tempDir);

        var folder = Path.Combine(_tempDir, "NamedLocations");
        Assert.Equal(2, Directory.GetFiles(folder, "*.json").Length);
        Assert.True(File.Exists(Path.Combine(_tempDir, "migration-table.json")));
    }

    [Fact]
    public async Task ExportAuthenticationStrengthPolicies_ExportsMultipleAndWritesMigrationTable()
    {
        var policies = new[]
        {
            new AuthenticationStrengthPolicy { Id = "asp-1", DisplayName = "Strength One" },
            new AuthenticationStrengthPolicy { Id = "asp-2", DisplayName = "Strength Two" }
        };

        await _service.ExportAuthenticationStrengthPoliciesAsync(policies, _tempDir);

        var folder = Path.Combine(_tempDir, "AuthenticationStrengths");
        Assert.Equal(2, Directory.GetFiles(folder, "*.json").Length);
        Assert.True(File.Exists(Path.Combine(_tempDir, "migration-table.json")));
    }

    [Fact]
    public async Task ExportAuthenticationContexts_ExportsMultipleAndWritesMigrationTable()
    {
        var contexts = new[]
        {
            new AuthenticationContextClassReference { Id = "ctx-1", DisplayName = "Context One" },
            new AuthenticationContextClassReference { Id = "ctx-2", DisplayName = "Context Two" }
        };

        await _service.ExportAuthenticationContextsAsync(contexts, _tempDir);

        var folder = Path.Combine(_tempDir, "AuthenticationContexts");
        Assert.Equal(2, Directory.GetFiles(folder, "*.json").Length);
        Assert.True(File.Exists(Path.Combine(_tempDir, "migration-table.json")));
    }

    [Fact]
    public async Task ExportTermsOfUseAgreements_ExportsMultipleAndWritesMigrationTable()
    {
        var agreements = new[]
        {
            new Agreement { Id = "tou-1", DisplayName = "Terms One" },
            new Agreement { Id = "tou-2", DisplayName = "Terms Two" }
        };

        await _service.ExportTermsOfUseAgreementsAsync(agreements, _tempDir);

        var folder = Path.Combine(_tempDir, "TermsOfUse");
        Assert.Equal(2, Directory.GetFiles(folder, "*.json").Length);
        Assert.True(File.Exists(Path.Combine(_tempDir, "migration-table.json")));
    }

    [Fact]
    public async Task ExportDeviceConfiguration_NullDisplayName_FallsBackToId()
    {
        var config = new DeviceConfiguration { Id = "fallback-id", DisplayName = null };
        var table = new MigrationTable();

        await _service.ExportDeviceConfigurationAsync(config, _tempDir, table);

        var folder = Path.Combine(_tempDir, "DeviceConfigurations");
        var files = Directory.GetFiles(folder, "*.json");
        Assert.Single(files);
        Assert.Contains("fallback-id", files[0]);
    }

    [Fact]
    public async Task ExportDeviceConfiguration_NullId_SkipsMigrationTableEntry()
    {
        var config = new DeviceConfiguration { Id = null, DisplayName = "No Id Config" };
        var table = new MigrationTable();

        await _service.ExportDeviceConfigurationAsync(config, _tempDir, table);

        Assert.Empty(table.Entries);
    }

    [Fact]
    public async Task ExportDeviceManagementScript_CreatesJsonFile()
    {
        var script = new DeviceManagementScript { Id = "dms-id", DisplayName = "PS Script" };
        var table = new MigrationTable();

        await _service.ExportDeviceManagementScriptAsync(script, _tempDir, table);

        Assert.True(File.Exists(Path.Combine(_tempDir, "DeviceManagementScripts", "PS Script.json")));
        Assert.Contains(table.Entries, e => e.ObjectType == "DeviceManagementScript" && e.OriginalId == "dms-id");
    }

    [Fact]
    public async Task ExportDeviceManagementScript_NullDisplayName_FallsBackToId()
    {
        var script = new DeviceManagementScript { Id = "fallback-dms-id", DisplayName = null };
        var table = new MigrationTable();

        await _service.ExportDeviceManagementScriptAsync(script, _tempDir, table);

        var folder = Path.Combine(_tempDir, "DeviceManagementScripts");
        var files = Directory.GetFiles(folder, "*.json");
        Assert.Single(files);
        Assert.Contains("fallback-dms-id", files[0]);
    }

    [Fact]
    public async Task ExportDeviceManagementScript_NullId_SkipsMigrationTableEntry()
    {
        var script = new DeviceManagementScript { Id = null, DisplayName = "No Id Script" };
        var table = new MigrationTable();

        await _service.ExportDeviceManagementScriptAsync(script, _tempDir, table);

        Assert.Empty(table.Entries);
    }

    [Fact]
    public async Task ExportDeviceManagementScripts_ExportsMultipleAndWritesMigrationTable()
    {
        var scripts = new List<DeviceManagementScript>
        {
            new() { Id = "dms-1", DisplayName = "Script One" },
            new() { Id = "dms-2", DisplayName = "Script Two" }
        };

        await _service.ExportDeviceManagementScriptsAsync(scripts, _tempDir);

        var folder = Path.Combine(_tempDir, "DeviceManagementScripts");
        Assert.True(File.Exists(Path.Combine(folder, "Script One.json")));
        Assert.True(File.Exists(Path.Combine(folder, "Script Two.json")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "migration-table.json")));
    }

    [Fact]
    public async Task ExportDeviceShellScript_CreatesJsonFile()
    {
        var script = new DeviceShellScript { Id = "dss-id", DisplayName = "Shell Script" };
        var table = new MigrationTable();

        await _service.ExportDeviceShellScriptAsync(script, _tempDir, table);

        Assert.True(File.Exists(Path.Combine(_tempDir, "DeviceShellScripts", "Shell Script.json")));
        Assert.Contains(table.Entries, e => e.ObjectType == "DeviceShellScript" && e.OriginalId == "dss-id");
    }

    [Fact]
    public async Task ExportDeviceShellScript_NullDisplayName_FallsBackToId()
    {
        var script = new DeviceShellScript { Id = "fallback-dss-id", DisplayName = null };
        var table = new MigrationTable();

        await _service.ExportDeviceShellScriptAsync(script, _tempDir, table);

        var folder = Path.Combine(_tempDir, "DeviceShellScripts");
        var files = Directory.GetFiles(folder, "*.json");
        Assert.Single(files);
        Assert.Contains("fallback-dss-id", files[0]);
    }

    [Fact]
    public async Task ExportDeviceShellScript_NullId_SkipsMigrationTableEntry()
    {
        var script = new DeviceShellScript { Id = null, DisplayName = "No Id Shell Script" };
        var table = new MigrationTable();

        await _service.ExportDeviceShellScriptAsync(script, _tempDir, table);

        Assert.Empty(table.Entries);
    }

    [Fact]
    public async Task ExportDeviceShellScripts_ExportsMultipleAndWritesMigrationTable()
    {
        var scripts = new List<DeviceShellScript>
        {
            new() { Id = "dss-1", DisplayName = "Shell One" },
            new() { Id = "dss-2", DisplayName = "Shell Two" }
        };

        await _service.ExportDeviceShellScriptsAsync(scripts, _tempDir);

        var folder = Path.Combine(_tempDir, "DeviceShellScripts");
        Assert.True(File.Exists(Path.Combine(folder, "Shell One.json")));
        Assert.True(File.Exists(Path.Combine(folder, "Shell Two.json")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "migration-table.json")));
    }

    [Fact]
    public async Task ExportComplianceScript_CreatesJsonFile()
    {
        var script = new DeviceComplianceScript { Id = "cs-id", DisplayName = "Compliance Script" };
        var table = new MigrationTable();

        await _service.ExportComplianceScriptAsync(script, _tempDir, table);

        Assert.True(File.Exists(Path.Combine(_tempDir, "ComplianceScripts", "Compliance Script.json")));
        Assert.Contains(table.Entries, e => e.ObjectType == "ComplianceScript" && e.OriginalId == "cs-id");
    }

    [Fact]
    public async Task ExportComplianceScripts_ExportsMultipleAndWritesMigrationTable()
    {
        var scripts = new List<DeviceComplianceScript>
        {
            new() { Id = "cs-1", DisplayName = "Script A" },
            new() { Id = "cs-2", DisplayName = "Script B" }
        };

        await _service.ExportComplianceScriptsAsync(scripts, _tempDir);

        var folder = Path.Combine(_tempDir, "ComplianceScripts");
        Assert.True(File.Exists(Path.Combine(folder, "Script A.json")));
        Assert.True(File.Exists(Path.Combine(folder, "Script B.json")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "migration-table.json")));
    }
}
