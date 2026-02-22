using System;

using System.Collections.Generic;

using System.Collections.ObjectModel;

using System.Globalization;

using System.IO;

using System.Linq;

using System.Text;

using System.Text.Json;

using System.Threading;

using System.Threading.Tasks;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using CommunityToolkit.Mvvm.Input;

using Intune.Commander.Core.Auth;

using Intune.Commander.Core.Models;

using Intune.Commander.Core.Services;

using Microsoft.Graph.Beta;

using Microsoft.Graph.Beta.Models;

using Microsoft.Graph.Beta.Models.ODataErrors;



namespace Intune.Commander.Desktop.ViewModels;



public partial class MainWindowViewModel : ViewModelBase

{

    private readonly ProfileService _profileService;

    private readonly IntuneGraphClientFactory _graphClientFactory;

    private readonly IExportService _exportService;

    private readonly ICacheService _cacheService;



    // Cache data-type keys

    private const string CacheKeyDeviceConfigs = "DeviceConfigurations";

    private const string CacheKeyCompliancePolicies = "CompliancePolicies";

    private const string CacheKeyApplications = "Applications";

    private const string CacheKeySettingsCatalog = "SettingsCatalog";

    private const string CacheKeyConditionalAccess = "ConditionalAccessPolicies";

    private const string CacheKeyAssignmentFilters = "AssignmentFilters";

    private const string CacheKeyPolicySets = "PolicySets";

    private const string CacheKeyEndpointSecurity = "EndpointSecurityIntents";

    private const string CacheKeyAdministrativeTemplates = "AdministrativeTemplates";

    private const string CacheKeyEnrollmentConfigurations = "EnrollmentConfigurations";

    private const string CacheKeyAppProtectionPolicies = "AppProtectionPolicies";

    private const string CacheKeyManagedDeviceAppConfigurations = "ManagedDeviceAppConfigurations";

    private const string CacheKeyTargetedManagedAppConfigurations = "TargetedManagedAppConfigurations";

    private const string CacheKeyTermsAndConditions = "TermsAndConditions";

    private const string CacheKeyScopeTags = "ScopeTags";

    private const string CacheKeyRoleDefinitions = "RoleDefinitions";

    private const string CacheKeyIntuneBrandingProfiles = "IntuneBrandingProfiles";

    private const string CacheKeyAzureBrandingLocalizations = "AzureBrandingLocalizations";

    private const string CacheKeyAutopilotProfiles = "AutopilotProfiles";

    private const string CacheKeyDeviceHealthScripts = "DeviceHealthScripts";

    private const string CacheKeyMacCustomAttributes = "MacCustomAttributes";

    private const string CacheKeyFeatureUpdateProfiles = "FeatureUpdateProfiles";

    private const string CacheKeyNamedLocations = "NamedLocations";

    private const string CacheKeyAuthenticationStrengths = "AuthenticationStrengths";

    private const string CacheKeyAuthenticationContexts = "AuthenticationContexts";

    private const string CacheKeyTermsOfUseAgreements = "TermsOfUseAgreements";

    private const string CacheKeyAppAssignments = "AppAssignments";

    private const string CacheKeyDynamicGroups = "DynamicGroups";

    private const string CacheKeyAssignedGroups = "AssignedGroups";

    private const string CacheKeyDeviceManagementScripts = "DeviceManagementScripts";

    private const string CacheKeyDeviceShellScripts = "DeviceShellScripts";

    private const string CacheKeyComplianceScripts = "ComplianceScripts";

    private const string CacheKeyUsers = "Users";



    private GraphServiceClient? _graphClient;

    private IConfigurationProfileService? _configProfileService;

    private IImportService? _importService;

    private ICompliancePolicyService? _compliancePolicyService;

    private IApplicationService? _applicationService;

    private IGroupService? _groupService;

    private ISettingsCatalogService? _settingsCatalogService;

    private IConditionalAccessPolicyService? _conditionalAccessPolicyService;

    private IAssignmentFilterService? _assignmentFilterService;

    private IPolicySetService? _policySetService;

    private IEndpointSecurityService? _endpointSecurityService;

    private IAdministrativeTemplateService? _administrativeTemplateService;

    private IEnrollmentConfigurationService? _enrollmentConfigurationService;

    private IAppProtectionPolicyService? _appProtectionPolicyService;

    private IManagedAppConfigurationService? _managedAppConfigurationService;

    private ITermsAndConditionsService? _termsAndConditionsService;

    private IScopeTagService? _scopeTagService;

    private IRoleDefinitionService? _roleDefinitionService;

    private IIntuneBrandingService? _intuneBrandingService;
    private IAzureBrandingService? _azureBrandingService;
    private IAutopilotService? _autopilotService;

