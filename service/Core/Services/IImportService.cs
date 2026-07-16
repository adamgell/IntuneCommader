using Intune.Commander.Core.Models;
using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IImportService
{
    Task<DeviceConfiguration?> ReadDeviceConfigurationAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<DeviceConfiguration>> ReadDeviceConfigurationsFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<MigrationTable> ReadMigrationTableAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<DeviceConfiguration> ImportDeviceConfigurationAsync(DeviceConfiguration config, MigrationTable migrationTable, CancellationToken cancellationToken = default);

    Task<CompliancePolicyExport?> ReadCompliancePolicyAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<CompliancePolicyExport>> ReadCompliancePoliciesFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<DeviceCompliancePolicy> ImportCompliancePolicyAsync(CompliancePolicyExport export, MigrationTable migrationTable, CancellationToken cancellationToken = default);

    Task<EndpointSecurityExport?> ReadEndpointSecurityIntentAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<EndpointSecurityExport>> ReadEndpointSecurityIntentsFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<DeviceManagementIntent> ImportEndpointSecurityIntentAsync(EndpointSecurityExport export, MigrationTable migrationTable, CancellationToken cancellationToken = default);

    Task<AdministrativeTemplateExport?> ReadAdministrativeTemplateAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<AdministrativeTemplateExport>> ReadAdministrativeTemplatesFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<GroupPolicyConfiguration> ImportAdministrativeTemplateAsync(AdministrativeTemplateExport export, MigrationTable migrationTable, CancellationToken cancellationToken = default);

    Task<DeviceEnrollmentConfiguration?> ReadEnrollmentConfigurationAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<DeviceEnrollmentConfiguration>> ReadEnrollmentConfigurationsFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<DeviceEnrollmentConfiguration> ImportEnrollmentConfigurationAsync(DeviceEnrollmentConfiguration configuration, MigrationTable migrationTable, CancellationToken cancellationToken = default);

    Task<ManagedAppPolicy?> ReadAppProtectionPolicyAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<ManagedAppPolicy>> ReadAppProtectionPoliciesFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<ManagedAppPolicy> ImportAppProtectionPolicyAsync(ManagedAppPolicy policy, MigrationTable migrationTable, CancellationToken cancellationToken = default);

    Task<ManagedDeviceMobileAppConfiguration?> ReadManagedDeviceAppConfigurationAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<ManagedDeviceMobileAppConfiguration>> ReadManagedDeviceAppConfigurationsFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<ManagedDeviceMobileAppConfiguration> ImportManagedDeviceAppConfigurationAsync(ManagedDeviceMobileAppConfiguration configuration, MigrationTable migrationTable, CancellationToken cancellationToken = default);

    Task<TargetedManagedAppConfiguration?> ReadTargetedManagedAppConfigurationAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<TargetedManagedAppConfiguration>> ReadTargetedManagedAppConfigurationsFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<TargetedManagedAppConfiguration> ImportTargetedManagedAppConfigurationAsync(TargetedManagedAppConfiguration configuration, MigrationTable migrationTable, CancellationToken cancellationToken = default);

    Task<TermsAndConditions?> ReadTermsAndConditionsAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<TermsAndConditions>> ReadTermsAndConditionsFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<TermsAndConditions> ImportTermsAndConditionsAsync(TermsAndConditions termsAndConditions, MigrationTable migrationTable, CancellationToken cancellationToken = default);

    Task<RoleScopeTag?> ReadScopeTagAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<RoleScopeTag>> ReadScopeTagsFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<RoleScopeTag> ImportScopeTagAsync(RoleScopeTag scopeTag, MigrationTable migrationTable, CancellationToken cancellationToken = default);

    Task<RoleDefinition?> ReadRoleDefinitionAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<RoleDefinition>> ReadRoleDefinitionsFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<RoleDefinition> ImportRoleDefinitionAsync(RoleDefinition roleDefinition, MigrationTable migrationTable, CancellationToken cancellationToken = default);

    Task<IntuneBrandingProfile?> ReadIntuneBrandingProfileAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<IntuneBrandingProfile>> ReadIntuneBrandingProfilesFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<IntuneBrandingProfile> ImportIntuneBrandingProfileAsync(IntuneBrandingProfile profile, MigrationTable migrationTable, CancellationToken cancellationToken = default);

    Task<OrganizationalBrandingLocalization?> ReadAzureBrandingLocalizationAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<OrganizationalBrandingLocalization>> ReadAzureBrandingLocalizationsFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<OrganizationalBrandingLocalization> ImportAzureBrandingLocalizationAsync(OrganizationalBrandingLocalization localization, MigrationTable migrationTable, CancellationToken cancellationToken = default);

    Task<WindowsAutopilotDeploymentProfile?> ReadAutopilotProfileAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<WindowsAutopilotDeploymentProfile>> ReadAutopilotProfilesFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<WindowsAutopilotDeploymentProfile> ImportAutopilotProfileAsync(WindowsAutopilotDeploymentProfile profile, MigrationTable migrationTable, CancellationToken cancellationToken = default);

    Task<DeviceHealthScript?> ReadDeviceHealthScriptAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<DeviceHealthScript>> ReadDeviceHealthScriptsFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<DeviceHealthScript> ImportDeviceHealthScriptAsync(DeviceHealthScript script, MigrationTable migrationTable, CancellationToken cancellationToken = default);

    Task<DeviceCustomAttributeShellScript?> ReadMacCustomAttributeAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<DeviceCustomAttributeShellScript>> ReadMacCustomAttributesFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<DeviceCustomAttributeShellScript> ImportMacCustomAttributeAsync(DeviceCustomAttributeShellScript script, MigrationTable migrationTable, CancellationToken cancellationToken = default);

    Task<WindowsFeatureUpdateProfile?> ReadFeatureUpdateProfileAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<WindowsFeatureUpdateProfile>> ReadFeatureUpdateProfilesFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<WindowsFeatureUpdateProfile> ImportFeatureUpdateProfileAsync(WindowsFeatureUpdateProfile profile, MigrationTable migrationTable, CancellationToken cancellationToken = default);

    Task<NamedLocation?> ReadNamedLocationAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<NamedLocation>> ReadNamedLocationsFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<NamedLocation> ImportNamedLocationAsync(NamedLocation namedLocation, MigrationTable migrationTable, CancellationToken cancellationToken = default);

    Task<AuthenticationStrengthPolicy?> ReadAuthenticationStrengthPolicyAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<AuthenticationStrengthPolicy>> ReadAuthenticationStrengthPoliciesFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<AuthenticationStrengthPolicy> ImportAuthenticationStrengthPolicyAsync(AuthenticationStrengthPolicy policy, MigrationTable migrationTable, CancellationToken cancellationToken = default);

    Task<AuthenticationContextClassReference?> ReadAuthenticationContextAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<AuthenticationContextClassReference>> ReadAuthenticationContextsFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<AuthenticationContextClassReference> ImportAuthenticationContextAsync(AuthenticationContextClassReference contextClassReference, MigrationTable migrationTable, CancellationToken cancellationToken = default);

    Task<Agreement?> ReadTermsOfUseAgreementAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<Agreement>> ReadTermsOfUseAgreementsFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<Agreement> ImportTermsOfUseAgreementAsync(Agreement agreement, MigrationTable migrationTable, CancellationToken cancellationToken = default);

    Task<DeviceManagementScript?> ReadDeviceManagementScriptAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<DeviceManagementScript>> ReadDeviceManagementScriptsFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<DeviceManagementScript> ImportDeviceManagementScriptAsync(DeviceManagementScript script, MigrationTable migrationTable, CancellationToken cancellationToken = default);

    Task<DeviceShellScript?> ReadDeviceShellScriptAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<DeviceShellScript>> ReadDeviceShellScriptsFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<DeviceShellScript> ImportDeviceShellScriptAsync(DeviceShellScript script, MigrationTable migrationTable, CancellationToken cancellationToken = default);

    Task<DeviceComplianceScript?> ReadComplianceScriptAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<DeviceComplianceScript>> ReadComplianceScriptsFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<DeviceComplianceScript> ImportComplianceScriptAsync(DeviceComplianceScript script, MigrationTable migrationTable, CancellationToken cancellationToken = default);

    Task<WindowsQualityUpdateProfile?> ReadQualityUpdateProfileAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<WindowsQualityUpdateProfile>> ReadQualityUpdateProfilesFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<WindowsQualityUpdateProfile> ImportQualityUpdateProfileAsync(WindowsQualityUpdateProfile profile, MigrationTable migrationTable, CancellationToken cancellationToken = default);

    Task<WindowsDriverUpdateProfile?> ReadDriverUpdateProfileAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<WindowsDriverUpdateProfile>> ReadDriverUpdateProfilesFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<WindowsDriverUpdateProfile> ImportDriverUpdateProfileAsync(WindowsDriverUpdateProfile profile, MigrationTable migrationTable, CancellationToken cancellationToken = default);
    Task<SettingsCatalogExport?> ReadSettingsCatalogPolicyAsync(string filePath, CancellationToken cancellationToken = default);
    Task<List<SettingsCatalogExport>> ReadSettingsCatalogPoliciesFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<DeviceManagementConfigurationPolicy> ImportSettingsCatalogPolicyAsync(SettingsCatalogExport export, MigrationTable migrationTable, CancellationToken cancellationToken = default);
}
