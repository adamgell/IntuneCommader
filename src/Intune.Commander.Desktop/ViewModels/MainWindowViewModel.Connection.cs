using System;

using System.Collections.ObjectModel;

using System.Linq;

using System.Threading;

using System.Threading.Tasks;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.Input;

using Intune.Commander.Core.Models;

using Intune.Commander.Core.Services;

using Microsoft.Graph.Beta;

using Microsoft.Graph.Beta.Models;



namespace Intune.Commander.Desktop.ViewModels;



public partial class MainWindowViewModel : ViewModelBase

{



    private async Task LoadProfilesAsync()

    {

        try

        {

            await _profileService.LoadAsync();

            LoginViewModel.PopulateSavedProfiles();

            LoginViewModel.SelectActiveProfile();

        }

        catch (Exception ex)

        {

            SetError($"Failed to load profiles: {ex.Message}");

        }

    }





    // --- Connection ---



    private async void OnLoginSucceeded(object? sender, TenantProfile profile)

    {

        await ConnectToProfile(profile);

    }



    private async Task ConnectToProfile(TenantProfile profile)

    {

        ClearError();

        IsBusy = true;

        StatusText = $"Connecting to {profile.Name}...";

        DebugLog.Log("Auth", $"Authenticating to tenant {profile.TenantId} ({profile.Cloud}) as {profile.ClientId}");



        try

        {

            ActiveProfile = profile;

            IsConnected = true;

            WindowTitle = $"Intune Commander - {profile.Name}";

            CurrentView = null;



            _graphClient = await _graphClientFactory.CreateClientAsync(profile);

            DebugLog.Log("Auth", "Graph client created successfully");

            _configProfileService = new ConfigurationProfileService(_graphClient);

            _compliancePolicyService = new CompliancePolicyService(_graphClient);

            _applicationService = new ApplicationService(_graphClient);

            _groupService = new GroupService(_graphClient);

            _settingsCatalogService = new SettingsCatalogService(_graphClient);

            _conditionalAccessPolicyService = new ConditionalAccessPolicyService(_graphClient);

            _assignmentFilterService = new AssignmentFilterService(_graphClient);

            _policySetService = new PolicySetService(_graphClient);

            _endpointSecurityService = new EndpointSecurityService(_graphClient);

            _administrativeTemplateService = new AdministrativeTemplateService(_graphClient);

            _enrollmentConfigurationService = new EnrollmentConfigurationService(_graphClient);

            _appProtectionPolicyService = new AppProtectionPolicyService(_graphClient);

            _managedAppConfigurationService = new ManagedAppConfigurationService(_graphClient);

            _termsAndConditionsService = new TermsAndConditionsService(_graphClient);

            _scopeTagService = new ScopeTagService(_graphClient);

            _roleDefinitionService = new RoleDefinitionService(_graphClient);

            _intuneBrandingService = new IntuneBrandingService(_graphClient);

            _azureBrandingService = new AzureBrandingService(_graphClient);

            _autopilotService = new AutopilotService(_graphClient);

            _deviceHealthScriptService = new DeviceHealthScriptService(_graphClient);

            _macCustomAttributeService = new MacCustomAttributeService(_graphClient);

            _featureUpdateProfileService = new FeatureUpdateProfileService(_graphClient);

            _namedLocationService = new NamedLocationService(_graphClient);

            _authenticationStrengthService = new AuthenticationStrengthService(_graphClient);

            _authenticationContextService = new AuthenticationContextService(_graphClient);

            _termsOfUseService = new TermsOfUseService(_graphClient);

            _conditionalAccessPptExportService = new ConditionalAccessPptExportService(
                _conditionalAccessPolicyService,
                _namedLocationService,
                _authenticationStrengthService,
                _authenticationContextService,
                _applicationService);

            ExportConditionalAccessPowerPointCommand.NotifyCanExecuteChanged();

            _importService = new ImportService(

                _configProfileService,

                _compliancePolicyService,

                _endpointSecurityService,

                _administrativeTemplateService,

                _enrollmentConfigurationService,

                _appProtectionPolicyService,

                _managedAppConfigurationService,

                _termsAndConditionsService,

                _scopeTagService,

                _roleDefinitionService,

                _intuneBrandingService,

                _azureBrandingService,

                _autopilotService,

                _deviceHealthScriptService,

                _macCustomAttributeService,

                _featureUpdateProfileService,

                _namedLocationService,

                _authenticationStrengthService,

                _authenticationContextService,

                _termsOfUseService);



            RefreshSwitcherProfiles();

            SelectedSwitchProfile = profile;

            EnsureNavCategories();

            DebugLog.Log("App", $"Connected nav categories ({NavCategories.Count}): {string.Join(", ", NavCategories.Select(c => c.Name))}");



            // Default to first nav category

            SelectedCategory = NavCategories.FirstOrDefault();



            StatusText = $"Connected to {profile.Name}";

            DebugLog.Log("Auth", $"Connected to {profile.Name}");



            // Try loading cached data — if all primary types are cached, skip Graph refresh

            var cachedCount = TryLoadFromCache(profile.TenantId ?? "");

            if (cachedCount >= 26)

            {

                DebugLog.Log("Cache", "All data loaded from cache — skipping Graph refresh");

                IsBusy = false;

            }

            else

            {

                if (cachedCount > 0)

                    DebugLog.Log("Cache", $"Partial cache hit ({cachedCount}/26) — refreshing from Graph");

                await RefreshAsync(CancellationToken.None);

            }

        }

        catch (Exception ex)

        {

            DebugLog.LogError($"Connection to {profile.Name} failed: {FormatGraphError(ex)}", ex);

            SetError($"Connection failed: {FormatGraphError(ex)}");

            StatusText = "Connection failed";

            DisconnectInternal();

            CurrentView = LoginViewModel;

            LoginViewModel.PopulateSavedProfiles();

            LoginViewModel.SelectActiveProfile();

        }

        finally

        {

            IsBusy = false;

        }

    }



