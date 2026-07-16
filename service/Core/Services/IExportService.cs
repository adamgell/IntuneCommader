using Intune.Commander.Core.Models;
using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IExportService
{
    Task ExportDeviceConfigurationAsync(DeviceConfiguration config, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportDeviceConfigurationsAsync(IEnumerable<DeviceConfiguration> configs, string outputPath, CancellationToken cancellationToken = default);

    Task ExportCompliancePolicyAsync(DeviceCompliancePolicy policy, IReadOnlyList<DeviceCompliancePolicyAssignment> assignments, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportCompliancePoliciesAsync(IEnumerable<(DeviceCompliancePolicy Policy, IReadOnlyList<DeviceCompliancePolicyAssignment> Assignments)> policies, string outputPath, CancellationToken cancellationToken = default);

    Task ExportApplicationAsync(MobileApp app, IReadOnlyList<MobileAppAssignment> assignments, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportApplicationsAsync(IEnumerable<(MobileApp App, IReadOnlyList<MobileAppAssignment> Assignments)> apps, string outputPath, CancellationToken cancellationToken = default);

    Task ExportEndpointSecurityIntentAsync(DeviceManagementIntent intent, IReadOnlyList<DeviceManagementIntentAssignment> assignments, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportEndpointSecurityIntentsAsync(IEnumerable<(DeviceManagementIntent Intent, IReadOnlyList<DeviceManagementIntentAssignment> Assignments)> intents, string outputPath, CancellationToken cancellationToken = default);

    Task ExportAdministrativeTemplateAsync(GroupPolicyConfiguration template, IReadOnlyList<GroupPolicyConfigurationAssignment> assignments, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportAdministrativeTemplatesAsync(IEnumerable<(GroupPolicyConfiguration Template, IReadOnlyList<GroupPolicyConfigurationAssignment> Assignments)> templates, string outputPath, CancellationToken cancellationToken = default);

    Task ExportEnrollmentConfigurationAsync(DeviceEnrollmentConfiguration configuration, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportEnrollmentConfigurationsAsync(IEnumerable<DeviceEnrollmentConfiguration> configurations, string outputPath, CancellationToken cancellationToken = default);

    Task ExportAppProtectionPolicyAsync(ManagedAppPolicy policy, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportAppProtectionPoliciesAsync(IEnumerable<ManagedAppPolicy> policies, string outputPath, CancellationToken cancellationToken = default);

    Task ExportManagedDeviceAppConfigurationAsync(ManagedDeviceMobileAppConfiguration configuration, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportManagedDeviceAppConfigurationsAsync(IEnumerable<ManagedDeviceMobileAppConfiguration> configurations, string outputPath, CancellationToken cancellationToken = default);

    Task ExportTargetedManagedAppConfigurationAsync(TargetedManagedAppConfiguration configuration, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportTargetedManagedAppConfigurationsAsync(IEnumerable<TargetedManagedAppConfiguration> configurations, string outputPath, CancellationToken cancellationToken = default);

    Task ExportTermsAndConditionsAsync(TermsAndConditions termsAndConditions, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportTermsAndConditionsCollectionAsync(IEnumerable<TermsAndConditions> termsCollection, string outputPath, CancellationToken cancellationToken = default);

    Task ExportScopeTagAsync(RoleScopeTag scopeTag, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportScopeTagsAsync(IEnumerable<RoleScopeTag> scopeTags, string outputPath, CancellationToken cancellationToken = default);

    Task ExportRoleDefinitionAsync(RoleDefinition roleDefinition, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportRoleDefinitionsAsync(IEnumerable<RoleDefinition> roleDefinitions, string outputPath, CancellationToken cancellationToken = default);

    Task ExportSettingsCatalogPolicyAsync(DeviceManagementConfigurationPolicy policy, IReadOnlyList<DeviceManagementConfigurationSetting> settings, IReadOnlyList<DeviceManagementConfigurationPolicyAssignment> assignments, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportSettingsCatalogPoliciesAsync(IEnumerable<(DeviceManagementConfigurationPolicy Policy, IReadOnlyList<DeviceManagementConfigurationSetting> Settings, IReadOnlyList<DeviceManagementConfigurationPolicyAssignment> Assignments)> policies, string outputPath, CancellationToken cancellationToken = default);

    Task ExportIntuneBrandingProfileAsync(IntuneBrandingProfile profile, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportIntuneBrandingProfilesAsync(IEnumerable<IntuneBrandingProfile> profiles, string outputPath, CancellationToken cancellationToken = default);

    Task ExportAzureBrandingLocalizationAsync(OrganizationalBrandingLocalization localization, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportAzureBrandingLocalizationsAsync(IEnumerable<OrganizationalBrandingLocalization> localizations, string outputPath, CancellationToken cancellationToken = default);

    Task ExportAutopilotProfileAsync(WindowsAutopilotDeploymentProfile profile, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportAutopilotProfilesAsync(IEnumerable<WindowsAutopilotDeploymentProfile> profiles, string outputPath, CancellationToken cancellationToken = default);

    Task ExportDeviceHealthScriptAsync(DeviceHealthScript script, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default, List<DeviceHealthScriptAssignment>? assignments = null);
    Task ExportDeviceHealthScriptsAsync(IEnumerable<DeviceHealthScript> scripts, string outputPath, CancellationToken cancellationToken = default);

    Task ExportMacCustomAttributeAsync(DeviceCustomAttributeShellScript script, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportMacCustomAttributesAsync(IEnumerable<DeviceCustomAttributeShellScript> scripts, string outputPath, CancellationToken cancellationToken = default);

    Task ExportFeatureUpdateProfileAsync(WindowsFeatureUpdateProfile profile, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportFeatureUpdateProfilesAsync(IEnumerable<WindowsFeatureUpdateProfile> profiles, string outputPath, CancellationToken cancellationToken = default);

    Task ExportNamedLocationAsync(NamedLocation namedLocation, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportNamedLocationsAsync(IEnumerable<NamedLocation> namedLocations, string outputPath, CancellationToken cancellationToken = default);

    Task ExportAuthenticationStrengthPolicyAsync(AuthenticationStrengthPolicy policy, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportAuthenticationStrengthPoliciesAsync(IEnumerable<AuthenticationStrengthPolicy> policies, string outputPath, CancellationToken cancellationToken = default);

    Task ExportConditionalAccessPolicyAsync(ConditionalAccessPolicy policy, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportConditionalAccessPoliciesAsync(IEnumerable<ConditionalAccessPolicy> policies, string outputPath, CancellationToken cancellationToken = default);
    Task ExportConditionalAccessPolicyWithResolvedGuidsAsync(ConditionalAccessPolicy policy, string outputPath, MigrationTable migrationTable, IReadOnlyDictionary<string, string> nameLookup, CancellationToken cancellationToken = default);

    Task ExportAuthenticationContextAsync(AuthenticationContextClassReference contextClassReference, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportAuthenticationContextsAsync(IEnumerable<AuthenticationContextClassReference> contextClassReferences, string outputPath, CancellationToken cancellationToken = default);

    Task ExportTermsOfUseAgreementAsync(Agreement agreement, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportTermsOfUseAgreementsAsync(IEnumerable<Agreement> agreements, string outputPath, CancellationToken cancellationToken = default);

    Task ExportDeviceManagementScriptAsync(DeviceManagementScript script, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportDeviceManagementScriptsAsync(IEnumerable<DeviceManagementScript> scripts, string outputPath, CancellationToken cancellationToken = default);

    Task ExportDeviceShellScriptAsync(DeviceShellScript script, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportDeviceShellScriptsAsync(IEnumerable<DeviceShellScript> scripts, string outputPath, CancellationToken cancellationToken = default);

    Task ExportComplianceScriptAsync(DeviceComplianceScript script, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportComplianceScriptsAsync(IEnumerable<DeviceComplianceScript> scripts, string outputPath, CancellationToken cancellationToken = default);

    Task ExportQualityUpdateProfileAsync(WindowsQualityUpdateProfile profile, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportQualityUpdateProfilesAsync(IEnumerable<WindowsQualityUpdateProfile> profiles, string outputPath, CancellationToken cancellationToken = default);

    Task ExportDriverUpdateProfileAsync(WindowsDriverUpdateProfile profile, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportDriverUpdateProfilesAsync(IEnumerable<WindowsDriverUpdateProfile> profiles, string outputPath, CancellationToken cancellationToken = default);
    Task ExportAdmxFileAsync(GroupPolicyUploadedDefinitionFile admxFile, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportAdmxFilesAsync(IEnumerable<GroupPolicyUploadedDefinitionFile> admxFiles, string outputPath, CancellationToken cancellationToken = default);

    Task ExportReusablePolicySettingAsync(DeviceManagementReusablePolicySetting setting, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportReusablePolicySettingsAsync(IEnumerable<DeviceManagementReusablePolicySetting> settings, string outputPath, CancellationToken cancellationToken = default);

    Task ExportNotificationTemplateAsync(NotificationMessageTemplate template, string outputPath, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task ExportNotificationTemplatesAsync(IEnumerable<NotificationMessageTemplate> templates, string outputPath, CancellationToken cancellationToken = default);

    Task SaveMigrationTableAsync(MigrationTable table, string outputPath, CancellationToken cancellationToken = default);
}