    private IDeviceHealthScriptService? _deviceHealthScriptService;

    private IMacCustomAttributeService? _macCustomAttributeService;

    private IFeatureUpdateProfileService? _featureUpdateProfileService;

    private INamedLocationService? _namedLocationService;

    private IAuthenticationStrengthService? _authenticationStrengthService;

    private IAuthenticationContextService? _authenticationContextService;

    private ITermsOfUseService? _termsOfUseService;

    private IDeviceManagementScriptService? _deviceManagementScriptService;
    private IDeviceShellScriptService? _deviceShellScriptService;
    private IComplianceScriptService? _complianceScriptService;
    private IConditionalAccessPptExportService? _conditionalAccessPptExportService;
    private IUserService? _userService;



    [ObservableProperty]

    private ViewModelBase? _currentView;



    [ObservableProperty]

    private string _windowTitle = "Intune Commander";



    [ObservableProperty]

    private TenantProfile? _activeProfile;



    [ObservableProperty]

    private bool _isConnected;



    [ObservableProperty]

    private string _statusText = "Not connected";



    [ObservableProperty]

    private string _cacheStatusText = "";



    // --- Download All to Cache ---

    [ObservableProperty]
    private bool _isDownloadingAll;

    [ObservableProperty]
    private string _downloadProgress = "";

    [ObservableProperty]
    private double _downloadProgressPercent;

    private CancellationTokenSource? _downloadAllCts;



    partial void OnStatusTextChanged(string value)

    {

        DebugLog.Log("Status", value);

    }



    // --- Navigation ---

    [ObservableProperty]

    private NavCategory? _selectedCategory;





    // --- Overview dashboard ---

    public OverviewViewModel Overview { get; } = new();



    // --- Device Configurations ---

    [ObservableProperty]

    private ObservableCollection<DeviceConfiguration> _deviceConfigurations = [];



    [ObservableProperty]

    private DeviceConfiguration? _selectedConfiguration;



    // --- Compliance Policies ---

    [ObservableProperty]

    private ObservableCollection<DeviceCompliancePolicy> _compliancePolicies = [];



    [ObservableProperty]

    private DeviceCompliancePolicy? _selectedCompliancePolicy;



    // --- Applications ---

    [ObservableProperty]

    private ObservableCollection<MobileApp> _applications = [];



    [ObservableProperty]

    private MobileApp? _selectedApplication;



    // --- Settings Catalog ---

    [ObservableProperty]

    private ObservableCollection<DeviceManagementConfigurationPolicy> _settingsCatalogPolicies = [];



    [ObservableProperty]

    private DeviceManagementConfigurationPolicy? _selectedSettingsCatalogPolicy;



    // --- Endpoint Security ---

    [ObservableProperty]

    private ObservableCollection<DeviceManagementIntent> _endpointSecurityIntents = [];



    [ObservableProperty]

    private DeviceManagementIntent? _selectedEndpointSecurityIntent;



    private bool _endpointSecurityLoaded;



    // --- Administrative Templates ---

    [ObservableProperty]

    private ObservableCollection<GroupPolicyConfiguration> _administrativeTemplates = [];



    [ObservableProperty]

    private GroupPolicyConfiguration? _selectedAdministrativeTemplate;



    private bool _administrativeTemplatesLoaded;



    // --- Enrollment Configurations ---

    [ObservableProperty]

    private ObservableCollection<DeviceEnrollmentConfiguration> _enrollmentConfigurations = [];



    [ObservableProperty]

    private DeviceEnrollmentConfiguration? _selectedEnrollmentConfiguration;



    private bool _enrollmentConfigurationsLoaded;



    // --- App Protection Policies ---

    [ObservableProperty]

    private ObservableCollection<ManagedAppPolicy> _appProtectionPolicies = [];



    [ObservableProperty]

    private ManagedAppPolicy? _selectedAppProtectionPolicy;



    private bool _appProtectionPoliciesLoaded;



    // --- Managed Device App Configurations ---

    [ObservableProperty]

    private ObservableCollection<ManagedDeviceMobileAppConfiguration> _managedDeviceAppConfigurations = [];



    [ObservableProperty]

    private ManagedDeviceMobileAppConfiguration? _selectedManagedDeviceAppConfiguration;



    private bool _managedDeviceAppConfigurationsLoaded;



    // --- Targeted Managed App Configurations ---

    [ObservableProperty]

    private ObservableCollection<TargetedManagedAppConfiguration> _targetedManagedAppConfigurations = [];



    [ObservableProperty]

    private TargetedManagedAppConfiguration? _selectedTargetedManagedAppConfiguration;



    private bool _targetedManagedAppConfigurationsLoaded;



