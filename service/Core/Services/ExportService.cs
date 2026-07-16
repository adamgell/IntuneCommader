using System.Text.Json;
using Intune.Commander.Core.Models;
using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public class ExportService : IExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Dictionary<string, FolderExportState> _folderStates = new(StringComparer.OrdinalIgnoreCase);

    private sealed class FolderExportState
    {
        public FolderExportState(string folderPath)
        {
            ReservedFileNames = Directory.Exists(folderPath)
                ? new HashSet<string>(
                    Directory.EnumerateFiles(folderPath, "*.json", SearchOption.TopDirectoryOnly)
                        .Select(Path.GetFileName)
                        .Where(static fileName => !string.IsNullOrEmpty(fileName))!,
                    StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public HashSet<string> ReservedFileNames { get; }
        public bool DirectoryEnsured { get; set; }
    }

    public async Task ExportDeviceConfigurationAsync(
        DeviceConfiguration config,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "DeviceConfigurations");

        var filePath = GetUniqueFilePath(folderPath, config.DisplayName ?? config.Id ?? "unknown", config.Id);

        // Serialize using System.Text.Json with the Graph model
        await WriteJsonFileAsync(filePath, config, config.GetType(), cancellationToken);

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
        EnsureFolder(outputPath);
        var filePath = Path.Combine(outputPath, "migration-table.json");

        try
        {
            await WriteJsonFileAsync(filePath, table, cancellationToken);
        }
        finally
        {
            // ExportService can live for an entire desktop session, so clear any
            // cached file reservations after each completed export batch.
            _folderStates.Clear();
        }
    }

    // Use a fixed set of Windows-invalid filename chars so sanitization is
    // consistent regardless of the OS where tests or exports are run.
    private static readonly char[] _invalidFileNameChars =
        ['/', '\\', ':', '*', '?', '"', '<', '>', '|', '\0'];

    private static string SanitizeFileName(string name)
    {
        var sanitized = string.Join("_", name.Split(_invalidFileNameChars, StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrEmpty(sanitized) ? "unnamed" : sanitized;
    }

    private void EnsureFolder(string folderPath)
    {
        var state = GetOrCreateFolderState(folderPath);
        if (state.DirectoryEnsured)
            return;

        Directory.CreateDirectory(folderPath);
        state.DirectoryEnsured = true;
    }

    /// <summary>
    /// Returns a unique .json file path under <paramref name="folderPath"/>.
    /// If the sanitized name already exists as a file (collision), appends the
    /// object's <paramref name="id"/> to disambiguate rather than overwriting.
    /// When both name and id collide (or id is null), falls back to a numeric suffix.
    /// </summary>
    private string GetUniqueFilePath(string folderPath, string name, string? id)
    {
        var state = GetOrCreateFolderState(folderPath);
        if (!state.DirectoryEnsured)
        {
            Directory.CreateDirectory(folderPath);
            state.DirectoryEnsured = true;
        }

        var sanitized = SanitizeFileName(name);
        var sanitizedId = string.IsNullOrEmpty(id) ? null : SanitizeFileName(id);
        var fileName = $"{sanitized}.json";
        if (state.ReservedFileNames.Add(fileName))
            return Path.Combine(folderPath, fileName);

        if (!string.IsNullOrEmpty(sanitizedId))
        {
            fileName = $"{sanitized}_{sanitizedId}.json";
            if (state.ReservedFileNames.Add(fileName))
                return Path.Combine(folderPath, fileName);
        }

        for (var i = 1; ; i++)
        {
            fileName = $"{sanitized}_{i}.json";
            if (state.ReservedFileNames.Add(fileName))
                return Path.Combine(folderPath, fileName);
        }
    }

    private FolderExportState GetOrCreateFolderState(string folderPath)
    {
        if (_folderStates.TryGetValue(folderPath, out var state))
            return state;

        state = new FolderExportState(folderPath);
        _folderStates[folderPath] = state;
        return state;
    }

    private static async Task WriteJsonFileAsync<T>(
        string filePath,
        T value,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    private static async Task WriteJsonFileAsync(
        string filePath,
        object value,
        Type runtimeType,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(value, runtimeType, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    public async Task ExportCompliancePolicyAsync(
        DeviceCompliancePolicy policy,
        IReadOnlyList<DeviceCompliancePolicyAssignment> assignments,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "CompliancePolicies");

        var filePath = GetUniqueFilePath(folderPath, policy.DisplayName ?? policy.Id ?? "unknown", policy.Id);

        var export = new CompliancePolicyExport
        {
            Policy = policy,
            Assignments = assignments.ToList()
        };

        await WriteJsonFileAsync(filePath, export, cancellationToken);

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

        var filePath = GetUniqueFilePath(folderPath, app.DisplayName ?? app.Id ?? "unknown", app.Id);

        var export = new ApplicationExport
        {
            Application = app,
            Assignments = assignments.ToList()
        };

        await WriteJsonFileAsync(filePath, export, cancellationToken);

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

    public async Task ExportEndpointSecurityIntentAsync(
        DeviceManagementIntent intent,
        IReadOnlyList<DeviceManagementIntentAssignment> assignments,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "EndpointSecurity");

        var filePath = GetUniqueFilePath(folderPath, intent.DisplayName ?? intent.Id ?? "unknown", intent.Id);

        var export = new EndpointSecurityExport
        {
            Intent = intent,
            Assignments = assignments.ToList()
        };

        await WriteJsonFileAsync(filePath, export, cancellationToken);

        if (intent.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "EndpointSecurityIntent",
                OriginalId = intent.Id,
                Name = intent.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportEndpointSecurityIntentsAsync(
        IEnumerable<(DeviceManagementIntent Intent, IReadOnlyList<DeviceManagementIntentAssignment> Assignments)> intents,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var (intent, assignments) in intents)
        {
            await ExportEndpointSecurityIntentAsync(intent, assignments, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportAdministrativeTemplateAsync(
        GroupPolicyConfiguration template,
        IReadOnlyList<GroupPolicyConfigurationAssignment> assignments,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "AdministrativeTemplates");

        var filePath = GetUniqueFilePath(folderPath, template.DisplayName ?? template.Id ?? "unknown", template.Id);

        var export = new AdministrativeTemplateExport
        {
            Template = template,
            Assignments = assignments.ToList()
        };

        await WriteJsonFileAsync(filePath, export, cancellationToken);

        if (template.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "AdministrativeTemplate",
                OriginalId = template.Id,
                Name = template.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportAdministrativeTemplatesAsync(
        IEnumerable<(GroupPolicyConfiguration Template, IReadOnlyList<GroupPolicyConfigurationAssignment> Assignments)> templates,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var (template, assignments) in templates)
        {
            await ExportAdministrativeTemplateAsync(template, assignments, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportEnrollmentConfigurationAsync(
        DeviceEnrollmentConfiguration configuration,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "EnrollmentConfigurations");

        var filePath = GetUniqueFilePath(folderPath, configuration.DisplayName ?? configuration.Id ?? "unknown", configuration.Id);

        await WriteJsonFileAsync(filePath, configuration, configuration.GetType(), cancellationToken);

        if (configuration.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "EnrollmentConfiguration",
                OriginalId = configuration.Id,
                Name = configuration.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportEnrollmentConfigurationsAsync(
        IEnumerable<DeviceEnrollmentConfiguration> configurations,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var configuration in configurations)
        {
            await ExportEnrollmentConfigurationAsync(configuration, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportAppProtectionPolicyAsync(
        ManagedAppPolicy policy,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "AppProtectionPolicies");

        var filePath = GetUniqueFilePath(folderPath, policy.DisplayName ?? policy.Id ?? "unknown", policy.Id);

        await WriteJsonFileAsync(filePath, policy, policy.GetType(), cancellationToken);

        if (policy.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "AppProtectionPolicy",
                OriginalId = policy.Id,
                Name = policy.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportAppProtectionPoliciesAsync(
        IEnumerable<ManagedAppPolicy> policies,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var policy in policies)
        {
            await ExportAppProtectionPolicyAsync(policy, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportManagedDeviceAppConfigurationAsync(
        ManagedDeviceMobileAppConfiguration configuration,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "ManagedDeviceAppConfigurations");

        var filePath = GetUniqueFilePath(folderPath, configuration.DisplayName ?? configuration.Id ?? "unknown", configuration.Id);

        await WriteJsonFileAsync(filePath, configuration, configuration.GetType(), cancellationToken);

        if (configuration.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "ManagedDeviceAppConfiguration",
                OriginalId = configuration.Id,
                Name = configuration.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportManagedDeviceAppConfigurationsAsync(
        IEnumerable<ManagedDeviceMobileAppConfiguration> configurations,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var configuration in configurations)
        {
            await ExportManagedDeviceAppConfigurationAsync(configuration, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportTargetedManagedAppConfigurationAsync(
        TargetedManagedAppConfiguration configuration,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "TargetedManagedAppConfigurations");

        var filePath = GetUniqueFilePath(folderPath, configuration.DisplayName ?? configuration.Id ?? "unknown", configuration.Id);

        await WriteJsonFileAsync(filePath, configuration, configuration.GetType(), cancellationToken);

        if (configuration.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "TargetedManagedAppConfiguration",
                OriginalId = configuration.Id,
                Name = configuration.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportTargetedManagedAppConfigurationsAsync(
        IEnumerable<TargetedManagedAppConfiguration> configurations,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var configuration in configurations)
        {
            await ExportTargetedManagedAppConfigurationAsync(configuration, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportTermsAndConditionsAsync(
        TermsAndConditions termsAndConditions,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "TermsAndConditions");

        var filePath = GetUniqueFilePath(folderPath, termsAndConditions.DisplayName ?? termsAndConditions.Id ?? "unknown", termsAndConditions.Id);

        await WriteJsonFileAsync(filePath, termsAndConditions, termsAndConditions.GetType(), cancellationToken);

        if (termsAndConditions.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "TermsAndConditions",
                OriginalId = termsAndConditions.Id,
                Name = termsAndConditions.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportTermsAndConditionsCollectionAsync(
        IEnumerable<TermsAndConditions> termsCollection,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var termsAndConditions in termsCollection)
        {
            await ExportTermsAndConditionsAsync(termsAndConditions, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportScopeTagAsync(
        RoleScopeTag scopeTag,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "ScopeTags");

        var filePath = GetUniqueFilePath(folderPath, scopeTag.DisplayName ?? scopeTag.Id ?? "unknown", scopeTag.Id);

        await WriteJsonFileAsync(filePath, scopeTag, scopeTag.GetType(), cancellationToken);

        if (scopeTag.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "ScopeTag",
                OriginalId = scopeTag.Id,
                Name = scopeTag.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportScopeTagsAsync(
        IEnumerable<RoleScopeTag> scopeTags,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var scopeTag in scopeTags)
        {
            await ExportScopeTagAsync(scopeTag, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportRoleDefinitionAsync(
        RoleDefinition roleDefinition,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "RoleDefinitions");

        var filePath = GetUniqueFilePath(folderPath, roleDefinition.DisplayName ?? roleDefinition.Id ?? "unknown", roleDefinition.Id);

        await WriteJsonFileAsync(filePath, roleDefinition, roleDefinition.GetType(), cancellationToken);

        if (roleDefinition.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "RoleDefinition",
                OriginalId = roleDefinition.Id,
                Name = roleDefinition.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportRoleDefinitionsAsync(
        IEnumerable<RoleDefinition> roleDefinitions,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var roleDefinition in roleDefinitions)
        {
            await ExportRoleDefinitionAsync(roleDefinition, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportSettingsCatalogPolicyAsync(
        DeviceManagementConfigurationPolicy policy,
        IReadOnlyList<DeviceManagementConfigurationSetting> settings,
        IReadOnlyList<DeviceManagementConfigurationPolicyAssignment> assignments,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "SettingsCatalog");

        var filePath = GetUniqueFilePath(folderPath, policy.Name ?? policy.Id ?? "unknown", policy.Id);

        var export = new SettingsCatalogExport
        {
            Policy = policy,
            Settings = settings.ToList(),
            Assignments = assignments.ToList()
        };

        await WriteJsonFileAsync(filePath, export, cancellationToken);

        if (policy.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "SettingsCatalog",
                OriginalId = policy.Id,
                Name = policy.Name ?? "Unknown"
            });
        }
    }

    public async Task ExportSettingsCatalogPoliciesAsync(
        IEnumerable<(DeviceManagementConfigurationPolicy Policy, IReadOnlyList<DeviceManagementConfigurationSetting> Settings, IReadOnlyList<DeviceManagementConfigurationPolicyAssignment> Assignments)> policies,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var (policy, settings, assignments) in policies)
        {
            await ExportSettingsCatalogPolicyAsync(policy, settings, assignments, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportIntuneBrandingProfileAsync(
        IntuneBrandingProfile profile,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "IntuneBrandingProfiles");

        var filePath = GetUniqueFilePath(folderPath, profile.ProfileName ?? profile.Id ?? "unknown", profile.Id);

        await WriteJsonFileAsync(filePath, profile, profile.GetType(), cancellationToken);

        if (profile.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "IntuneBrandingProfile",
                OriginalId = profile.Id,
                Name = profile.ProfileName ?? "Unknown"
            });
        }
    }

    public async Task ExportIntuneBrandingProfilesAsync(
        IEnumerable<IntuneBrandingProfile> profiles,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var profile in profiles)
        {
            await ExportIntuneBrandingProfileAsync(profile, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportAzureBrandingLocalizationAsync(
        OrganizationalBrandingLocalization localization,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "AzureBrandingLocalizations");

        var filePath = GetUniqueFilePath(folderPath, localization.Id ?? "unknown", null);

        await WriteJsonFileAsync(filePath, localization, localization.GetType(), cancellationToken);

        if (localization.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "AzureBrandingLocalization",
                OriginalId = localization.Id,
                Name = localization.Id ?? "Unknown"
            });
        }
    }

    public async Task ExportAzureBrandingLocalizationsAsync(
        IEnumerable<OrganizationalBrandingLocalization> localizations,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var localization in localizations)
        {
            await ExportAzureBrandingLocalizationAsync(localization, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportAutopilotProfileAsync(
        WindowsAutopilotDeploymentProfile profile,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "AutopilotProfiles");

        var filePath = GetUniqueFilePath(folderPath, profile.DisplayName ?? profile.Id ?? "unknown", profile.Id);

        await WriteJsonFileAsync(filePath, profile, profile.GetType(), cancellationToken);

        if (profile.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "AutopilotProfile",
                OriginalId = profile.Id,
                Name = profile.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportAutopilotProfilesAsync(
        IEnumerable<WindowsAutopilotDeploymentProfile> profiles,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var profile in profiles)
        {
            await ExportAutopilotProfileAsync(profile, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportDeviceHealthScriptAsync(
        DeviceHealthScript script,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default,
        List<DeviceHealthScriptAssignment>? assignments = null)
    {
        var folderPath = Path.Combine(outputPath, "DeviceHealthScripts");

        var filePath = GetUniqueFilePath(folderPath, script.DisplayName ?? script.Id ?? "unknown", script.Id);

        object exportPayload = assignments != null
            ? new Models.DeviceHealthScriptExport { Script = script, Assignments = assignments }
            : script;

        await WriteJsonFileAsync(filePath, exportPayload, exportPayload.GetType(), cancellationToken);

        if (script.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "DeviceHealthScript",
                OriginalId = script.Id,
                Name = script.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportDeviceHealthScriptsAsync(
        IEnumerable<DeviceHealthScript> scripts,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var script in scripts)
        {
            await ExportDeviceHealthScriptAsync(script, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportMacCustomAttributeAsync(
        DeviceCustomAttributeShellScript script,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "MacCustomAttributes");

        var filePath = GetUniqueFilePath(folderPath, script.DisplayName ?? script.Id ?? "unknown", script.Id);

        await WriteJsonFileAsync(filePath, script, script.GetType(), cancellationToken);

        if (script.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "MacCustomAttribute",
                OriginalId = script.Id,
                Name = script.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportMacCustomAttributesAsync(
        IEnumerable<DeviceCustomAttributeShellScript> scripts,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var script in scripts)
        {
            await ExportMacCustomAttributeAsync(script, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportFeatureUpdateProfileAsync(
        WindowsFeatureUpdateProfile profile,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "FeatureUpdates");

        var filePath = GetUniqueFilePath(folderPath, profile.DisplayName ?? profile.Id ?? "unknown", profile.Id);

        await WriteJsonFileAsync(filePath, profile, profile.GetType(), cancellationToken);

        if (profile.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "FeatureUpdateProfile",
                OriginalId = profile.Id,
                Name = profile.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportFeatureUpdateProfilesAsync(
        IEnumerable<WindowsFeatureUpdateProfile> profiles,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var profile in profiles)
        {
            await ExportFeatureUpdateProfileAsync(profile, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportNamedLocationAsync(
        NamedLocation namedLocation,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "NamedLocations");

        var displayName = namedLocation.AdditionalData?.TryGetValue("displayName", out var value) == true
            ? value?.ToString()
            : null;

        var filePath = GetUniqueFilePath(folderPath, displayName ?? "unknown", namedLocation.Id);

        await WriteJsonFileAsync(filePath, namedLocation, namedLocation.GetType(), cancellationToken);

        if (namedLocation.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "NamedLocation",
                OriginalId = namedLocation.Id,
                Name = displayName ?? "Unknown"
            });
        }
    }

    public async Task ExportNamedLocationsAsync(
        IEnumerable<NamedLocation> namedLocations,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var namedLocation in namedLocations)
        {
            await ExportNamedLocationAsync(namedLocation, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportAuthenticationStrengthPolicyAsync(
        AuthenticationStrengthPolicy policy,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "AuthenticationStrengths");

        var filePath = GetUniqueFilePath(folderPath, policy.DisplayName ?? policy.Id ?? "unknown", policy.Id);

        await WriteJsonFileAsync(filePath, policy, policy.GetType(), cancellationToken);

        if (policy.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "AuthenticationStrengthPolicy",
                OriginalId = policy.Id,
                Name = policy.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportAuthenticationStrengthPoliciesAsync(
        IEnumerable<AuthenticationStrengthPolicy> policies,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var policy in policies)
        {
            await ExportAuthenticationStrengthPolicyAsync(policy, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportConditionalAccessPolicyAsync(
        ConditionalAccessPolicy policy,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "ConditionalAccessPolicies");

        var filePath = GetUniqueFilePath(folderPath, policy.DisplayName ?? policy.Id ?? "unknown", policy.Id);

        await WriteJsonFileAsync(filePath, policy, policy.GetType(), cancellationToken);

        if (policy.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "ConditionalAccessPolicy",
                OriginalId = policy.Id,
                Name = policy.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportConditionalAccessPoliciesAsync(
        IEnumerable<ConditionalAccessPolicy> policies,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var policy in policies)
        {
            await ExportConditionalAccessPolicyAsync(policy, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportConditionalAccessPolicyWithResolvedGuidsAsync(
        ConditionalAccessPolicy policy,
        string outputPath,
        MigrationTable migrationTable,
        IReadOnlyDictionary<string, string> nameLookup,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "ConditionalAccessPolicies");

        // Serialize the original policy to JSON
        var json = JsonSerializer.Serialize(policy, policy.GetType(), JsonOptions);

        // Parse as a mutable JSON DOM and walk all string values, replacing
        // any that match the lookup. This avoids the BackingStore hydration
        // problem with Graph SDK model deserialization.
        var node = System.Text.Json.Nodes.JsonNode.Parse(json);
        if (node != null)
        {
            ReplaceGuidsInJsonNode(node, nameLookup);
            json = node.ToJsonString(JsonOptions);
        }

        var filePath = GetUniqueFilePath(folderPath, policy.DisplayName ?? policy.Id ?? "unknown", policy.Id);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        if (policy.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "ConditionalAccessPolicy",
                OriginalId = policy.Id,
                Name = policy.DisplayName ?? "Unknown"
            });
        }
    }

    /// <summary>
    /// Recursively walks a JSON node tree and replaces any string values
    /// that match keys in the lookup dictionary with their resolved names.
    /// Skips well-known property names that should never be resolved
    /// (e.g., displayName, id, odataType, operator, state).
    /// </summary>
    private static void ReplaceGuidsInJsonNode(
        System.Text.Json.Nodes.JsonNode node,
        IReadOnlyDictionary<string, string> nameLookup)
    {
        if (node is System.Text.Json.Nodes.JsonObject obj)
        {
            foreach (var prop in obj.ToList())
            {
                if (prop.Value == null) continue;

                if (prop.Value is System.Text.Json.Nodes.JsonArray arr)
                {
                    // Only replace strings in arrays whose property names contain
                    // known GUID-bearing field patterns
                    if (IsGuidBearingArrayProperty(prop.Key))
                    {
                        for (var i = 0; i < arr.Count; i++)
                        {
                            if (arr[i] is System.Text.Json.Nodes.JsonValue val &&
                                val.TryGetValue<string>(out var str) &&
                                !string.IsNullOrEmpty(str) &&
                                nameLookup.TryGetValue(str, out var resolved))
                            {
                                arr[i] = System.Text.Json.Nodes.JsonValue.Create(resolved);
                            }
                        }
                    }
                    else
                    {
                        // Recurse into arrays of objects
                        foreach (var item in arr)
                        {
                            if (item != null)
                                ReplaceGuidsInJsonNode(item, nameLookup);
                        }
                    }
                }
                else if (prop.Value is System.Text.Json.Nodes.JsonObject)
                {
                    ReplaceGuidsInJsonNode(prop.Value, nameLookup);
                }
            }
        }
    }

    /// <summary>
    /// Returns true if the JSON property name is one that typically contains
    /// GUID values that should be resolved to display names in CA policies.
    /// </summary>
    private static bool IsGuidBearingArrayProperty(string propertyName)
    {
        return propertyName is
            "includeUsers" or "excludeUsers" or
            "includeGroups" or "excludeGroups" or
            "includeRoles" or "excludeRoles" or
            "includeApplications" or "excludeApplications" or
            "includeLocations" or "excludeLocations" or
            "includeAuthenticationContextClassReferences";
    }

    public async Task ExportAuthenticationContextAsync(
        AuthenticationContextClassReference contextClassReference,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "AuthenticationContexts");

        var displayName = contextClassReference.DisplayName ?? contextClassReference.Id;
        var filePath = GetUniqueFilePath(folderPath, displayName ?? "unknown", contextClassReference.Id);

        await WriteJsonFileAsync(filePath, contextClassReference, contextClassReference.GetType(), cancellationToken);

        if (contextClassReference.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "AuthenticationContext",
                OriginalId = contextClassReference.Id,
                Name = contextClassReference.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportAuthenticationContextsAsync(
        IEnumerable<AuthenticationContextClassReference> contextClassReferences,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var contextClassReference in contextClassReferences)
        {
            await ExportAuthenticationContextAsync(contextClassReference, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportTermsOfUseAgreementAsync(
        Agreement agreement,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "TermsOfUse");

        var filePath = GetUniqueFilePath(folderPath, agreement.DisplayName ?? agreement.Id ?? "unknown", agreement.Id);

        await WriteJsonFileAsync(filePath, agreement, agreement.GetType(), cancellationToken);

        if (agreement.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "TermsOfUseAgreement",
                OriginalId = agreement.Id,
                Name = agreement.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportTermsOfUseAgreementsAsync(
        IEnumerable<Agreement> agreements,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var agreement in agreements)
        {
            await ExportTermsOfUseAgreementAsync(agreement, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportDeviceManagementScriptAsync(
        DeviceManagementScript script,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "DeviceManagementScripts");

        var filePath = GetUniqueFilePath(folderPath, script.DisplayName ?? script.Id ?? "unknown", script.Id);

        await WriteJsonFileAsync(filePath, script, script.GetType(), cancellationToken);

        if (script.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "DeviceManagementScript",
                OriginalId = script.Id,
                Name = script.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportDeviceManagementScriptsAsync(
        IEnumerable<DeviceManagementScript> scripts,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var script in scripts)
        {
            await ExportDeviceManagementScriptAsync(script, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportDeviceShellScriptAsync(
        DeviceShellScript script,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "DeviceShellScripts");

        var filePath = GetUniqueFilePath(folderPath, script.DisplayName ?? script.Id ?? "unknown", script.Id);

        await WriteJsonFileAsync(filePath, script, script.GetType(), cancellationToken);

        if (script.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "DeviceShellScript",
                OriginalId = script.Id,
                Name = script.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportDeviceShellScriptsAsync(
        IEnumerable<DeviceShellScript> scripts,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var script in scripts)
        {
            await ExportDeviceShellScriptAsync(script, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportComplianceScriptAsync(
        DeviceComplianceScript script,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "ComplianceScripts");

        var filePath = GetUniqueFilePath(folderPath, script.DisplayName ?? script.Id ?? "unknown", script.Id);

        await WriteJsonFileAsync(filePath, script, script.GetType(), cancellationToken);

        if (script.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "ComplianceScript",
                OriginalId = script.Id,
                Name = script.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportComplianceScriptsAsync(
        IEnumerable<DeviceComplianceScript> scripts,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var script in scripts)
        {
            await ExportComplianceScriptAsync(script, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportQualityUpdateProfileAsync(
        WindowsQualityUpdateProfile profile,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "QualityUpdates");

        var filePath = GetUniqueFilePath(folderPath, profile.DisplayName ?? profile.Id ?? "unknown", profile.Id);

        await WriteJsonFileAsync(filePath, profile, profile.GetType(), cancellationToken);

        if (profile.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "QualityUpdateProfile",
                OriginalId = profile.Id,
                Name = profile.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportQualityUpdateProfilesAsync(
        IEnumerable<WindowsQualityUpdateProfile> profiles,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var profile in profiles)
        {
            await ExportQualityUpdateProfileAsync(profile, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportAdmxFileAsync(
        GroupPolicyUploadedDefinitionFile admxFile,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "AdmxFiles");

        var filePath = GetUniqueFilePath(folderPath, admxFile.DisplayName ?? admxFile.FileName ?? admxFile.Id ?? "unknown", admxFile.Id);

        await WriteJsonFileAsync(filePath, admxFile, admxFile.GetType(), cancellationToken);

        if (admxFile.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "AdmxFile",
                OriginalId = admxFile.Id,
                Name = admxFile.DisplayName ?? admxFile.FileName ?? "Unknown"
            });
        }
    }

    public async Task ExportAdmxFilesAsync(
        IEnumerable<GroupPolicyUploadedDefinitionFile> admxFiles,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var admxFile in admxFiles)
        {
            await ExportAdmxFileAsync(admxFile, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportDriverUpdateProfileAsync(
        WindowsDriverUpdateProfile profile,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "DriverUpdates");

        var filePath = GetUniqueFilePath(folderPath, profile.DisplayName ?? profile.Id ?? "unknown", profile.Id);

        await WriteJsonFileAsync(filePath, profile, profile.GetType(), cancellationToken);

        if (profile.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "DriverUpdateProfile",
                OriginalId = profile.Id,
                Name = profile.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportDriverUpdateProfilesAsync(
        IEnumerable<WindowsDriverUpdateProfile> profiles,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var profile in profiles)
        {
            await ExportDriverUpdateProfileAsync(profile, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportReusablePolicySettingAsync(
        DeviceManagementReusablePolicySetting setting,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "ReusablePolicySettings");

        var filePath = GetUniqueFilePath(folderPath, setting.DisplayName ?? setting.Id ?? "unknown", setting.Id);

        await WriteJsonFileAsync(filePath, setting, setting.GetType(), cancellationToken);

        if (setting.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "ReusablePolicySetting",
                OriginalId = setting.Id,
                Name = setting.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportReusablePolicySettingsAsync(
        IEnumerable<DeviceManagementReusablePolicySetting> settings,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var setting in settings)
        {
            await ExportReusablePolicySettingAsync(setting, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }

    public async Task ExportNotificationTemplateAsync(
        NotificationMessageTemplate template,
        string outputPath,
        MigrationTable migrationTable,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(outputPath, "NotificationTemplates");

        var filePath = GetUniqueFilePath(folderPath, template.DisplayName ?? template.Id ?? "unknown", template.Id);

        await WriteJsonFileAsync(filePath, template, template.GetType(), cancellationToken);

        if (template.Id != null)
        {
            migrationTable.AddOrUpdate(new MigrationEntry
            {
                ObjectType = "NotificationTemplate",
                OriginalId = template.Id,
                Name = template.DisplayName ?? "Unknown"
            });
        }
    }

    public async Task ExportNotificationTemplatesAsync(
        IEnumerable<NotificationMessageTemplate> templates,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var migrationTable = new MigrationTable();

        foreach (var template in templates)
        {
            await ExportNotificationTemplateAsync(template, outputPath, migrationTable, cancellationToken);
        }

        await SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
    }
}