    private void RefreshSwitcherProfiles()

    {

        SwitcherProfiles.Clear();

        foreach (var p in _profileService.Profiles)

            SwitcherProfiles.Add(p);

    }



    partial void OnSelectedSwitchProfileChanged(TenantProfile? value)

    {

        if (value is null || !IsConnected || value.Id == ActiveProfile?.Id)

            return;



        _ = RequestSwitchAsync(value);

    }



    private async Task RequestSwitchAsync(TenantProfile target)

    {

        if (SwitchProfileRequested is not null)

        {

            var confirmed = await SwitchProfileRequested.Invoke(target);

            if (confirmed)

            {

                DisconnectInternal();

                await ConnectToProfile(target);

                target.LastUsed = DateTime.UtcNow;

                _profileService.SetActiveProfile(target.Id);

                await _profileService.SaveAsync();

            }

            else

            {

                SelectedSwitchProfile = ActiveProfile;

            }

        }

    }





    // --- Disconnect ---



    [RelayCommand]

    private void Disconnect()

    {

        DisconnectInternal();

        CurrentView = LoginViewModel;

        LoginViewModel.PopulateSavedProfiles();

        LoginViewModel.SelectActiveProfile();

    }



    private void DisconnectInternal()

    {

        IsConnected = false;

        ActiveProfile = null;

        WindowTitle = "Intune Commander";

        StatusText = "Not connected";

        SelectedCategory = null;

        DeviceConfigurations.Clear();

        SelectedConfiguration = null;

        CompliancePolicies.Clear();

        SelectedCompliancePolicy = null;

        Applications.Clear();

        SelectedApplication = null;

        AppAssignmentRows.Clear();

        SelectedAppAssignmentRow = null;

        _appAssignmentsLoaded = false;

        _conditionalAccessLoaded = false;

        _endpointSecurityLoaded = false;

        _administrativeTemplatesLoaded = false;

        _enrollmentConfigurationsLoaded = false;

        _appProtectionPoliciesLoaded = false;

        _managedDeviceAppConfigurationsLoaded = false;

        _targetedManagedAppConfigurationsLoaded = false;

        _termsAndConditionsLoaded = false;

        _scopeTagsLoaded = false;

        _roleDefinitionsLoaded = false;

        _intuneBrandingProfilesLoaded = false;

        _azureBrandingLocalizationsLoaded = false;

        _assignmentFiltersLoaded = false;

        _policySetsLoaded = false;

        _autopilotProfilesLoaded = false;

        _deviceHealthScriptsLoaded = false;

        _macCustomAttributesLoaded = false;

        _featureUpdateProfilesLoaded = false;

        _namedLocationsLoaded = false;

        _authenticationStrengthPoliciesLoaded = false;

        _authenticationContextClassReferencesLoaded = false;

        _termsOfUseAgreementsLoaded = false;

        SettingsCatalogPolicies.Clear();

        SelectedSettingsCatalogPolicy = null;

        EndpointSecurityIntents.Clear();

        SelectedEndpointSecurityIntent = null;

        AdministrativeTemplates.Clear();

        SelectedAdministrativeTemplate = null;

        EnrollmentConfigurations.Clear();

        SelectedEnrollmentConfiguration = null;

        AppProtectionPolicies.Clear();

        SelectedAppProtectionPolicy = null;

        ManagedDeviceAppConfigurations.Clear();

        SelectedManagedDeviceAppConfiguration = null;

        TargetedManagedAppConfigurations.Clear();

        SelectedTargetedManagedAppConfiguration = null;

        TermsAndConditionsCollection.Clear();

        SelectedTermsAndConditions = null;

        ScopeTags.Clear();

        SelectedScopeTag = null;

        RoleDefinitions.Clear();

        SelectedRoleDefinition = null;

        IntuneBrandingProfiles.Clear();

        SelectedIntuneBrandingProfile = null;

        AzureBrandingLocalizations.Clear();

        SelectedAzureBrandingLocalization = null;

        ConditionalAccessPolicies.Clear();

        SelectedConditionalAccessPolicy = null;

        AssignmentFilters.Clear();

        SelectedAssignmentFilter = null;

        PolicySets.Clear();

        SelectedPolicySet = null;

        AutopilotProfiles.Clear();

        SelectedAutopilotProfile = null;

        DeviceHealthScripts.Clear();

        SelectedDeviceHealthScript = null;

        MacCustomAttributes.Clear();

        SelectedMacCustomAttribute = null;

        FeatureUpdateProfiles.Clear();

        SelectedFeatureUpdateProfile = null;

        NamedLocations.Clear();

        SelectedNamedLocation = null;

        AuthenticationStrengthPolicies.Clear();

        SelectedAuthenticationStrengthPolicy = null;

        AuthenticationContextClassReferences.Clear();

        SelectedAuthenticationContextClassReference = null;

        TermsOfUseAgreements.Clear();

        SelectedTermsOfUseAgreement = null;

        DynamicGroupRows.Clear();

        SelectedDynamicGroupRow = null;

        _dynamicGroupsLoaded = false;

        AssignedGroupRows.Clear();

        SelectedAssignedGroupRow = null;

        _assignedGroupsLoaded = false;

        SelectedItemAssignments.Clear();

        SelectedItemTypeName = "";

        _graphClient = null;

        _configProfileService = null;

        _compliancePolicyService = null;

        _applicationService = null;

        _groupService = null;

        _settingsCatalogService = null;

        _conditionalAccessPolicyService = null;

        _assignmentFilterService = null;

        _policySetService = null;

        _endpointSecurityService = null;

        _administrativeTemplateService = null;

        _enrollmentConfigurationService = null;

        _appProtectionPolicyService = null;

        _managedAppConfigurationService = null;

        _termsAndConditionsService = null;

        _scopeTagService = null;

        _roleDefinitionService = null;

        _intuneBrandingService = null;

        _azureBrandingService = null;

        _autopilotService = null;

        _deviceHealthScriptService = null;

        _macCustomAttributeService = null;

        _featureUpdateProfileService = null;

        _namedLocationService = null;

        _authenticationStrengthService = null;

        _authenticationContextService = null;

        _termsOfUseService = null;

        _importService = null;

        _groupNameCache.Clear();

        CacheStatusText = "";

    }

}