    // --- Terms and Conditions ---

    [ObservableProperty]

    private ObservableCollection<TermsAndConditions> _termsAndConditionsCollection = [];



    [ObservableProperty]

    private TermsAndConditions? _selectedTermsAndConditions;



    private bool _termsAndConditionsLoaded;



    // --- Scope Tags ---

    [ObservableProperty]

    private ObservableCollection<RoleScopeTag> _scopeTags = [];



    [ObservableProperty]

    private RoleScopeTag? _selectedScopeTag;



    private bool _scopeTagsLoaded;



    // --- Role Definitions ---

    [ObservableProperty]

    private ObservableCollection<RoleDefinition> _roleDefinitions = [];



    [ObservableProperty]

    private RoleDefinition? _selectedRoleDefinition;



    private bool _roleDefinitionsLoaded;



    // --- Intune Branding ---

    [ObservableProperty]

    private ObservableCollection<IntuneBrandingProfile> _intuneBrandingProfiles = [];



    [ObservableProperty]

    private IntuneBrandingProfile? _selectedIntuneBrandingProfile;



    private bool _intuneBrandingProfilesLoaded;



    // --- Azure Branding ---

    [ObservableProperty]

    private ObservableCollection<OrganizationalBrandingLocalization> _azureBrandingLocalizations = [];



    [ObservableProperty]

    private OrganizationalBrandingLocalization? _selectedAzureBrandingLocalization;



    private bool _azureBrandingLocalizationsLoaded;



    // --- Autopilot Profiles ---

    [ObservableProperty]

    private ObservableCollection<WindowsAutopilotDeploymentProfile> _autopilotProfiles = [];



    [ObservableProperty]

    private WindowsAutopilotDeploymentProfile? _selectedAutopilotProfile;



    private bool _autopilotProfilesLoaded;



    // --- Device Health Scripts ---

    [ObservableProperty]

    private ObservableCollection<DeviceHealthScript> _deviceHealthScripts = [];



    [ObservableProperty]

    private DeviceHealthScript? _selectedDeviceHealthScript;



    private bool _deviceHealthScriptsLoaded;



    // --- Mac Custom Attributes ---

    [ObservableProperty]

    private ObservableCollection<DeviceCustomAttributeShellScript> _macCustomAttributes = [];



    [ObservableProperty]

    private DeviceCustomAttributeShellScript? _selectedMacCustomAttribute;



    private bool _macCustomAttributesLoaded;



    // --- Feature Update Profiles ---

    [ObservableProperty]

    private ObservableCollection<WindowsFeatureUpdateProfile> _featureUpdateProfiles = [];



    [ObservableProperty]

    private WindowsFeatureUpdateProfile? _selectedFeatureUpdateProfile;



    private bool _featureUpdateProfilesLoaded;

    // --- Device Management Scripts (PowerShell) ---
    [ObservableProperty]
    private ObservableCollection<DeviceManagementScript> _deviceManagementScripts = [];

    [ObservableProperty]
    private DeviceManagementScript? _selectedDeviceManagementScript;

    private bool _deviceManagementScriptsLoaded;

    // --- Device Shell Scripts (macOS/Linux) ---
    [ObservableProperty]
    private ObservableCollection<DeviceShellScript> _deviceShellScripts = [];

    [ObservableProperty]
    private DeviceShellScript? _selectedDeviceShellScript;

    private bool _deviceShellScriptsLoaded;

    // --- Compliance Scripts ---
    [ObservableProperty]
    private ObservableCollection<DeviceComplianceScript> _complianceScripts = [];

    [ObservableProperty]
    private DeviceComplianceScript? _selectedComplianceScript;

    private bool _complianceScriptsLoaded;

    // --- Named Locations ---

    [ObservableProperty]

    private ObservableCollection<NamedLocation> _namedLocations = [];



    [ObservableProperty]

    private NamedLocation? _selectedNamedLocation;



    private bool _namedLocationsLoaded;



    // --- Authentication Strengths ---

    [ObservableProperty]

    private ObservableCollection<AuthenticationStrengthPolicy> _authenticationStrengthPolicies = [];



    [ObservableProperty]

    private AuthenticationStrengthPolicy? _selectedAuthenticationStrengthPolicy;



    private bool _authenticationStrengthPoliciesLoaded;



    // --- Authentication Contexts ---

    [ObservableProperty]

    private ObservableCollection<AuthenticationContextClassReference> _authenticationContextClassReferences = [];



    [ObservableProperty]

    private AuthenticationContextClassReference? _selectedAuthenticationContextClassReference;



    private bool _authenticationContextClassReferencesLoaded;



    // --- Terms of Use ---

    [ObservableProperty]

