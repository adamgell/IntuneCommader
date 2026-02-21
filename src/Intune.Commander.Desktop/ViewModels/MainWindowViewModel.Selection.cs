using System;

using System.Collections.Generic;

using System.Collections.ObjectModel;

using System.Linq;

using System.Threading;

using System.Threading.Tasks;

using CommunityToolkit.Mvvm.Input;

using Microsoft.Graph.Beta.Models;



namespace Intune.Commander.Desktop.ViewModels;



public partial class MainWindowViewModel : ViewModelBase

{



    // --- Refresh single item from Graph ---



    [RelayCommand]

    private async Task RefreshSelectedItemAsync(CancellationToken cancellationToken)

    {

        try

        {

            if (IsDeviceConfigCategory && SelectedConfiguration?.Id != null && _configProfileService != null)

            {

                StatusText = $"Refreshing {SelectedConfiguration.DisplayName}...";

                var updated = await _configProfileService.GetDeviceConfigurationAsync(SelectedConfiguration.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = DeviceConfigurations.IndexOf(SelectedConfiguration);

                    if (idx >= 0)

                    {

                        DeviceConfigurations[idx] = updated;

                        SelectedConfiguration = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed device configuration: {updated.DisplayName}");

                }

            }

            else if (IsCompliancePolicyCategory && SelectedCompliancePolicy?.Id != null && _compliancePolicyService != null)

            {

                StatusText = $"Refreshing {SelectedCompliancePolicy.DisplayName}...";

                var updated = await _compliancePolicyService.GetCompliancePolicyAsync(SelectedCompliancePolicy.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = CompliancePolicies.IndexOf(SelectedCompliancePolicy);

                    if (idx >= 0)

                    {

                        CompliancePolicies[idx] = updated;

                        SelectedCompliancePolicy = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed compliance policy: {updated.DisplayName}");

                }

            }

            else if (IsApplicationCategory && SelectedApplication?.Id != null && _applicationService != null)

            {

                StatusText = $"Refreshing {SelectedApplication.DisplayName}...";

                var updated = await _applicationService.GetApplicationAsync(SelectedApplication.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = Applications.IndexOf(SelectedApplication);

                    if (idx >= 0)

                    {

                        Applications[idx] = updated;

                        SelectedApplication = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed application: {updated.DisplayName}");

                }

            }

            else if (IsSettingsCatalogCategory && SelectedSettingsCatalogPolicy?.Id != null && _settingsCatalogService != null)

            {

                StatusText = $"Refreshing {SelectedSettingsCatalogPolicy.Name}...";

                var updated = await _settingsCatalogService.GetSettingsCatalogPolicyAsync(SelectedSettingsCatalogPolicy.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = SettingsCatalogPolicies.IndexOf(SelectedSettingsCatalogPolicy);

                    if (idx >= 0)

                    {

                        SettingsCatalogPolicies[idx] = updated;

                        SelectedSettingsCatalogPolicy = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed settings catalog policy: {updated.Name}");

                }

            }

            else if (IsEndpointSecurityCategory && SelectedEndpointSecurityIntent?.Id != null && _endpointSecurityService != null)

            {

                StatusText = $"Refreshing {SelectedEndpointSecurityIntent.DisplayName}...";

                var updated = await _endpointSecurityService.GetEndpointSecurityIntentAsync(SelectedEndpointSecurityIntent.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = EndpointSecurityIntents.IndexOf(SelectedEndpointSecurityIntent);

                    if (idx >= 0)

                    {

                        EndpointSecurityIntents[idx] = updated;

                        SelectedEndpointSecurityIntent = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed endpoint security intent: {updated.DisplayName}");

                }

            }

            else if (IsAdministrativeTemplatesCategory && SelectedAdministrativeTemplate?.Id != null && _administrativeTemplateService != null)

            {

                StatusText = $"Refreshing {SelectedAdministrativeTemplate.DisplayName}...";

                var updated = await _administrativeTemplateService.GetAdministrativeTemplateAsync(SelectedAdministrativeTemplate.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = AdministrativeTemplates.IndexOf(SelectedAdministrativeTemplate);

                    if (idx >= 0)

                    {

                        AdministrativeTemplates[idx] = updated;

                        SelectedAdministrativeTemplate = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed administrative template: {updated.DisplayName}");

                }

            }

            else if (IsEnrollmentConfigurationsCategory && SelectedEnrollmentConfiguration?.Id != null && _enrollmentConfigurationService != null)

            {

                StatusText = $"Refreshing {SelectedEnrollmentConfiguration.DisplayName}...";

                var updated = await _enrollmentConfigurationService.GetEnrollmentConfigurationAsync(SelectedEnrollmentConfiguration.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = EnrollmentConfigurations.IndexOf(SelectedEnrollmentConfiguration);

                    if (idx >= 0)

                    {

                        EnrollmentConfigurations[idx] = updated;

                        SelectedEnrollmentConfiguration = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed enrollment configuration: {updated.DisplayName}");

                }

            }

            else if (IsAppProtectionPoliciesCategory && SelectedAppProtectionPolicy?.Id != null && _appProtectionPolicyService != null)

            {

                StatusText = $"Refreshing {SelectedAppProtectionPolicy.DisplayName}...";

                var updated = await _appProtectionPolicyService.GetAppProtectionPolicyAsync(SelectedAppProtectionPolicy.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = AppProtectionPolicies.IndexOf(SelectedAppProtectionPolicy);

                    if (idx >= 0)

                    {

                        AppProtectionPolicies[idx] = updated;

                        SelectedAppProtectionPolicy = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed app protection policy: {updated.DisplayName}");

                }

            }

            else if (IsManagedDeviceAppConfigurationsCategory && SelectedManagedDeviceAppConfiguration?.Id != null && _managedAppConfigurationService != null)

            {

                StatusText = $"Refreshing {SelectedManagedDeviceAppConfiguration.DisplayName}...";

                var updated = await _managedAppConfigurationService.GetManagedDeviceAppConfigurationAsync(SelectedManagedDeviceAppConfiguration.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = ManagedDeviceAppConfigurations.IndexOf(SelectedManagedDeviceAppConfiguration);

                    if (idx >= 0)

                    {

                        ManagedDeviceAppConfigurations[idx] = updated;

                        SelectedManagedDeviceAppConfiguration = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed managed device app configuration: {updated.DisplayName}");

                }

            }

            else if (IsTargetedManagedAppConfigurationsCategory && SelectedTargetedManagedAppConfiguration?.Id != null && _managedAppConfigurationService != null)

            {

                StatusText = $"Refreshing {SelectedTargetedManagedAppConfiguration.DisplayName}...";

                var updated = await _managedAppConfigurationService.GetTargetedManagedAppConfigurationAsync(SelectedTargetedManagedAppConfiguration.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = TargetedManagedAppConfigurations.IndexOf(SelectedTargetedManagedAppConfiguration);

                    if (idx >= 0)

                    {

                        TargetedManagedAppConfigurations[idx] = updated;

                        SelectedTargetedManagedAppConfiguration = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed targeted managed app configuration: {updated.DisplayName}");

                }

            }

            else if (IsTermsAndConditionsCategory && SelectedTermsAndConditions?.Id != null && _termsAndConditionsService != null)

            {

                StatusText = $"Refreshing {SelectedTermsAndConditions.DisplayName}...";

                var updated = await _termsAndConditionsService.GetTermsAndConditionsAsync(SelectedTermsAndConditions.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = TermsAndConditionsCollection.IndexOf(SelectedTermsAndConditions);

                    if (idx >= 0)

                    {

                        TermsAndConditionsCollection[idx] = updated;

                        SelectedTermsAndConditions = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed terms and conditions: {updated.DisplayName}");

                }

            }

            else if (IsScopeTagsCategory && SelectedScopeTag?.Id != null && _scopeTagService != null)

            {

                StatusText = $"Refreshing {SelectedScopeTag.DisplayName}...";

                var updated = await _scopeTagService.GetScopeTagAsync(SelectedScopeTag.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = ScopeTags.IndexOf(SelectedScopeTag);

                    if (idx >= 0)

                    {

                        ScopeTags[idx] = updated;

                        SelectedScopeTag = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed scope tag: {updated.DisplayName}");

                }

            }

            else if (IsRoleDefinitionsCategory && SelectedRoleDefinition?.Id != null && _roleDefinitionService != null)

            {

                StatusText = $"Refreshing {SelectedRoleDefinition.DisplayName}...";

                var updated = await _roleDefinitionService.GetRoleDefinitionAsync(SelectedRoleDefinition.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = RoleDefinitions.IndexOf(SelectedRoleDefinition);

                    if (idx >= 0)

                    {

                        RoleDefinitions[idx] = updated;

                        SelectedRoleDefinition = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed role definition: {updated.DisplayName}");

                }

            }

            else if (IsIntuneBrandingCategory && SelectedIntuneBrandingProfile?.Id != null && _intuneBrandingService != null)

            {

                StatusText = $"Refreshing {SelectedIntuneBrandingProfile.ProfileName ?? SelectedIntuneBrandingProfile.DisplayName}...";

                var updated = await _intuneBrandingService.GetIntuneBrandingProfileAsync(SelectedIntuneBrandingProfile.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = IntuneBrandingProfiles.IndexOf(SelectedIntuneBrandingProfile);

                    if (idx >= 0)

                    {

                        IntuneBrandingProfiles[idx] = updated;

                        SelectedIntuneBrandingProfile = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed Intune branding profile: {updated.ProfileName ?? updated.DisplayName}");

                }

            }

            else if (IsAzureBrandingCategory && SelectedAzureBrandingLocalization?.Id != null && _azureBrandingService != null)

            {

                StatusText = $"Refreshing {SelectedAzureBrandingLocalization.Id}...";

                var updated = await _azureBrandingService.GetBrandingLocalizationAsync(SelectedAzureBrandingLocalization.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = AzureBrandingLocalizations.IndexOf(SelectedAzureBrandingLocalization);

                    if (idx >= 0)

                    {

                        AzureBrandingLocalizations[idx] = updated;

                        SelectedAzureBrandingLocalization = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed Azure branding localization: {updated.Id}");

                }

            }

            else if (IsConditionalAccessCategory && SelectedConditionalAccessPolicy?.Id != null && _conditionalAccessPolicyService != null)

            {

                StatusText = $"Refreshing {SelectedConditionalAccessPolicy.DisplayName}...";

                var updated = await _conditionalAccessPolicyService.GetPolicyAsync(SelectedConditionalAccessPolicy.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = ConditionalAccessPolicies.IndexOf(SelectedConditionalAccessPolicy);

                    if (idx >= 0)

                    {

                        ConditionalAccessPolicies[idx] = updated;

                        SelectedConditionalAccessPolicy = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed conditional access policy: {updated.DisplayName}");

                }

            }

            else if (IsAssignmentFiltersCategory && SelectedAssignmentFilter?.Id != null && _assignmentFilterService != null)

            {

                StatusText = $"Refreshing {SelectedAssignmentFilter.DisplayName}...";

                var updated = await _assignmentFilterService.GetFilterAsync(SelectedAssignmentFilter.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = AssignmentFilters.IndexOf(SelectedAssignmentFilter);

                    if (idx >= 0)

                    {

                        AssignmentFilters[idx] = updated;

                        SelectedAssignmentFilter = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed assignment filter: {updated.DisplayName}");

                }

            }

            else if (IsPolicySetsCategory && SelectedPolicySet?.Id != null && _policySetService != null)

            {

                StatusText = $"Refreshing {SelectedPolicySet.DisplayName}...";

                var updated = await _policySetService.GetPolicySetAsync(SelectedPolicySet.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = PolicySets.IndexOf(SelectedPolicySet);

                    if (idx >= 0)

                    {

                        PolicySets[idx] = updated;

                        SelectedPolicySet = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed policy set: {updated.DisplayName}");

                }

            }

            else if (IsAutopilotProfilesCategory && SelectedAutopilotProfile?.Id != null && _autopilotService != null)

            {

                StatusText = "Refreshing autopilot profile...";

                var updated = await _autopilotService.GetAutopilotProfileAsync(SelectedAutopilotProfile.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = AutopilotProfiles.IndexOf(SelectedAutopilotProfile);

                    if (idx >= 0)

                    {

                        AutopilotProfiles[idx] = updated;

                        SelectedAutopilotProfile = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed autopilot profile: {TryReadStringProperty(updated, "DisplayName")}");

                }

            }

            else if (IsDeviceHealthScriptsCategory && SelectedDeviceHealthScript?.Id != null && _deviceHealthScriptService != null)

            {

                StatusText = "Refreshing device health script...";

                var updated = await _deviceHealthScriptService.GetDeviceHealthScriptAsync(SelectedDeviceHealthScript.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = DeviceHealthScripts.IndexOf(SelectedDeviceHealthScript);

                    if (idx >= 0)

                    {

                        DeviceHealthScripts[idx] = updated;

                        SelectedDeviceHealthScript = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed device health script: {TryReadStringProperty(updated, "DisplayName")}");

                }

            }

            else if (IsMacCustomAttributesCategory && SelectedMacCustomAttribute?.Id != null && _macCustomAttributeService != null)

            {

                StatusText = "Refreshing mac custom attribute...";

                var updated = await _macCustomAttributeService.GetMacCustomAttributeAsync(SelectedMacCustomAttribute.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = MacCustomAttributes.IndexOf(SelectedMacCustomAttribute);

                    if (idx >= 0)

                    {

                        MacCustomAttributes[idx] = updated;

                        SelectedMacCustomAttribute = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed mac custom attribute: {TryReadStringProperty(updated, "DisplayName")}");

                }

            }

            else if (IsFeatureUpdatesCategory && SelectedFeatureUpdateProfile?.Id != null && _featureUpdateProfileService != null)

            {

                StatusText = "Refreshing feature update profile...";

                var updated = await _featureUpdateProfileService.GetFeatureUpdateProfileAsync(SelectedFeatureUpdateProfile.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = FeatureUpdateProfiles.IndexOf(SelectedFeatureUpdateProfile);

                    if (idx >= 0)

                    {

                        FeatureUpdateProfiles[idx] = updated;

                        SelectedFeatureUpdateProfile = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed feature update profile: {TryReadStringProperty(updated, "DisplayName")}");

                }

            }

            else if (IsNamedLocationsCategory && SelectedNamedLocation?.Id != null && _namedLocationService != null)

            {

                StatusText = "Refreshing named location...";

                var updated = await _namedLocationService.GetNamedLocationAsync(SelectedNamedLocation.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = NamedLocations.IndexOf(SelectedNamedLocation);

                    if (idx >= 0)

                    {

                        NamedLocations[idx] = updated;

                        SelectedNamedLocation = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed named location: {TryReadStringProperty(updated, "DisplayName")}");

                }

            }

            else if (IsAuthenticationStrengthsCategory && SelectedAuthenticationStrengthPolicy?.Id != null && _authenticationStrengthService != null)

            {

                StatusText = "Refreshing authentication strength...";

                var updated = await _authenticationStrengthService.GetAuthenticationStrengthPolicyAsync(SelectedAuthenticationStrengthPolicy.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = AuthenticationStrengthPolicies.IndexOf(SelectedAuthenticationStrengthPolicy);

                    if (idx >= 0)

                    {

                        AuthenticationStrengthPolicies[idx] = updated;

                        SelectedAuthenticationStrengthPolicy = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed authentication strength: {TryReadStringProperty(updated, "DisplayName")}");

                }

            }

            else if (IsAuthenticationContextsCategory && SelectedAuthenticationContextClassReference?.Id != null && _authenticationContextService != null)

            {

                StatusText = "Refreshing authentication context...";

                var updated = await _authenticationContextService.GetAuthenticationContextAsync(SelectedAuthenticationContextClassReference.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = AuthenticationContextClassReferences.IndexOf(SelectedAuthenticationContextClassReference);

                    if (idx >= 0)

                    {

                        AuthenticationContextClassReferences[idx] = updated;

                        SelectedAuthenticationContextClassReference = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed authentication context: {TryReadStringProperty(updated, "DisplayName")}");

                }

            }

            else if (IsTermsOfUseCategory && SelectedTermsOfUseAgreement?.Id != null && _termsOfUseService != null)

            {

                StatusText = "Refreshing terms of use...";

                var updated = await _termsOfUseService.GetTermsOfUseAgreementAsync(SelectedTermsOfUseAgreement.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = TermsOfUseAgreements.IndexOf(SelectedTermsOfUseAgreement);

                    if (idx >= 0)

                    {

                        TermsOfUseAgreements[idx] = updated;

                        SelectedTermsOfUseAgreement = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed terms of use agreement: {TryReadStringProperty(updated, "DisplayName")}");

                }

            }

            else if (IsDeviceManagementScriptsCategory && SelectedDeviceManagementScript?.Id != null && _deviceManagementScriptService != null)

            {

                StatusText = "Refreshing device management script...";

                var updated = await _deviceManagementScriptService.GetDeviceManagementScriptAsync(SelectedDeviceManagementScript.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = DeviceManagementScripts.IndexOf(SelectedDeviceManagementScript);

                    if (idx >= 0)

                    {

                        DeviceManagementScripts[idx] = updated;

                        SelectedDeviceManagementScript = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed device management script: {TryReadStringProperty(updated, "DisplayName")}");

                }

            }

            else if (IsDeviceShellScriptsCategory && SelectedDeviceShellScript?.Id != null && _deviceShellScriptService != null)

            {

                StatusText = "Refreshing device shell script...";

                var updated = await _deviceShellScriptService.GetDeviceShellScriptAsync(SelectedDeviceShellScript.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = DeviceShellScripts.IndexOf(SelectedDeviceShellScript);

                    if (idx >= 0)

                    {

                        DeviceShellScripts[idx] = updated;

                        SelectedDeviceShellScript = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed device shell script: {TryReadStringProperty(updated, "DisplayName")}");

                }

            }

            else if (IsComplianceScriptsCategory && SelectedComplianceScript?.Id != null && _complianceScriptService != null)

            {

                StatusText = "Refreshing compliance script...";

                var updated = await _complianceScriptService.GetComplianceScriptAsync(SelectedComplianceScript.Id, cancellationToken);

                if (updated != null)

                {

                    var idx = ComplianceScripts.IndexOf(SelectedComplianceScript);

                    if (idx >= 0)

                    {

                        ComplianceScripts[idx] = updated;

                        SelectedComplianceScript = updated;

                    }

                    DebugLog.Log("Graph", $"Refreshed compliance script: {TryReadStringProperty(updated, "DisplayName")}");

                }

            }

            else

            {

                return;

            }



            StatusText = "Item refreshed";

        }

        catch (Exception ex)

        {

            DebugLog.LogError($"Failed to refresh item: {FormatGraphError(ex)}", ex);

            SetError($"Refresh failed: {FormatGraphError(ex)}");

        }

    }



    public bool CanRefreshSelectedItem =>

        (IsDeviceConfigCategory && SelectedConfiguration != null) ||

        (IsCompliancePolicyCategory && SelectedCompliancePolicy != null) ||

        (IsApplicationCategory && SelectedApplication != null) ||

        (IsSettingsCatalogCategory && SelectedSettingsCatalogPolicy != null) ||

        (IsEndpointSecurityCategory && SelectedEndpointSecurityIntent != null) ||

        (IsAdministrativeTemplatesCategory && SelectedAdministrativeTemplate != null) ||

        (IsEnrollmentConfigurationsCategory && SelectedEnrollmentConfiguration != null) ||

        (IsAppProtectionPoliciesCategory && SelectedAppProtectionPolicy != null) ||

        (IsManagedDeviceAppConfigurationsCategory && SelectedManagedDeviceAppConfiguration != null) ||

        (IsTargetedManagedAppConfigurationsCategory && SelectedTargetedManagedAppConfiguration != null) ||

        (IsTermsAndConditionsCategory && SelectedTermsAndConditions != null) ||

        (IsScopeTagsCategory && SelectedScopeTag != null) ||

        (IsRoleDefinitionsCategory && SelectedRoleDefinition != null) ||

        (IsIntuneBrandingCategory && SelectedIntuneBrandingProfile != null) ||

        (IsAzureBrandingCategory && SelectedAzureBrandingLocalization != null) ||

        (IsConditionalAccessCategory && SelectedConditionalAccessPolicy != null) ||

        (IsAssignmentFiltersCategory && SelectedAssignmentFilter != null) ||

        (IsPolicySetsCategory && SelectedPolicySet != null) ||

        (IsAutopilotProfilesCategory && SelectedAutopilotProfile != null) ||

        (IsDeviceHealthScriptsCategory && SelectedDeviceHealthScript != null) ||

        (IsMacCustomAttributesCategory && SelectedMacCustomAttribute != null) ||

        (IsFeatureUpdatesCategory && SelectedFeatureUpdateProfile != null) ||

        (IsNamedLocationsCategory && SelectedNamedLocation != null) ||

        (IsAuthenticationStrengthsCategory && SelectedAuthenticationStrengthPolicy != null) ||

        (IsAuthenticationContextsCategory && SelectedAuthenticationContextClassReference != null) ||

        (IsTermsOfUseCategory && SelectedTermsOfUseAgreement != null) ||

        (IsDeviceManagementScriptsCategory && SelectedDeviceManagementScript != null) ||

        (IsDeviceShellScriptsCategory && SelectedDeviceShellScript != null) ||

        (IsComplianceScriptsCategory && SelectedComplianceScript != null);



    // --- Selection-changed handlers (load detail + assignments) ---



    partial void OnSelectedConfigurationChanged(DeviceConfiguration? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = FriendlyODataType(value?.OdataType);

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

        if (value?.Id != null)

            _ = LoadConfigAssignmentsAsync(value.Id);

    }



    partial void OnSelectedCompliancePolicyChanged(DeviceCompliancePolicy? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = FriendlyODataType(value?.OdataType);

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

        if (value?.Id != null)

            _ = LoadCompliancePolicyAssignmentsAsync(value.Id);

    }



    partial void OnSelectedSettingsCatalogPolicyChanged(DeviceManagementConfigurationPolicy? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = value?.Platforms?.ToString() ?? "";

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

        if (value?.Id != null)

            _ = LoadSettingsCatalogAssignmentsAsync(value.Id);

    }



    partial void OnSelectedApplicationChanged(MobileApp? value)

    {

        SelectedItemAssignments.Clear();

        var odataType = value?.OdataType;

        SelectedItemTypeName = FriendlyODataType(odataType);

        SelectedItemPlatform = InferPlatform(odataType);

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

        if (value?.Id != null)

            _ = LoadApplicationAssignmentsAsync(value.Id);

    }



    partial void OnSelectedConditionalAccessPolicyChanged(ConditionalAccessPolicy? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = "Conditional Access";

        SelectedItemPlatform = "";

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

    }



    partial void OnSelectedEndpointSecurityIntentChanged(DeviceManagementIntent? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = "Endpoint Security";

        SelectedItemPlatform = "";

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

        if (value?.Id != null)

            _ = LoadEndpointSecurityAssignmentsAsync(value.Id);

    }



    partial void OnSelectedAdministrativeTemplateChanged(GroupPolicyConfiguration? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = "Administrative Template";

        SelectedItemPlatform = "";

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

        if (value?.Id != null)

            _ = LoadAdministrativeTemplateAssignmentsAsync(value.Id);

    }



    partial void OnSelectedEnrollmentConfigurationChanged(DeviceEnrollmentConfiguration? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = FriendlyODataType(value?.OdataType);

        SelectedItemPlatform = "";

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

    }



    partial void OnSelectedAppProtectionPolicyChanged(ManagedAppPolicy? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = FriendlyODataType(value?.OdataType);

        SelectedItemPlatform = "";

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

    }



    partial void OnSelectedManagedDeviceAppConfigurationChanged(ManagedDeviceMobileAppConfiguration? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = FriendlyODataType(value?.OdataType);

        SelectedItemPlatform = "";

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

    }



    partial void OnSelectedTargetedManagedAppConfigurationChanged(TargetedManagedAppConfiguration? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = FriendlyODataType(value?.OdataType);

        SelectedItemPlatform = "";

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

    }



    partial void OnSelectedTermsAndConditionsChanged(TermsAndConditions? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = "Terms and Conditions";

        SelectedItemPlatform = "";

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

    }



    partial void OnSelectedScopeTagChanged(RoleScopeTag? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = "Scope Tag";

        SelectedItemPlatform = "";

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

    }



    partial void OnSelectedRoleDefinitionChanged(RoleDefinition? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = "Role Definition";

        SelectedItemPlatform = "";

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

    }



    partial void OnSelectedIntuneBrandingProfileChanged(IntuneBrandingProfile? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = "Intune Branding";

        SelectedItemPlatform = "";

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

    }



    partial void OnSelectedAzureBrandingLocalizationChanged(OrganizationalBrandingLocalization? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = "Azure Branding";

        SelectedItemPlatform = "";

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

    }



    partial void OnSelectedAssignmentFilterChanged(DeviceAndAppManagementAssignmentFilter? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = "Assignment Filter";

        SelectedItemPlatform = "";

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

    }



    partial void OnSelectedPolicySetChanged(PolicySet? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = "Policy Set";

        SelectedItemPlatform = "";

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

    }



    partial void OnSelectedAutopilotProfileChanged(WindowsAutopilotDeploymentProfile? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = "Autopilot Profile";

        SelectedItemPlatform = "";

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

    }



    partial void OnSelectedDeviceHealthScriptChanged(DeviceHealthScript? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = "Device Health Script";

        SelectedItemPlatform = "";

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

    }



    partial void OnSelectedMacCustomAttributeChanged(DeviceCustomAttributeShellScript? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = "Mac Custom Attribute";

        SelectedItemPlatform = "";

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

    }



    partial void OnSelectedFeatureUpdateProfileChanged(WindowsFeatureUpdateProfile? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = "Feature Update Profile";

        SelectedItemPlatform = "";

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

    }



    partial void OnSelectedNamedLocationChanged(NamedLocation? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = "Named Location";

        SelectedItemPlatform = "";

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

    }



    partial void OnSelectedAuthenticationStrengthPolicyChanged(AuthenticationStrengthPolicy? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = "Authentication Strength";

        SelectedItemPlatform = "";

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

    }



    partial void OnSelectedAuthenticationContextClassReferenceChanged(AuthenticationContextClassReference? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = "Authentication Context";

        SelectedItemPlatform = "";

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

    }



    partial void OnSelectedTermsOfUseAgreementChanged(Agreement? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = "Terms of Use";

        SelectedItemPlatform = "";

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

    }



    partial void OnSelectedDeviceManagementScriptChanged(DeviceManagementScript? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = "Device Management Script";

        SelectedItemPlatform = "";

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

        if (value?.Id != null)

            _ = LoadDeviceManagementScriptAssignmentsAsync(value.Id);

    }



    partial void OnSelectedDeviceShellScriptChanged(DeviceShellScript? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = "Device Shell Script";

        SelectedItemPlatform = "";

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

        if (value?.Id != null)

            _ = LoadDeviceShellScriptAssignmentsAsync(value.Id);

    }



    partial void OnSelectedComplianceScriptChanged(DeviceComplianceScript? value)

    {

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = "Compliance Script";

        SelectedItemPlatform = "";

        OnPropertyChanged(nameof(CanRefreshSelectedItem));

    }



    partial void OnSelectedDynamicGroupRowChanged(GroupRow? value)

    {

        SelectedGroupMembers.Clear();

        if (value?.GroupId != null && !string.IsNullOrEmpty(value.GroupId))

            _ = LoadGroupMembersAsync(value.GroupId);

    }



    partial void OnSelectedAssignedGroupRowChanged(GroupRow? value)

    {

        SelectedGroupMembers.Clear();

        if (value?.GroupId != null && !string.IsNullOrEmpty(value.GroupId))

            _ = LoadGroupMembersAsync(value.GroupId);

    }



    private async Task LoadGroupMembersAsync(string groupId)

    {

        if (_groupService == null) return;

        IsLoadingGroupMembers = true;

        try

        {

            var members = await _groupService.ListGroupMembersAsync(groupId);

            var items = members.Select(m => new GroupMemberItem

            {

                MemberType = m.MemberType,

                DisplayName = m.DisplayName,

                SecondaryInfo = m.SecondaryInfo,

                TertiaryInfo = m.TertiaryInfo,

                Status = m.Status,

                Id = m.Id

            }).ToList();

            SelectedGroupMembers = new ObservableCollection<GroupMemberItem>(items);

            DebugLog.Log("Graph", $"Loaded {items.Count} member(s) for group {groupId}");

        }

        catch (Exception ex)

        {

            DebugLog.LogError($"Failed to load group members: {FormatGraphError(ex)}", ex);

        }

        finally { IsLoadingGroupMembers = false; }

    }



    private async Task LoadConfigAssignmentsAsync(string configId)

    {

        if (_configProfileService == null) return;

        IsLoadingDetails = true;

        try

        {

            var assignments = await _configProfileService.GetAssignmentsAsync(configId);

            var items = new List<AssignmentDisplayItem>();

            foreach (var a in assignments)

                items.Add(await MapAssignmentAsync(a.Target));

            SelectedItemAssignments = new ObservableCollection<AssignmentDisplayItem>(items);

        }

        catch (Exception ex)

        {

            DebugLog.LogError($"Failed to load configuration assignments: {FormatGraphError(ex)}", ex);

        }

        finally { IsLoadingDetails = false; }

    }



    private async Task LoadCompliancePolicyAssignmentsAsync(string policyId)

    {

        if (_compliancePolicyService == null) return;

        IsLoadingDetails = true;

        try

        {

            var assignments = await _compliancePolicyService.GetAssignmentsAsync(policyId);

            var items = new List<AssignmentDisplayItem>();

            foreach (var a in assignments)

                items.Add(await MapAssignmentAsync(a.Target));

            SelectedItemAssignments = new ObservableCollection<AssignmentDisplayItem>(items);

        }

        catch (Exception ex)

        {

            DebugLog.LogError($"Failed to load compliance policy assignments: {FormatGraphError(ex)}", ex);

        }

        finally { IsLoadingDetails = false; }

    }



    private async Task LoadSettingsCatalogAssignmentsAsync(string policyId)

    {

        if (_settingsCatalogService == null) return;

        IsLoadingDetails = true;

        try

        {

            var assignments = await _settingsCatalogService.GetAssignmentsAsync(policyId);

            var items = new List<AssignmentDisplayItem>();

            foreach (var a in assignments)

                items.Add(await MapAssignmentAsync(a.Target));

            SelectedItemAssignments = new ObservableCollection<AssignmentDisplayItem>(items);

        }

        catch (Exception ex)

        {

            DebugLog.LogError($"Failed to load settings catalog assignments: {FormatGraphError(ex)}", ex);

        }

        finally { IsLoadingDetails = false; }

    }



    private async Task LoadApplicationAssignmentsAsync(string appId)

    {

        if (_applicationService == null) return;

        IsLoadingDetails = true;

        try

        {

            var assignments = await _applicationService.GetAssignmentsAsync(appId);

            var items = new List<AssignmentDisplayItem>();

            foreach (var a in assignments)

            {

                var item = await MapAssignmentAsync(a.Target);

                items.Add(new AssignmentDisplayItem

                {

                    Target = item.Target,

                    GroupId = item.GroupId,

                    TargetKind = item.TargetKind,

                    Intent = a.Intent?.ToString() ?? ""

                });

            }

            SelectedItemAssignments = new ObservableCollection<AssignmentDisplayItem>(items);

        }

        catch (Exception ex)

        {

            DebugLog.LogError($"Failed to load application assignments: {FormatGraphError(ex)}", ex);

        }

        finally { IsLoadingDetails = false; }

    }



    private async Task LoadEndpointSecurityAssignmentsAsync(string intentId)

    {

        if (_endpointSecurityService == null) return;

        IsLoadingDetails = true;

        try

        {

            var assignments = await _endpointSecurityService.GetAssignmentsAsync(intentId);

            var items = new List<AssignmentDisplayItem>();

            foreach (var a in assignments)

                items.Add(await MapAssignmentAsync(a.Target));

            SelectedItemAssignments = new ObservableCollection<AssignmentDisplayItem>(items);

        }

        catch (Exception ex)

        {

            DebugLog.LogError($"Failed to load endpoint security assignments: {FormatGraphError(ex)}", ex);

        }

        finally { IsLoadingDetails = false; }

    }



    private async Task LoadAdministrativeTemplateAssignmentsAsync(string templateId)

    {

        if (_administrativeTemplateService == null) return;

        IsLoadingDetails = true;

        try

        {

            var assignments = await _administrativeTemplateService.GetAssignmentsAsync(templateId);

            var items = new List<AssignmentDisplayItem>();

            foreach (var a in assignments)

                items.Add(await MapAssignmentAsync(a.Target));

            SelectedItemAssignments = new ObservableCollection<AssignmentDisplayItem>(items);

        }

        catch (Exception ex)

        {

            DebugLog.LogError($"Failed to load administrative template assignments: {FormatGraphError(ex)}", ex);

        }

        finally { IsLoadingDetails = false; }

    }



    private async Task LoadDeviceManagementScriptAssignmentsAsync(string scriptId)

    {

        if (_deviceManagementScriptService == null) return;

        IsLoadingDetails = true;

        try

        {

            var assignments = await _deviceManagementScriptService.GetAssignmentsAsync(scriptId);

            var items = new List<AssignmentDisplayItem>();

            foreach (var a in assignments)

                items.Add(await MapAssignmentAsync(a.Target));

            SelectedItemAssignments = new ObservableCollection<AssignmentDisplayItem>(items);

        }

        catch (Exception ex)

        {

            DebugLog.LogError($"Failed to load device management script assignments: {FormatGraphError(ex)}", ex);

        }

        finally { IsLoadingDetails = false; }

    }



    private async Task LoadDeviceShellScriptAssignmentsAsync(string scriptId)

    {

        if (_deviceShellScriptService == null) return;

        IsLoadingDetails = true;

        try

        {

            var assignments = await _deviceShellScriptService.GetAssignmentsAsync(scriptId);

            var items = new List<AssignmentDisplayItem>();

            foreach (var a in assignments)

                items.Add(await MapAssignmentAsync(a.Target));

            SelectedItemAssignments = new ObservableCollection<AssignmentDisplayItem>(items);

        }

        catch (Exception ex)

        {

            DebugLog.LogError($"Failed to load device shell script assignments: {FormatGraphError(ex)}", ex);

        }

        finally { IsLoadingDetails = false; }

    }



    private async Task<AssignmentDisplayItem> MapAssignmentAsync(DeviceAndAppManagementAssignmentTarget? target)

    {

        switch (target)

        {

            case AllDevicesAssignmentTarget:

                return new AssignmentDisplayItem { Target = "All Devices", TargetKind = "Include" };

            case AllLicensedUsersAssignmentTarget:

                return new AssignmentDisplayItem { Target = "All Users", TargetKind = "Include" };

            case ExclusionGroupAssignmentTarget excl:

                return new AssignmentDisplayItem

                {

                    Target = await ResolveGroupNameAsync(excl.GroupId),

                    GroupId = excl.GroupId ?? "",

                    TargetKind = "Exclude"

                };

            case GroupAssignmentTarget grp:

                return new AssignmentDisplayItem

                {

                    Target = await ResolveGroupNameAsync(grp.GroupId),

                    GroupId = grp.GroupId ?? "",

                    TargetKind = "Include"

                };

            default:

                return new AssignmentDisplayItem { Target = "Unknown", TargetKind = "Include" };

        }

    }



    private readonly Dictionary<string, string> _groupNameCache = new();



    private async Task<string> ResolveGroupNameAsync(string? groupId)

    {

        if (string.IsNullOrEmpty(groupId)) return "Unknown Group";

        if (!Guid.TryParse(groupId, out _)) return groupId; // Not a valid GUID — skip Graph call

        if (_groupNameCache.TryGetValue(groupId, out var cached)) return cached;



        try

        {

            if (_graphClient != null)

            {

                // Use $filter to avoid 404 ODataError for deleted/inaccessible groups

                var response = await _graphClient.Groups

                    .GetAsync(req =>

                    {

                        req.QueryParameters.Filter = $"id eq '{groupId}'";

                        req.QueryParameters.Select = new[] { "displayName" };

                        req.QueryParameters.Top = 1;

                    });

                var group = response?.Value?.FirstOrDefault();

                var name = group?.DisplayName ?? groupId;

                _groupNameCache[groupId] = name;

                return name;

            }

        }

        catch (Exception ex)

        {

            DebugLog.Log("Graph", $"Could not resolve group {groupId}: {FormatGraphError(ex)}");

        }



        _groupNameCache[groupId] = groupId;

        return groupId;

    }

}