    private ObservableCollection<Agreement> _termsOfUseAgreements = [];



    [ObservableProperty]

    private Agreement? _selectedTermsOfUseAgreement;



    private bool _termsOfUseAgreementsLoaded;



    // --- Conditional Access ---

    [ObservableProperty]

    private ObservableCollection<ConditionalAccessPolicy> _conditionalAccessPolicies = [];



    [ObservableProperty]

    private ConditionalAccessPolicy? _selectedConditionalAccessPolicy;



    private bool _conditionalAccessLoaded;



    // --- Assignment Filters ---

    [ObservableProperty]

    private ObservableCollection<DeviceAndAppManagementAssignmentFilter> _assignmentFilters = [];



    [ObservableProperty]

    private DeviceAndAppManagementAssignmentFilter? _selectedAssignmentFilter;



    private bool _assignmentFiltersLoaded;



    // --- Policy Sets ---

    [ObservableProperty]

    private ObservableCollection<PolicySet> _policySets = [];



    [ObservableProperty]

    private PolicySet? _selectedPolicySet;



    private bool _policySetsLoaded;



    // --- Application Assignments (flattened view) ---

    [ObservableProperty]

    private ObservableCollection<AppAssignmentRow> _appAssignmentRows = [];



    [ObservableProperty]

    private AppAssignmentRow? _selectedAppAssignmentRow;



    private bool _appAssignmentsLoaded;



    // --- Dynamic Groups ---

    [ObservableProperty]

    private ObservableCollection<GroupRow> _dynamicGroupRows = [];



    [ObservableProperty]

    private GroupRow? _selectedDynamicGroupRow;



    private bool _dynamicGroupsLoaded;



    // --- Assigned Groups ---

    [ObservableProperty]

    private ObservableCollection<GroupRow> _assignedGroupRows = [];



    [ObservableProperty]

    private GroupRow? _selectedAssignedGroupRow;



    private bool _assignedGroupsLoaded;



    // --- Detail pane ---

    [ObservableProperty]

    private ObservableCollection<AssignmentDisplayItem> _selectedItemAssignments = [];



    [ObservableProperty]

    private ObservableCollection<GroupMemberItem> _selectedGroupMembers = [];



    [ObservableProperty]

    private bool _isLoadingGroupMembers;



    [ObservableProperty]

    private string _selectedItemTypeName = "";



    [ObservableProperty]

    private string _selectedItemPlatform = "";



    [ObservableProperty]

    private bool _isLoadingDetails;



    /// <summary>

    /// Raised when the user clicks "Copy Details". The view handles clipboard access.

    /// </summary>

    public event Action<string>? CopyDetailsRequested;

    public event Action<string, string>? ViewRawJsonRequested;





    // --- Configurable columns per category ---

    public ObservableCollection<DataGridColumnConfig> DeviceConfigColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "Type", BindingPath = "Computed:ODataType", Width = 180, IsVisible = true },

        new() { Header = "Platform", BindingPath = "Computed:Platform", Width = 100, IsVisible = true },

        new() { Header = "Description", BindingPath = "Description", Width = 200, IsVisible = false },

        new() { Header = "Created", BindingPath = "CreatedDateTime", Width = 150, IsVisible = false },

        new() { Header = "Last Modified", BindingPath = "LastModifiedDateTime", Width = 150, IsVisible = true },

        new() { Header = "Version", BindingPath = "Version", Width = 80, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> CompliancePolicyColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "Type", BindingPath = "Computed:ODataType", Width = 180, IsVisible = true },

        new() { Header = "Platform", BindingPath = "Computed:Platform", Width = 100, IsVisible = true },

        new() { Header = "Description", BindingPath = "Description", Width = 200, IsVisible = false },

        new() { Header = "Created", BindingPath = "CreatedDateTime", Width = 150, IsVisible = false },

        new() { Header = "Last Modified", BindingPath = "LastModifiedDateTime", Width = 150, IsVisible = true },

        new() { Header = "Version", BindingPath = "Version", Width = 80, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> ApplicationColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "App Type", BindingPath = "Computed:ODataType", Width = 180, IsVisible = true },

        new() { Header = "Platform", BindingPath = "Computed:Platform", Width = 120, IsVisible = true },

        new() { Header = "Publisher", BindingPath = "Publisher", Width = 150, IsVisible = true },

        new() { Header = "Description", BindingPath = "Description", Width = 200, IsVisible = false },

        new() { Header = "Owner", BindingPath = "Owner", Width = 120, IsVisible = false },

        new() { Header = "Developer", BindingPath = "Developer", Width = 120, IsVisible = false },

        new() { Header = "Created", BindingPath = "CreatedDateTime", Width = 150, IsVisible = false },

        new() { Header = "Last Modified", BindingPath = "LastModifiedDateTime", Width = 150, IsVisible = false },

        new() { Header = "Publishing State", BindingPath = "PublishingState", Width = 120, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> SettingsCatalogColumns { get; } =

    [

        new() { Header = "Name", BindingPath = "Name", IsStar = true, IsVisible = true },

        new() { Header = "Platforms", BindingPath = "Platforms", Width = 120, IsVisible = true },

        new() { Header = "Technologies", BindingPath = "Technologies", Width = 140, IsVisible = true },

        new() { Header = "Description", BindingPath = "Description", Width = 200, IsVisible = false },

        new() { Header = "Is Assigned", BindingPath = "IsAssigned", Width = 90, IsVisible = true },

        new() { Header = "Created", BindingPath = "CreatedDateTime", Width = 150, IsVisible = false },

        new() { Header = "Last Modified", BindingPath = "LastModifiedDateTime", Width = 150, IsVisible = true },

        new() { Header = "Role Scope Tags", BindingPath = "Computed:RoleScopeTags", Width = 120, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> EndpointSecurityColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "Description", BindingPath = "Description", Width = 260, IsVisible = true },

        new() { Header = "Is Assigned", BindingPath = "IsAssigned", Width = 90, IsVisible = true },

        new() { Header = "Last Modified", BindingPath = "LastModifiedDateTime", Width = 150, IsVisible = true },

        new() { Header = "ID", BindingPath = "Id", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> AdministrativeTemplateColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "Description", BindingPath = "Description", Width = 260, IsVisible = true },

        new() { Header = "Ingestion Type", BindingPath = "PolicyConfigurationIngestionType", Width = 140, IsVisible = true },

        new() { Header = "Last Modified", BindingPath = "LastModifiedDateTime", Width = 150, IsVisible = true },

        new() { Header = "ID", BindingPath = "Id", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> EnrollmentConfigurationColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "Description", BindingPath = "Description", Width = 260, IsVisible = true },

        new() { Header = "Type", BindingPath = "Computed:ODataType", Width = 200, IsVisible = true },

        new() { Header = "Priority", BindingPath = "Priority", Width = 90, IsVisible = true },

        new() { Header = "ID", BindingPath = "Id", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> AppProtectionPolicyColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "Description", BindingPath = "Description", Width = 260, IsVisible = true },

        new() { Header = "Type", BindingPath = "Computed:ODataType", Width = 220, IsVisible = true },

        new() { Header = "Version", BindingPath = "Version", Width = 90, IsVisible = true },

        new() { Header = "Last Modified", BindingPath = "LastModifiedDateTime", Width = 150, IsVisible = true },

        new() { Header = "ID", BindingPath = "Id", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> ManagedDeviceAppConfigurationColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "Description", BindingPath = "Description", Width = 260, IsVisible = true },

        new() { Header = "Type", BindingPath = "Computed:ODataType", Width = 220, IsVisible = true },

        new() { Header = "Version", BindingPath = "Version", Width = 90, IsVisible = true },

        new() { Header = "Last Modified", BindingPath = "LastModifiedDateTime", Width = 150, IsVisible = true },

        new() { Header = "ID", BindingPath = "Id", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> TargetedManagedAppConfigurationColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "Description", BindingPath = "Description", Width = 260, IsVisible = true },

        new() { Header = "Type", BindingPath = "Computed:ODataType", Width = 220, IsVisible = true },

        new() { Header = "Version", BindingPath = "Version", Width = 90, IsVisible = true },

        new() { Header = "Last Modified", BindingPath = "LastModifiedDateTime", Width = 150, IsVisible = true },

        new() { Header = "ID", BindingPath = "Id", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> TermsAndConditionsColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "Description", BindingPath = "Description", Width = 260, IsVisible = true },

        new() { Header = "Version", BindingPath = "Version", Width = 90, IsVisible = true },

        new() { Header = "Created", BindingPath = "CreatedDateTime", Width = 150, IsVisible = true },

        new() { Header = "Last Modified", BindingPath = "LastModifiedDateTime", Width = 150, IsVisible = true },

        new() { Header = "ID", BindingPath = "Id", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> ScopeTagColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "Description", BindingPath = "Description", Width = 260, IsVisible = true },

        new() { Header = "Built In", BindingPath = "IsBuiltIn", Width = 90, IsVisible = true },

        new() { Header = "ID", BindingPath = "Id", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> RoleDefinitionColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "Description", BindingPath = "Description", Width = 260, IsVisible = true },

        new() { Header = "Built In", BindingPath = "IsBuiltIn", Width = 90, IsVisible = true },

        new() { Header = "Built In Role Definition", BindingPath = "IsBuiltInRoleDefinition", Width = 180, IsVisible = true },

        new() { Header = "ID", BindingPath = "Id", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> IntuneBrandingColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "Profile Name", BindingPath = "ProfileName", IsStar = true, IsVisible = true },

        new() { Header = "Show Logo", BindingPath = "ShowLogo", Width = 110, IsVisible = true },

        new() { Header = "ID", BindingPath = "Id", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> AzureBrandingColumns { get; } =

    [

        new() { Header = "Localization ID", BindingPath = "Id", Width = 140, IsVisible = true },

        new() { Header = "Sign-in Text", BindingPath = "SignInPageText", IsStar = true, IsVisible = true },

        new() { Header = "Username Hint", BindingPath = "UsernameHintText", Width = 220, IsVisible = true },

        new() { Header = "Banner Logo URL", BindingPath = "BannerLogoRelativeUrl", Width = 220, IsVisible = false },

        new() { Header = "ID", BindingPath = "Id", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> ConditionalAccessColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "State", BindingPath = "State", Width = 120, IsVisible = true },

        new() { Header = "ID", BindingPath = "Id", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> AssignmentFilterColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "Platform", BindingPath = "Platform", Width = 120, IsVisible = true },

        new() { Header = "Type", BindingPath = "AssignmentFilterManagementType", Width = 140, IsVisible = true },

        new() { Header = "ID", BindingPath = "Id", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> PolicySetColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "Description", BindingPath = "Description", Width = 240, IsVisible = true },

        new() { Header = "ID", BindingPath = "Id", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> AutopilotProfileColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "Description", BindingPath = "Description", Width = 260, IsVisible = true },

        new() { Header = "Last Modified", BindingPath = "LastModifiedDateTime", Width = 150, IsVisible = true },

        new() { Header = "ID", BindingPath = "Id", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> DeviceHealthScriptColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "Description", BindingPath = "Description", Width = 260, IsVisible = true },

        new() { Header = "Created", BindingPath = "CreatedDateTime", Width = 150, IsVisible = true },

        new() { Header = "Last Modified", BindingPath = "LastModifiedDateTime", Width = 150, IsVisible = true },

        new() { Header = "ID", BindingPath = "Id", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> MacCustomAttributeColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "Description", BindingPath = "Description", Width = 260, IsVisible = true },

        new() { Header = "Created", BindingPath = "CreatedDateTime", Width = 150, IsVisible = true },

        new() { Header = "Last Modified", BindingPath = "LastModifiedDateTime", Width = 150, IsVisible = true },

        new() { Header = "ID", BindingPath = "Id", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> FeatureUpdateProfileColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "Description", BindingPath = "Description", Width = 260, IsVisible = true },

        new() { Header = "Last Modified", BindingPath = "LastModifiedDateTime", Width = 150, IsVisible = true },

        new() { Header = "ID", BindingPath = "Id", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> DeviceManagementScriptColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "Description", BindingPath = "Description", Width = 260, IsVisible = true },

        new() { Header = "File Name", BindingPath = "FileName", Width = 180, IsVisible = true },

        new() { Header = "Run As Account", BindingPath = "RunAsAccount", Width = 120, IsVisible = true },

        new() { Header = "Created", BindingPath = "CreatedDateTime", Width = 150, IsVisible = false },

        new() { Header = "Last Modified", BindingPath = "LastModifiedDateTime", Width = 150, IsVisible = true },

        new() { Header = "ID", BindingPath = "Id", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> DeviceShellScriptColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "Description", BindingPath = "Description", Width = 260, IsVisible = true },

        new() { Header = "File Name", BindingPath = "FileName", Width = 180, IsVisible = true },

        new() { Header = "Run As Account", BindingPath = "RunAsAccount", Width = 120, IsVisible = true },

        new() { Header = "Created", BindingPath = "CreatedDateTime", Width = 150, IsVisible = false },

        new() { Header = "Last Modified", BindingPath = "LastModifiedDateTime", Width = 150, IsVisible = true },

        new() { Header = "ID", BindingPath = "Id", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> ComplianceScriptColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "Description", BindingPath = "Description", Width = 260, IsVisible = true },

        new() { Header = "Publisher", BindingPath = "Publisher", Width = 150, IsVisible = true },

        new() { Header = "Created", BindingPath = "CreatedDateTime", Width = 150, IsVisible = false },

        new() { Header = "Last Modified", BindingPath = "LastModifiedDateTime", Width = 150, IsVisible = true },

        new() { Header = "ID", BindingPath = "Id", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> NamedLocationColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "Type", BindingPath = "Computed:ODataType", Width = 200, IsVisible = true },

        new() { Header = "Modified", BindingPath = "ModifiedDateTime", Width = 150, IsVisible = true },

        new() { Header = "ID", BindingPath = "Id", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> AuthenticationStrengthColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "Description", BindingPath = "Description", Width = 260, IsVisible = true },

        new() { Header = "Policy Type", BindingPath = "PolicyType", Width = 120, IsVisible = true },

        new() { Header = "ID", BindingPath = "Id", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> AuthenticationContextColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "Description", BindingPath = "Description", Width = 260, IsVisible = true },

        new() { Header = "Is Available", BindingPath = "IsAvailable", Width = 100, IsVisible = true },

        new() { Header = "ID", BindingPath = "Id", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> TermsOfUseColumns { get; } =

    [

        new() { Header = "Display Name", BindingPath = "DisplayName", IsStar = true, IsVisible = true },

        new() { Header = "File Name", BindingPath = "FileName", Width = 220, IsVisible = true },

        new() { Header = "Created", BindingPath = "CreatedDateTime", Width = 150, IsVisible = true },

        new() { Header = "ID", BindingPath = "Id", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> DynamicGroupColumns { get; } =

    [

        new() { Header = "Group Name", BindingPath = "GroupName", IsStar = true, IsVisible = true },

        new() { Header = "Description", BindingPath = "Description", Width = 200, IsVisible = false },

        new() { Header = "Membership Rule", BindingPath = "MembershipRule", Width = 280, IsVisible = true },

        new() { Header = "Processing State", BindingPath = "ProcessingState", Width = 120, IsVisible = true },

        new() { Header = "Group Type", BindingPath = "GroupType", Width = 130, IsVisible = true },

        new() { Header = "Total Members", BindingPath = "TotalMembers", Width = 100, IsVisible = true },

        new() { Header = "Users", BindingPath = "Users", Width = 70, IsVisible = true },

        new() { Header = "Devices", BindingPath = "Devices", Width = 70, IsVisible = true },

        new() { Header = "Nested Groups", BindingPath = "NestedGroups", Width = 100, IsVisible = false },

        new() { Header = "Security Enabled", BindingPath = "SecurityEnabled", Width = 110, IsVisible = false },

        new() { Header = "Mail Enabled", BindingPath = "MailEnabled", Width = 100, IsVisible = false },

        new() { Header = "Created Date", BindingPath = "CreatedDate", Width = 150, IsVisible = false },

        new() { Header = "Group ID", BindingPath = "GroupId", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> AssignedGroupColumns { get; } =

    [

        new() { Header = "Group Name", BindingPath = "GroupName", IsStar = true, IsVisible = true },

        new() { Header = "Description", BindingPath = "Description", Width = 200, IsVisible = false },

        new() { Header = "Group Type", BindingPath = "GroupType", Width = 130, IsVisible = true },

        new() { Header = "Total Members", BindingPath = "TotalMembers", Width = 100, IsVisible = true },

        new() { Header = "Users", BindingPath = "Users", Width = 70, IsVisible = true },

        new() { Header = "Devices", BindingPath = "Devices", Width = 70, IsVisible = true },

        new() { Header = "Nested Groups", BindingPath = "NestedGroups", Width = 100, IsVisible = false },

        new() { Header = "Security Enabled", BindingPath = "SecurityEnabled", Width = 110, IsVisible = false },

        new() { Header = "Mail Enabled", BindingPath = "MailEnabled", Width = 100, IsVisible = false },

        new() { Header = "Created Date", BindingPath = "CreatedDate", Width = 150, IsVisible = false },

        new() { Header = "Group ID", BindingPath = "GroupId", Width = 280, IsVisible = false }

    ];



    public ObservableCollection<DataGridColumnConfig> AppAssignmentColumns { get; } =

    [

        new() { Header = "App Name", BindingPath = "AppName", IsStar = true, IsVisible = true },

        new() { Header = "App Type", BindingPath = "AppType", Width = 140, IsVisible = true },

        new() { Header = "Platform", BindingPath = "Platform", Width = 90, IsVisible = true },

        new() { Header = "Assignment Type", BindingPath = "AssignmentType", Width = 110, IsVisible = true },

        new() { Header = "Target Name", BindingPath = "TargetName", Width = 180, IsVisible = true },

        new() { Header = "Install Intent", BindingPath = "InstallIntent", Width = 90, IsVisible = true },

        new() { Header = "Is Exclusion", BindingPath = "IsExclusion", Width = 80, IsVisible = true },

        new() { Header = "Publisher", BindingPath = "Publisher", Width = 140, IsVisible = false },

        new() { Header = "Version", BindingPath = "Version", Width = 100, IsVisible = false },

        new() { Header = "Assignment Settings", BindingPath = "AssignmentSettings", Width = 260, IsVisible = false },

        new() { Header = "Target Group ID", BindingPath = "TargetGroupId", Width = 280, IsVisible = false },

        new() { Header = "Description", BindingPath = "Description", Width = 250, IsVisible = false },

        new() { Header = "Bundle ID", BindingPath = "BundleId", Width = 160, IsVisible = false },

        new() { Header = "Package ID", BindingPath = "PackageId", Width = 160, IsVisible = false },

        new() { Header = "Is Featured", BindingPath = "IsFeatured", Width = 80, IsVisible = false },

        new() { Header = "Created Date", BindingPath = "CreatedDate", Width = 150, IsVisible = false },

        new() { Header = "Last Modified", BindingPath = "LastModified", Width = 150, IsVisible = false },

        new() { Header = "App Store URL", BindingPath = "AppStoreUrl", Width = 200, IsVisible = false },

        new() { Header = "Privacy URL", BindingPath = "PrivacyUrl", Width = 200, IsVisible = false },

        new() { Header = "Information URL", BindingPath = "InformationUrl", Width = 200, IsVisible = false },

        new() { Header = "Min OS Version", BindingPath = "MinimumOsVersion", Width = 120, IsVisible = false },

        new() { Header = "Min Disk (MB)", BindingPath = "MinimumFreeDiskSpaceMB", Width = 100, IsVisible = false },

        new() { Header = "Min Memory (MB)", BindingPath = "MinimumMemoryMB", Width = 100, IsVisible = false },

        new() { Header = "Min Processors", BindingPath = "MinimumProcessors", Width = 100, IsVisible = false },

        new() { Header = "Categories", BindingPath = "Categories", Width = 180, IsVisible = false },

        new() { Header = "Notes", BindingPath = "Notes", Width = 200, IsVisible = false }

    ];

    // --- Profile switcher ---

    [ObservableProperty]

    private TenantProfile? _selectedSwitchProfile;



    public ObservableCollection<TenantProfile> SwitcherProfiles { get; } = [];



    public event Func<TenantProfile, Task<bool>>? SwitchProfileRequested;



    public LoginViewModel LoginViewModel { get; }



    public MainWindowViewModel(

        ProfileService profileService,

        IntuneGraphClientFactory graphClientFactory,

        IExportService exportService,

        ICacheService cacheService)

    {

        _profileService = profileService;

        _graphClientFactory = graphClientFactory;

        _exportService = exportService;

        _cacheService = cacheService;



        LoginViewModel = new LoginViewModel(profileService, graphClientFactory);

        LoginViewModel.LoginSucceeded += OnLoginSucceeded;



        CurrentView = LoginViewModel;

        EnsureNavCategories();
        Overview.NavigateToCategory = ActivateCategoryByName;
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsBusy))
                OnPropertyChanged(nameof(IsCurrentCategoryEmpty));
        };



        DebugLog.Log("App", "Intune Commander started");

        DebugLog.Log("App", $"Nav categories loaded ({NavCategories.Count}): {string.Join(", ", NavCategories.Select(c => c.Name))}");



        // Load profiles asynchronously — never block the UI thread

        _ = LoadProfilesAsync();

    }



    // Design-time constructor

    public MainWindowViewModel()

    {

        _profileService = null!;

        _graphClientFactory = null!;

        _exportService = null!;

        _cacheService = null!;

        LoginViewModel = null!;

    }



    // --- Navigation ---





    /// <summary>

    /// Whether a single item is currently selected and can be refreshed.

    /// </summary>



    // --- Refresh (loads data for the selected category) ---



    /// <summary>

    /// Extracts a human-readable error message from an ODataError, including

    /// the response status code, error code, and inner error details.

    /// </summary>

    private static string FormatODataError(ODataError odataError)

    {

        var sb = new StringBuilder();

        sb.Append($"HTTP {odataError.ResponseStatusCode}");



        if (odataError.Error != null)

        {

            if (!string.IsNullOrEmpty(odataError.Error.Code))

                sb.Append($" [{odataError.Error.Code}]");

            if (!string.IsNullOrEmpty(odataError.Error.Message))

                sb.Append($": {odataError.Error.Message}");

        }

        else if (!string.IsNullOrEmpty(odataError.Message))

        {

            sb.Append($": {odataError.Message}");

        }



        return sb.ToString();

    }



    /// <summary>

    /// Formats any exception for display, with special handling for ODataError.

    /// </summary>

    private static string FormatGraphError(Exception ex)

    {

        return ex is ODataError odata ? FormatODataError(odata) : ex.Message;

    }





}

