using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntuneManager.Core.Auth;
using IntuneManager.Core.Models;
using IntuneManager.Core.Services;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Graph.Beta.Models.ODataErrors;

namespace IntuneManager.Desktop.ViewModels;

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
    private const string CacheKeyAppAssignments = "AppAssignments";
    private const string CacheKeyDynamicGroups = "DynamicGroups";
    private const string CacheKeyAssignedGroups = "AssignedGroups";

    private GraphServiceClient? _graphClient;
    private IConfigurationProfileService? _configProfileService;
    private IImportService? _importService;
    private ICompliancePolicyService? _compliancePolicyService;
    private IApplicationService? _applicationService;
    private IGroupService? _groupService;
    private ISettingsCatalogService? _settingsCatalogService;

    [ObservableProperty]
    private ViewModelBase? _currentView;

    [ObservableProperty]
    private string _windowTitle = "IntuneManager";

    [ObservableProperty]
    private TenantProfile? _activeProfile;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusText = "Not connected";

    partial void OnStatusTextChanged(string value)
    {
        DebugLog.Log("Status", value);
    }

    // --- Navigation ---
    [ObservableProperty]
    private NavCategory? _selectedCategory;

    public ObservableCollection<NavCategory> NavCategories { get; } =
    [
        new NavCategory { Name = "Overview", Icon = "📊" },
        new NavCategory { Name = "Device Configurations", Icon = "⚙" },
        new NavCategory { Name = "Compliance Policies", Icon = "✓" },
        new NavCategory { Name = "Applications", Icon = "📦" },
        new NavCategory { Name = "Application Assignments", Icon = "📋" },
        new NavCategory { Name = "Settings Catalog", Icon = "⚙" },
        new NavCategory { Name = "Dynamic Groups", Icon = "🔄" },
        new NavCategory { Name = "Assigned Groups", Icon = "👥" }
    ];

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
    private string _selectedItemTypeName = "";

    [ObservableProperty]
    private string _selectedItemPlatform = "";

    [ObservableProperty]
    private bool _isLoadingDetails;

    /// <summary>
    /// Raised when the user clicks "Copy Details". The view handles clipboard access.
    /// </summary>
    public event Action<string>? CopyDetailsRequested;

    /// <summary>
    /// Creates a <see cref="GroupLookupViewModel"/> wired to the current Graph services.
    /// Returns null if not connected.
    /// </summary>
    public GroupLookupViewModel? CreateGroupLookupViewModel()
    {
        if (_groupService == null || _configProfileService == null ||
            _compliancePolicyService == null || _applicationService == null)
            return null;

        return new GroupLookupViewModel(
            _groupService, _configProfileService,
            _compliancePolicyService, _applicationService);
    }

    [RelayCommand]
    private void CopyDetailsToClipboard()
    {
        var text = GetDetailText();
        if (!string.IsNullOrEmpty(text))
            CopyDetailsRequested?.Invoke(text);
    }

    /// <summary>
    /// Builds a plain-text representation of whichever item is currently selected in the detail pane.
    /// </summary>
    public string GetDetailText()
    {
        var sb = new StringBuilder();

        if (SelectedConfiguration is { } cfg)
        {
            sb.AppendLine("=== Device Configuration ===");
            Append(sb, "Name", cfg.DisplayName);
            Append(sb, "Description", cfg.Description);
            Append(sb, "Platform / Type", SelectedItemTypeName);
            Append(sb, "ID", cfg.Id);
            Append(sb, "Created", cfg.CreatedDateTime?.ToString("g"));
            Append(sb, "Last Modified", cfg.LastModifiedDateTime?.ToString("g"));
            Append(sb, "Version", cfg.Version?.ToString());
            AppendAssignments(sb);
        }
        else if (SelectedCompliancePolicy is { } pol)
        {
            sb.AppendLine("=== Compliance Policy ===");
            Append(sb, "Name", pol.DisplayName);
            Append(sb, "Description", pol.Description);
            Append(sb, "Platform / Type", SelectedItemTypeName);
            Append(sb, "ID", pol.Id);
            Append(sb, "Created", pol.CreatedDateTime?.ToString("g"));
            Append(sb, "Last Modified", pol.LastModifiedDateTime?.ToString("g"));
            Append(sb, "Version", pol.Version?.ToString());
            AppendAssignments(sb);
        }
        else if (SelectedSettingsCatalogPolicy is { } sc)
        {
            sb.AppendLine("=== Settings Catalog Policy ===");
            Append(sb, "Name", sc.Name);
            Append(sb, "Description", sc.Description);
            Append(sb, "Platforms", sc.Platforms?.ToString());
            Append(sb, "Technologies", sc.Technologies?.ToString());
            Append(sb, "ID", sc.Id);
            Append(sb, "Is Assigned", sc.IsAssigned?.ToString());
            Append(sb, "Role Scope Tags", sc.RoleScopeTagIds != null ? string.Join(", ", sc.RoleScopeTagIds) : "");
            Append(sb, "Created", sc.CreatedDateTime?.ToString("g"));
            Append(sb, "Last Modified", sc.LastModifiedDateTime?.ToString("g"));
            AppendAssignments(sb);
        }
        else if (SelectedApplication is { } app)
        {
            sb.AppendLine("=== Application ===");
            Append(sb, "Name", app.DisplayName);
            Append(sb, "Description", app.Description);
            Append(sb, "App Type", SelectedItemTypeName);
            Append(sb, "Platform", SelectedItemPlatform);
            Append(sb, "ID", app.Id);
            Append(sb, "Publisher", app.Publisher);
            Append(sb, "Developer", app.Developer);
            Append(sb, "Owner", app.Owner);
            Append(sb, "Featured", app.IsFeatured?.ToString());
            Append(sb, "Notes", app.Notes);
            Append(sb, "Information URL", app.InformationUrl);
            Append(sb, "Privacy URL", app.PrivacyInformationUrl);
            Append(sb, "Created", app.CreatedDateTime?.ToString("g"));
            Append(sb, "Last Modified", app.LastModifiedDateTime?.ToString("g"));
            Append(sb, "Publishing State", app.PublishingState?.ToString());
            AppendAssignments(sb);
        }
        else if (SelectedAppAssignmentRow is { } row)
        {
            sb.AppendLine("=== Application Assignment ===");
            Append(sb, "App Name", row.AppName);
            Append(sb, "App Type", row.AppType);
            Append(sb, "Platform", row.Platform);
            Append(sb, "Publisher", row.Publisher);
            Append(sb, "Version", row.Version);
            Append(sb, "Description", row.Description);
            sb.AppendLine();
            Append(sb, "Assignment Type", row.AssignmentType);
            Append(sb, "Install Intent", row.InstallIntent);
            Append(sb, "Target Name", row.TargetName);
            Append(sb, "Target Group ID", row.TargetGroupId);
            Append(sb, "Is Exclusion", row.IsExclusion);
            Append(sb, "Is Featured", row.IsFeatured);
            Append(sb, "Assignment Settings", row.AssignmentSettings);
            sb.AppendLine();
            Append(sb, "Bundle ID", row.BundleId);
            Append(sb, "Package ID", row.PackageId);
            Append(sb, "Min OS Version", row.MinimumOsVersion);
            Append(sb, "Min Disk (MB)", row.MinimumFreeDiskSpaceMB);
            Append(sb, "Min RAM (MB)", row.MinimumMemoryMB);
            Append(sb, "Min CPUs", row.MinimumProcessors);
            sb.AppendLine();
            Append(sb, "Information URL", row.InformationUrl);
            Append(sb, "Privacy URL", row.PrivacyUrl);
            Append(sb, "App Store URL", row.AppStoreUrl);
            Append(sb, "Created", row.CreatedDate);
            Append(sb, "Last Modified", row.LastModified);
            Append(sb, "Categories", row.Categories);
            Append(sb, "Notes", row.Notes);
        }
        else if (SelectedDynamicGroupRow is { } dg)
        {
            sb.AppendLine("=== Dynamic Group ===");
            Append(sb, "Group Name", dg.GroupName);
            Append(sb, "Description", dg.Description);
            Append(sb, "Membership Rule", dg.MembershipRule);
            Append(sb, "Processing State", dg.ProcessingState);
            Append(sb, "Group Type", dg.GroupType);
            Append(sb, "Total Members", dg.TotalMembers);
            Append(sb, "Users", dg.Users);
            Append(sb, "Devices", dg.Devices);
            Append(sb, "Nested Groups", dg.NestedGroups);
            Append(sb, "Security Enabled", dg.SecurityEnabled);
            Append(sb, "Mail Enabled", dg.MailEnabled);
            Append(sb, "Created Date", dg.CreatedDate);
            Append(sb, "Group ID", dg.GroupId);
        }
        else if (SelectedAssignedGroupRow is { } ag)
        {
            sb.AppendLine("=== Assigned Group ===");
            Append(sb, "Group Name", ag.GroupName);
            Append(sb, "Description", ag.Description);
            Append(sb, "Group Type", ag.GroupType);
            Append(sb, "Total Members", ag.TotalMembers);
            Append(sb, "Users", ag.Users);
            Append(sb, "Devices", ag.Devices);
            Append(sb, "Nested Groups", ag.NestedGroups);
            Append(sb, "Security Enabled", ag.SecurityEnabled);
            Append(sb, "Mail Enabled", ag.MailEnabled);
            Append(sb, "Created Date", ag.CreatedDate);
            Append(sb, "Group ID", ag.GroupId);
        }

        return sb.ToString();
    }

    private static void Append(StringBuilder sb, string label, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            sb.AppendLine($"{label}: {value}");
    }

    private void AppendAssignments(StringBuilder sb)
    {
        if (SelectedItemAssignments.Count == 0) return;
        sb.AppendLine();
        sb.AppendLine("Assignments:");
        foreach (var a in SelectedItemAssignments)
        {
            var line = $"  [{a.TargetKind}] {a.Target}";
            if (!string.IsNullOrEmpty(a.Intent)) line += $" ({a.Intent})";
            if (!string.IsNullOrEmpty(a.GroupId)) line += $" [{a.GroupId}]";
            sb.AppendLine(line);
        }
    }

    // --- Search / filter ---
    [ObservableProperty]
    private string _searchText = "";

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    /// <summary>
    /// Filtered views exposed for DataGrid binding.
    /// These are rebuilt whenever the source collection or SearchText changes.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<DeviceConfiguration> _filteredDeviceConfigurations = [];

    [ObservableProperty]
    private ObservableCollection<DeviceCompliancePolicy> _filteredCompliancePolicies = [];

    [ObservableProperty]
    private ObservableCollection<MobileApp> _filteredApplications = [];

    [ObservableProperty]
    private ObservableCollection<AppAssignmentRow> _filteredAppAssignmentRows = [];

    [ObservableProperty]
    private ObservableCollection<GroupRow> _filteredDynamicGroupRows = [];

    [ObservableProperty]
    private ObservableCollection<GroupRow> _filteredAssignedGroupRows = [];

    [ObservableProperty]
    private ObservableCollection<DeviceManagementConfigurationPolicy> _filteredSettingsCatalogPolicies = [];

    private void ApplyFilter()
    {
        var q = SearchText.Trim();

        if (string.IsNullOrEmpty(q))
        {
            FilteredDeviceConfigurations = new ObservableCollection<DeviceConfiguration>(DeviceConfigurations);
            FilteredCompliancePolicies = new ObservableCollection<DeviceCompliancePolicy>(CompliancePolicies);
            FilteredApplications = new ObservableCollection<MobileApp>(Applications);
            FilteredAppAssignmentRows = new ObservableCollection<AppAssignmentRow>(AppAssignmentRows);
            FilteredDynamicGroupRows = new ObservableCollection<GroupRow>(DynamicGroupRows);
            FilteredAssignedGroupRows = new ObservableCollection<GroupRow>(AssignedGroupRows);
            FilteredSettingsCatalogPolicies = new ObservableCollection<DeviceManagementConfigurationPolicy>(SettingsCatalogPolicies);
            return;
        }

        FilteredDeviceConfigurations = new ObservableCollection<DeviceConfiguration>(
            DeviceConfigurations.Where(c =>
                Contains(c.DisplayName, q) ||
                Contains(c.Description, q) ||
                Contains(c.OdataType, q)));

        FilteredCompliancePolicies = new ObservableCollection<DeviceCompliancePolicy>(
            CompliancePolicies.Where(p =>
                Contains(p.DisplayName, q) ||
                Contains(p.Description, q) ||
                Contains(p.OdataType, q)));

        FilteredApplications = new ObservableCollection<MobileApp>(
            Applications.Where(a =>
                Contains(a.DisplayName, q) ||
                Contains(a.Publisher, q) ||
                Contains(a.Description, q) ||
                Contains(a.OdataType, q)));

        FilteredAppAssignmentRows = new ObservableCollection<AppAssignmentRow>(
            AppAssignmentRows.Where(r =>
                Contains(r.AppName, q) ||
                Contains(r.Publisher, q) ||
                Contains(r.TargetName, q) ||
                Contains(r.AppType, q) ||
                Contains(r.Platform, q) ||
                Contains(r.InstallIntent, q)));

        FilteredDynamicGroupRows = new ObservableCollection<GroupRow>(
            DynamicGroupRows.Where(g =>
                Contains(g.GroupName, q) ||
                Contains(g.Description, q) ||
                Contains(g.MembershipRule, q) ||
                Contains(g.GroupType, q) ||
                Contains(g.GroupId, q)));

        FilteredAssignedGroupRows = new ObservableCollection<GroupRow>(
            AssignedGroupRows.Where(g =>
                Contains(g.GroupName, q) ||
                Contains(g.Description, q) ||
                Contains(g.GroupType, q) ||
                Contains(g.GroupId, q)));

        FilteredSettingsCatalogPolicies = new ObservableCollection<DeviceManagementConfigurationPolicy>(
            SettingsCatalogPolicies.Where(p =>
                Contains(p.Name, q) ||
                Contains(p.Description, q) ||
                Contains(p.Platforms?.ToString(), q) ||
                Contains(p.Technologies?.ToString(), q)));
    }

    private static bool Contains(string? source, string search)
        => source?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

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

    /// <summary>
    /// Returns the column configs for the currently selected nav category.
    /// </summary>
    public ObservableCollection<DataGridColumnConfig>? ActiveColumns => SelectedCategory?.Name switch
    {
        "Device Configurations" => DeviceConfigColumns,
        "Compliance Policies" => CompliancePolicyColumns,
        "Applications" => ApplicationColumns,
        "Application Assignments" => AppAssignmentColumns,
        "Settings Catalog" => SettingsCatalogColumns,
        "Dynamic Groups" => DynamicGroupColumns,
        "Assigned Groups" => AssignedGroupColumns,
        _ => null
    };

    /// <summary>
    /// Maps an OData type string to an inferred platform name.
    /// </summary>
    public static string InferPlatform(string? odataType)
    {
        if (string.IsNullOrEmpty(odataType)) return "";
        var lower = odataType.ToLowerInvariant();
        if (lower.Contains("windows") || lower.Contains("win32") || lower.Contains("msi")) return "Windows";
        if (lower.Contains("ios") || lower.Contains("iphone")) return "iOS";
        if (lower.Contains("macos") || lower.Contains("mac")) return "macOS";
        if (lower.Contains("android")) return "Android";
        if (lower.Contains("webapp") || lower.Contains("webapp")) return "Web";
        return "Cross-platform";
    }

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

        DebugLog.Log("App", "IntuneManager started");

        // Load profiles asynchronously — never block the UI thread
        _ = LoadProfilesAsync();
    }

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

    // Computed visibility helpers for the view
    public bool IsOverviewCategory => SelectedCategory?.Name == "Overview";
    public bool IsDeviceConfigCategory => SelectedCategory?.Name == "Device Configurations";
    public bool IsCompliancePolicyCategory => SelectedCategory?.Name == "Compliance Policies";
    public bool IsApplicationCategory => SelectedCategory?.Name == "Applications";
    public bool IsAppAssignmentsCategory => SelectedCategory?.Name == "Application Assignments";
    public bool IsSettingsCatalogCategory => SelectedCategory?.Name == "Settings Catalog";
    public bool IsDynamicGroupsCategory => SelectedCategory?.Name == "Dynamic Groups";
    public bool IsAssignedGroupsCategory => SelectedCategory?.Name == "Assigned Groups";

    partial void OnSelectedCategoryChanged(NavCategory? value)
    {
        // Clear selections when switching categories
        SelectedConfiguration = null;
        SelectedCompliancePolicy = null;
        SelectedApplication = null;
        SelectedAppAssignmentRow = null;
        SelectedSettingsCatalogPolicy = null;
        SelectedDynamicGroupRow = null;
        SelectedAssignedGroupRow = null;
        SelectedItemAssignments.Clear();
        SelectedItemTypeName = "";
        SelectedItemPlatform = "";

        // Notify category/column changes BEFORE resetting SearchText.
        // SearchText = "" triggers ApplyFilter → data rebinding, and columns
        // must already reflect the new category to avoid binding mismatches
        // (e.g. AppAssignmentColumns applied to MobileApp objects).
        OnPropertyChanged(nameof(IsOverviewCategory));
        OnPropertyChanged(nameof(IsDeviceConfigCategory));
        OnPropertyChanged(nameof(IsCompliancePolicyCategory));
        OnPropertyChanged(nameof(IsApplicationCategory));
        OnPropertyChanged(nameof(IsAppAssignmentsCategory));
        OnPropertyChanged(nameof(IsSettingsCatalogCategory));
        OnPropertyChanged(nameof(IsDynamicGroupsCategory));
        OnPropertyChanged(nameof(IsAssignedGroupsCategory));
        OnPropertyChanged(nameof(ActiveColumns));

        SearchText = "";

        // Lazy-load assignments when navigating to tabs that require them
        if ((value?.Name == "Application Assignments" || value?.Name == "Overview") && !_appAssignmentsLoaded)
        {
            if (!TryLoadLazyCacheEntry<AppAssignmentRow>(CacheKeyAppAssignments, rows =>
            {
                AppAssignmentRows = new ObservableCollection<AppAssignmentRow>(rows);
                _appAssignmentsLoaded = true;
                ApplyFilter();
                StatusText = $"Loaded {rows.Count} app assignment(s) from cache";
            }))
            {
                _ = LoadAppAssignmentRowsAsync();
            }
        }

        // Lazy-load group views
        if (value?.Name == "Dynamic Groups" && !_dynamicGroupsLoaded)
        {
            if (!TryLoadLazyCacheEntry<GroupRow>(CacheKeyDynamicGroups, rows =>
            {
                DynamicGroupRows = new ObservableCollection<GroupRow>(rows);
                _dynamicGroupsLoaded = true;
                ApplyFilter();
                StatusText = $"Loaded {rows.Count} dynamic group(s) from cache";
            }))
            {
                _ = LoadDynamicGroupRowsAsync();
            }
        }
        if (value?.Name == "Assigned Groups" && !_assignedGroupsLoaded)
        {
            if (!TryLoadLazyCacheEntry<GroupRow>(CacheKeyAssignedGroups, rows =>
            {
                AssignedGroupRows = new ObservableCollection<GroupRow>(rows);
                _assignedGroupsLoaded = true;
                ApplyFilter();
                StatusText = $"Loaded {rows.Count} assigned group(s) from cache";
            }))
            {
                _ = LoadAssignedGroupRowsAsync();
            }
        }
    }

    // --- Selection-changed handlers (load detail + assignments) ---

    partial void OnSelectedConfigurationChanged(DeviceConfiguration? value)
    {
        SelectedItemAssignments.Clear();
        SelectedItemTypeName = FriendlyODataType(value?.OdataType);
        if (value?.Id != null)
            _ = LoadConfigAssignmentsAsync(value.Id);
    }

    partial void OnSelectedCompliancePolicyChanged(DeviceCompliancePolicy? value)
    {
        SelectedItemAssignments.Clear();
        SelectedItemTypeName = FriendlyODataType(value?.OdataType);
        if (value?.Id != null)
            _ = LoadCompliancePolicyAssignmentsAsync(value.Id);
    }

    partial void OnSelectedSettingsCatalogPolicyChanged(DeviceManagementConfigurationPolicy? value)
    {
        SelectedItemAssignments.Clear();
        SelectedItemTypeName = value?.Platforms?.ToString() ?? "";
        if (value?.Id != null)
            _ = LoadSettingsCatalogAssignmentsAsync(value.Id);
    }

    partial void OnSelectedApplicationChanged(MobileApp? value)
    {
        SelectedItemAssignments.Clear();
        var odataType = value?.OdataType;
        SelectedItemTypeName = FriendlyODataType(odataType);
        SelectedItemPlatform = InferPlatform(odataType);
        if (value?.Id != null)
            _ = LoadApplicationAssignmentsAsync(value.Id);
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
        catch { /* swallow – non-critical */ }
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
        catch { /* swallow – non-critical */ }
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
        catch { /* swallow – non-critical */ }
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
        catch { /* swallow – non-critical */ }
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
        if (_groupNameCache.TryGetValue(groupId, out var cached)) return cached;

        try
        {
            if (_graphClient != null)
            {
                var group = await _graphClient.Groups[groupId].GetAsync(cfg =>
                    cfg.QueryParameters.Select = ["displayName"]);
                var name = group?.DisplayName ?? groupId;
                _groupNameCache[groupId] = name;
                return name;
            }
        }
        catch { /* fall back to GUID */ }

        _groupNameCache[groupId] = groupId;
        return groupId;
    }

    private static string FriendlyODataType(string? odataType)
    {
        if (string.IsNullOrEmpty(odataType)) return "";
        // OData type is like "#microsoft.graph.windows10GeneralConfiguration"
        var name = odataType.Split('.').LastOrDefault() ?? odataType;
        // Insert spaces before capitals: "windows10GeneralConfiguration" → "Windows10 General Configuration"
        var spaced = System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ");
        return char.ToUpper(spaced[0]) + spaced[1..];
    }

    // --- Application Assignments flattened view ---

    private async Task LoadAppAssignmentRowsAsync()
    {
        if (_applicationService == null || _graphClient == null) return;

        IsBusy = true;
        IsLoadingDetails = true;
        Overview.IsLoading = true;
        StatusText = "Loading application assignments...";

        try
        {
            // Reuse existing apps list if available, otherwise fetch
            var apps = Applications.Count > 0
                ? Applications.ToList()
                : await _applicationService.ListApplicationsAsync();

            var rows = new List<AppAssignmentRow>();
            var total = apps.Count;
            var processed = 0;

            // Use a semaphore to limit concurrent Graph API calls
            using var semaphore = new SemaphoreSlim(5, 5);
            var tasks = apps.Select(async app =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var assignments = app.Id != null
                        ? await _applicationService.GetAssignmentsAsync(app.Id)
                        : [];

                    var appRows = new List<AppAssignmentRow>();
                    foreach (var assignment in assignments)
                    {
                        appRows.Add(await BuildAppAssignmentRowAsync(app, assignment));
                    }

                    // If app has no assignments, still include it with empty assignment fields
                    if (assignments.Count == 0)
                    {
                        appRows.Add(BuildAppRowNoAssignment(app));
                    }

                    lock (rows)
                    {
                        rows.AddRange(appRows);
                        processed++;
                    }

                    // Update status on UI thread periodically
                    if (processed % 10 == 0 || processed == total)
                    {
                        var currentProcessed = processed;
                        var currentTotal = total;
                        Dispatcher.UIThread.Post(() =>
                            StatusText = $"Loading assignments... {currentProcessed}/{currentTotal} apps");
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks);

            // Sort by app name, then target name
            rows.Sort((a, b) =>
            {
                var cmp = string.Compare(a.AppName, b.AppName, StringComparison.OrdinalIgnoreCase);
                return cmp != 0 ? cmp : string.Compare(a.TargetName, b.TargetName, StringComparison.OrdinalIgnoreCase);
            });

            AppAssignmentRows = new ObservableCollection<AppAssignmentRow>(rows);
            _appAssignmentsLoaded = true;
            ApplyFilter();

            // Save to cache
            if (ActiveProfile?.TenantId != null)
            {
                _cacheService.Set(ActiveProfile.TenantId, CacheKeyAppAssignments, rows);
                DebugLog.Log("Cache", $"Saved {rows.Count} app assignment row(s) to cache");
            }

            // Update Overview dashboard now that all data is ready
            Overview.Update(
                ActiveProfile,
                (IReadOnlyList<DeviceConfiguration>)DeviceConfigurations,
                (IReadOnlyList<DeviceCompliancePolicy>)CompliancePolicies,
                (IReadOnlyList<MobileApp>)Applications,
                (IReadOnlyList<AppAssignmentRow>)AppAssignmentRows);

            StatusText = $"Loaded {rows.Count} application assignments row(s) from {total} apps";
        }
        catch (Exception ex)
        {
            SetError($"Failed to load Application Assignments: {FormatGraphError(ex)}");
            StatusText = "Error loading Application Assignments";
        }
        finally
        {
            IsBusy = false;
            IsLoadingDetails = false;
            Overview.IsLoading = false;
        }
    }

    private async Task<AppAssignmentRow> BuildAppAssignmentRowAsync(MobileApp app, MobileAppAssignment assignment)
    {
        var (assignmentType, targetName, targetGroupId, isExclusion) =
            await ResolveAssignmentTargetAsync(assignment.Target);

        return new AppAssignmentRow
        {
            AppId = app.Id ?? "",
            AppName = app.DisplayName ?? "",
            Publisher = app.Publisher ?? "",
            Description = app.Description ?? "",
            AppType = ExtractShortTypeName(app.OdataType),
            Version = ExtractVersion(app),
            Platform = InferPlatform(app.OdataType),
            BundleId = ExtractBundleId(app),
            PackageId = ExtractPackageId(app),
            IsFeatured = app.IsFeatured == true ? "True" : "False",
            CreatedDate = app.CreatedDateTime?.ToString("g", CultureInfo.InvariantCulture) ?? "",
            LastModified = app.LastModifiedDateTime?.ToString("g", CultureInfo.InvariantCulture) ?? "",
            AssignmentType = assignmentType,
            TargetName = targetName,
            TargetGroupId = targetGroupId,
            InstallIntent = assignment.Intent?.ToString()?.ToLowerInvariant() ?? "",
            AssignmentSettings = FormatAssignmentSettings(assignment.Settings),
            IsExclusion = isExclusion,
            AppStoreUrl = ExtractAppStoreUrl(app),
            PrivacyUrl = app.PrivacyInformationUrl ?? "",
            InformationUrl = app.InformationUrl ?? "",
            MinimumOsVersion = ExtractMinOsVersion(app),
            MinimumFreeDiskSpaceMB = ExtractMinDiskSpace(app),
            MinimumMemoryMB = ExtractMinMemory(app),
            MinimumProcessors = ExtractMinProcessors(app),
            Categories = app.Categories != null
                ? string.Join(", ", app.Categories.Select(c => c.DisplayName ?? ""))
                : "",
            Notes = app.Notes ?? ""
        };
    }

    private AppAssignmentRow BuildAppRowNoAssignment(MobileApp app)
    {
        return new AppAssignmentRow
        {
            AppId = app.Id ?? "",
            AppName = app.DisplayName ?? "",
            Publisher = app.Publisher ?? "",
            Description = app.Description ?? "",
            AppType = ExtractShortTypeName(app.OdataType),
            Version = ExtractVersion(app),
            Platform = InferPlatform(app.OdataType),
            BundleId = ExtractBundleId(app),
            PackageId = ExtractPackageId(app),
            IsFeatured = app.IsFeatured == true ? "True" : "False",
            CreatedDate = app.CreatedDateTime?.ToString("g", CultureInfo.InvariantCulture) ?? "",
            LastModified = app.LastModifiedDateTime?.ToString("g", CultureInfo.InvariantCulture) ?? "",
            AssignmentType = "None",
            TargetName = "",
            TargetGroupId = "",
            InstallIntent = "",
            AssignmentSettings = "",
            IsExclusion = "False",
            AppStoreUrl = ExtractAppStoreUrl(app),
            PrivacyUrl = app.PrivacyInformationUrl ?? "",
            InformationUrl = app.InformationUrl ?? "",
            MinimumOsVersion = ExtractMinOsVersion(app),
            MinimumFreeDiskSpaceMB = ExtractMinDiskSpace(app),
            MinimumMemoryMB = ExtractMinMemory(app),
            MinimumProcessors = ExtractMinProcessors(app),
            Categories = app.Categories != null
                ? string.Join(", ", app.Categories.Select(c => c.DisplayName ?? ""))
                : "",
            Notes = app.Notes ?? ""
        };
    }

    private async Task<(string Type, string Name, string GroupId, string IsExclusion)>
        ResolveAssignmentTargetAsync(DeviceAndAppManagementAssignmentTarget? target)
    {
        return target switch
        {
            AllDevicesAssignmentTarget => ("All Devices", "All Devices", "", "False"),
            AllLicensedUsersAssignmentTarget => ("All Users", "All Users", "", "False"),
            ExclusionGroupAssignmentTarget excl =>
                ("Group", await ResolveGroupNameAsync(excl.GroupId), excl.GroupId ?? "", "True"),
            GroupAssignmentTarget grp =>
                ("Group", await ResolveGroupNameAsync(grp.GroupId), grp.GroupId ?? "", "False"),
            _ => ("Unknown", "Unknown", "", "False")
        };
    }

    // --- Type-specific field extractors ---

    private static string? TryGetAdditionalString(MobileApp app, string key)
    {
        if (app.AdditionalData?.TryGetValue(key, out var val) == true)
            return val?.ToString();
        return null;
    }

    private static string ExtractShortTypeName(string? odataType)
    {
        if (string.IsNullOrEmpty(odataType)) return "";
        // "#microsoft.graph.win32LobApp" → "win32LobApp"
        return odataType.Split('.').LastOrDefault() ?? odataType;
    }

    private static string ExtractVersion(MobileApp app)
    {
        return app switch
        {
            Win32LobApp w => TryGetAdditionalString(w, "displayVersion")
                             ?? w.MsiInformation?.ProductVersion ?? "",
            MacOSLobApp m => m.VersionNumber ?? "",
            MacOSDmgApp d => d.PrimaryBundleVersion ?? "",
            IosLobApp i => i.VersionNumber ?? "",
            _ => ""
        };
    }

    private static string ExtractBundleId(MobileApp app)
    {
        return app switch
        {
            IosLobApp i => i.BundleId ?? "",
            IosStoreApp s => s.BundleId ?? "",
            IosVppApp v => v.BundleId ?? "",
            MacOSLobApp m => m.BundleId ?? "",
            MacOSDmgApp d => d.PrimaryBundleId ?? "",
            _ => ""
        };
    }

    private static string ExtractPackageId(MobileApp app)
    {
        return app switch
        {
            AndroidStoreApp a => a.PackageId ?? "",
            _ => ""
        };
    }

    private static string ExtractAppStoreUrl(MobileApp app)
    {
        return app switch
        {
            IosStoreApp i => i.AppStoreUrl ?? "",
            AndroidStoreApp a => a.AppStoreUrl ?? "",
            WebApp w => w.AppUrl ?? "",
            _ => ""
        };
    }

    private static string ExtractMinOsVersion(MobileApp app)
    {
        return app switch
        {
            Win32LobApp w => w.MinimumSupportedWindowsRelease ?? "",
            _ => ""
        };
    }

    private static string ExtractMinDiskSpace(MobileApp app)
    {
        return app switch
        {
            Win32LobApp w when w.MinimumFreeDiskSpaceInMB.HasValue =>
                w.MinimumFreeDiskSpaceInMB.Value.ToString(CultureInfo.InvariantCulture),
            _ => ""
        };
    }

    private static string ExtractMinMemory(MobileApp app)
    {
        return app switch
        {
            Win32LobApp w when w.MinimumMemoryInMB.HasValue =>
                w.MinimumMemoryInMB.Value.ToString(CultureInfo.InvariantCulture),
            _ => ""
        };
    }

    private static string ExtractMinProcessors(MobileApp app)
    {
        return app switch
        {
            Win32LobApp w when w.MinimumNumberOfProcessors.HasValue =>
                w.MinimumNumberOfProcessors.Value.ToString(CultureInfo.InvariantCulture),
            _ => ""
        };
    }

    private static string FormatAssignmentSettings(MobileAppAssignmentSettings? settings)
    {
        if (settings is Win32LobAppAssignmentSettings w32)
        {
            var parts = new List<string>();
            if (w32.Notifications.HasValue)
                parts.Add($"Notifications: {w32.Notifications.Value.ToString().ToLowerInvariant()}");
            if (w32.InstallTimeSettings != null)
                parts.Add("Install Time: configured");
            if (w32.DeliveryOptimizationPriority.HasValue)
                parts.Add($"Delivery Priority: {w32.DeliveryOptimizationPriority.Value.ToString().ToLowerInvariant()}");
            return parts.Count > 0 ? string.Join("; ", parts) : "N/A";
        }

        return settings != null ? "N/A" : "N/A";
    }

    // --- CSV Export ---

    [RelayCommand]
    private async Task ExportAppAssignmentsCsvAsync(CancellationToken cancellationToken)
    {
        if (AppAssignmentRows.Count == 0)
        {
            StatusText = "No application assignments data to export";
            return;
        }

        IsBusy = true;
        StatusText = "Exporting Application Assignments to CSV...";

        try
        {
            var outputPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "IntuneExport");
            Directory.CreateDirectory(outputPath);

            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var csvPath = Path.Combine(outputPath, $"ApplicationAssignments-{timestamp}.csv");

            var sb = new StringBuilder();

            // Header row matching the CSV columns
            sb.AppendLine("\"App Name\",\"Publisher\",\"Description\",\"App Type\",\"Version\"," +
                          "\"Bundle ID\",\"Package ID\",\"Is Featured\",\"Created Date\",\"Last Modified\"," +
                          "\"Assignment Type\",\"Target Name\",\"Target Group ID\",\"Install Intent\"," +
                          "\"Assignment Settings\",\"Is Exclusion\",\"App Store URL\",\"Privacy URL\"," +
                          "\"Information URL\",\"Minimum OS Version\",\"Minimum Free Disk Space (MB)\"," +
                          "\"Minimum Memory (MB)\",\"Minimum Processors\",\"Categories\",\"Notes\"");

            foreach (var row in AppAssignmentRows)
            {
                sb.AppendLine(string.Join(",",
                    CsvEscape(row.AppName),
                    CsvEscape(row.Publisher),
                    CsvEscape(row.Description),
                    CsvEscape(row.AppType),
                    CsvEscape(row.Version),
                    CsvEscape(row.BundleId),
                    CsvEscape(row.PackageId),
                    CsvEscape(row.IsFeatured),
                    CsvEscape(row.CreatedDate),
                    CsvEscape(row.LastModified),
                    CsvEscape(row.AssignmentType),
                    CsvEscape(row.TargetName),
                    CsvEscape(row.TargetGroupId),
                    CsvEscape(row.InstallIntent),
                    CsvEscape(row.AssignmentSettings),
                    CsvEscape(row.IsExclusion),
                    CsvEscape(row.AppStoreUrl),
                    CsvEscape(row.PrivacyUrl),
                    CsvEscape(row.InformationUrl),
                    CsvEscape(row.MinimumOsVersion),
                    CsvEscape(row.MinimumFreeDiskSpaceMB),
                    CsvEscape(row.MinimumMemoryMB),
                    CsvEscape(row.MinimumProcessors),
                    CsvEscape(row.Categories),
                    CsvEscape(row.Notes)));
            }

            await File.WriteAllTextAsync(csvPath, sb.ToString(), Encoding.UTF8, cancellationToken);
            StatusText = $"Exported {AppAssignmentRows.Count} rows to {csvPath}";
        }
        catch (Exception ex)
        {
            SetError($"CSV export failed: {ex.Message}");
            StatusText = "CSV export failed";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    // --- Group CSV Export ---

    [RelayCommand]
    private async Task ExportGroupsCsvAsync(CancellationToken cancellationToken)
    {
        var isDynamic = IsDynamicGroupsCategory;
        var rows = isDynamic ? DynamicGroupRows : AssignedGroupRows;
        var label = isDynamic ? "Dynamic Groups" : "Assigned Groups";

        if (rows.Count == 0)
        {
            StatusText = $"No {label.ToLowerInvariant()} data to export";
            return;
        }

        IsBusy = true;
        StatusText = $"Exporting {label} to CSV...";

        try
        {
            var outputPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "IntuneExport");
            Directory.CreateDirectory(outputPath);

            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var fileName = isDynamic ? "DynamicGroups" : "AssignedGroups";
            var csvPath = Path.Combine(outputPath, $"{fileName}-{timestamp}.csv");

            var sb = new StringBuilder();

            if (isDynamic)
            {
                sb.AppendLine("\"Group Name\",\"Description\",\"Membership Rule\",\"Processing State\"," +
                              "\"Group Type\",\"Total Members\",\"Users\",\"Devices\",\"Nested Groups\"," +
                              "\"Security Enabled\",\"Mail Enabled\",\"Created Date\",\"Group ID\"");

                foreach (var row in rows)
                {
                    sb.AppendLine(string.Join(",",
                        CsvEscape(row.GroupName), CsvEscape(row.Description),
                        CsvEscape(row.MembershipRule), CsvEscape(row.ProcessingState),
                        CsvEscape(row.GroupType), CsvEscape(row.TotalMembers),
                        CsvEscape(row.Users), CsvEscape(row.Devices),
                        CsvEscape(row.NestedGroups), CsvEscape(row.SecurityEnabled),
                        CsvEscape(row.MailEnabled), CsvEscape(row.CreatedDate),
                        CsvEscape(row.GroupId)));
                }
            }
            else
            {
                sb.AppendLine("\"Group Name\",\"Description\",\"Group Type\"," +
                              "\"Total Members\",\"Users\",\"Devices\",\"Nested Groups\"," +
                              "\"Security Enabled\",\"Mail Enabled\",\"Created Date\",\"Group ID\"");

                foreach (var row in rows)
                {
                    sb.AppendLine(string.Join(",",
                        CsvEscape(row.GroupName), CsvEscape(row.Description),
                        CsvEscape(row.GroupType), CsvEscape(row.TotalMembers),
                        CsvEscape(row.Users), CsvEscape(row.Devices),
                        CsvEscape(row.NestedGroups), CsvEscape(row.SecurityEnabled),
                        CsvEscape(row.MailEnabled), CsvEscape(row.CreatedDate),
                        CsvEscape(row.GroupId)));
                }
            }

            await File.WriteAllTextAsync(csvPath, sb.ToString(), Encoding.UTF8, cancellationToken);
            StatusText = $"Exported {rows.Count} {label.ToLowerInvariant()} to {csvPath}";
        }
        catch (Exception ex)
        {
            SetError($"CSV export failed: {ex.Message}");
            StatusText = "CSV export failed";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // --- Dynamic Groups view ---

    private async Task LoadDynamicGroupRowsAsync()
    {
        if (_groupService == null) return;

        IsBusy = true;
        StatusText = "Loading dynamic groups...";

        try
        {
            var groups = await _groupService.ListDynamicGroupsAsync();
            var rows = new List<GroupRow>();
            var total = groups.Count;
            var processed = 0;

            using var semaphore = new SemaphoreSlim(5, 5);
            var tasks = groups.Select(async group =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var counts = group.Id != null
                        ? await _groupService.GetMemberCountsAsync(group.Id)
                        : new GroupMemberCounts(0, 0, 0, 0);

                    var row = BuildGroupRow(group, counts);

                    lock (rows)
                    {
                        rows.Add(row);
                        processed++;
                    }

                    if (processed % 10 == 0 || processed == total)
                    {
                        var p = processed;
                        Dispatcher.UIThread.Post(() =>
                            StatusText = $"Loading dynamic groups... {p}/{total}");
                    }
                }
                finally { semaphore.Release(); }
            }).ToList();

            await Task.WhenAll(tasks);

            rows.Sort((a, b) => string.Compare(a.GroupName, b.GroupName, StringComparison.OrdinalIgnoreCase));

            DynamicGroupRows = new ObservableCollection<GroupRow>(rows);
            _dynamicGroupsLoaded = true;
            ApplyFilter();

            // Save to cache
            if (ActiveProfile?.TenantId != null)
            {
                _cacheService.Set(ActiveProfile.TenantId, CacheKeyDynamicGroups, rows);
                DebugLog.Log("Cache", $"Saved {rows.Count} dynamic group row(s) to cache");
            }

            StatusText = $"Loaded {rows.Count} dynamic group(s)";
        }
        catch (Exception ex)
        {
            SetError($"Failed to load dynamic groups: {FormatGraphError(ex)}");
            StatusText = "Error loading dynamic groups";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // --- Assigned Groups view ---

    private async Task LoadAssignedGroupRowsAsync()
    {
        if (_groupService == null) return;

        IsBusy = true;
        StatusText = "Loading assigned groups...";

        try
        {
            var groups = await _groupService.ListAssignedGroupsAsync();
            var rows = new List<GroupRow>();
            var total = groups.Count;
            var processed = 0;

            using var semaphore = new SemaphoreSlim(5, 5);
            var tasks = groups.Select(async group =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var counts = group.Id != null
                        ? await _groupService.GetMemberCountsAsync(group.Id)
                        : new GroupMemberCounts(0, 0, 0, 0);

                    var row = BuildGroupRow(group, counts);

                    lock (rows)
                    {
                        rows.Add(row);
                        processed++;
                    }

                    if (processed % 10 == 0 || processed == total)
                    {
                        var p = processed;
                        Dispatcher.UIThread.Post(() =>
                            StatusText = $"Loading assigned groups... {p}/{total}");
                    }
                }
                finally { semaphore.Release(); }
            }).ToList();

            await Task.WhenAll(tasks);

            rows.Sort((a, b) => string.Compare(a.GroupName, b.GroupName, StringComparison.OrdinalIgnoreCase));

            AssignedGroupRows = new ObservableCollection<GroupRow>(rows);
            _assignedGroupsLoaded = true;
            ApplyFilter();

            // Save to cache
            if (ActiveProfile?.TenantId != null)
            {
                _cacheService.Set(ActiveProfile.TenantId, CacheKeyAssignedGroups, rows);
                DebugLog.Log("Cache", $"Saved {rows.Count} assigned group row(s) to cache");
            }

            StatusText = $"Loaded {rows.Count} assigned group(s)";
        }
        catch (Exception ex)
        {
            SetError($"Failed to load assigned groups: {FormatGraphError(ex)}");
            StatusText = "Error loading assigned groups";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static GroupRow BuildGroupRow(Microsoft.Graph.Beta.Models.Group group, GroupMemberCounts counts)
    {
        return new GroupRow
        {
            GroupName = group.DisplayName ?? "",
            Description = group.Description ?? "",
            MembershipRule = group.MembershipRule ?? "",
            ProcessingState = group.MembershipRuleProcessingState ?? "",
            GroupType = GroupService.InferGroupType(group),
            TotalMembers = counts.Total.ToString(CultureInfo.InvariantCulture),
            Users = counts.Users.ToString(CultureInfo.InvariantCulture),
            Devices = counts.Devices.ToString(CultureInfo.InvariantCulture),
            NestedGroups = counts.NestedGroups.ToString(CultureInfo.InvariantCulture),
            SecurityEnabled = group.SecurityEnabled == true ? "Yes" : "No",
            MailEnabled = group.MailEnabled == true ? "Yes" : "No",
            CreatedDate = group.CreatedDateTime?.ToString("g", CultureInfo.InvariantCulture) ?? "",
            GroupId = group.Id ?? ""
        };
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
            WindowTitle = $"IntuneManager - {profile.Name}";
            CurrentView = null;

            _graphClient = await _graphClientFactory.CreateClientAsync(profile);
            DebugLog.Log("Auth", "Graph client created successfully");
            _configProfileService = new ConfigurationProfileService(_graphClient);
            _compliancePolicyService = new CompliancePolicyService(_graphClient);
            _applicationService = new ApplicationService(_graphClient);
            _groupService = new GroupService(_graphClient);
            _settingsCatalogService = new SettingsCatalogService(_graphClient);
            _importService = new ImportService(_configProfileService, _compliancePolicyService);

            RefreshSwitcherProfiles();
            SelectedSwitchProfile = profile;

            // Default to first nav category
            SelectedCategory = NavCategories.FirstOrDefault();

            StatusText = $"Connected to {profile.Name}";
            DebugLog.Log("Auth", $"Connected to {profile.Name}");

            // Try loading cached data — if all 4 types are cached, skip Graph refresh
            var cachedCount = TryLoadFromCache(profile.TenantId ?? "");
            if (cachedCount >= 4)
            {
                DebugLog.Log("Cache", "All data loaded from cache — skipping Graph refresh");
                IsBusy = false;
            }
            else
            {
                if (cachedCount > 0)
                    DebugLog.Log("Cache", $"Partial cache hit ({cachedCount}/4) — refreshing from Graph");
                await RefreshAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            DebugLog.LogError($"Connection to {profile.Name} failed: {FormatGraphError(ex)}", ex);
            SetError($"Connection failed: {FormatGraphError(ex)}");
            StatusText = "Connection failed";
            DisconnectInternal();
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

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        ClearError();
        IsBusy = true;
        DebugLog.Log("Graph", "Refreshing all data from Graph API...");
        var errors = new List<string>();

        try
        {
            if (_configProfileService != null)
            {
                try
                {
                    StatusText = "Loading device configurations...";
                    var configs = await _configProfileService.ListDeviceConfigurationsAsync(cancellationToken);
                    DeviceConfigurations = new ObservableCollection<DeviceConfiguration>(configs);
                    DebugLog.Log("Graph", $"Loaded {configs.Count} device configuration(s)");
                }
                catch (Exception ex)
                {
                    var detail = FormatGraphError(ex);
                    DebugLog.LogError($"Failed to load device configurations: {detail}", ex);
                    errors.Add($"Device Configs: {detail}");
                }
            }

            if (_compliancePolicyService != null)
            {
                try
                {
                    StatusText = "Loading compliance policies...";
                    var policies = await _compliancePolicyService.ListCompliancePoliciesAsync(cancellationToken);
                    CompliancePolicies = new ObservableCollection<DeviceCompliancePolicy>(policies);
                    DebugLog.Log("Graph", $"Loaded {policies.Count} compliance policy(ies)");
                }
                catch (Exception ex)
                {
                    var detail = FormatGraphError(ex);
                    DebugLog.LogError($"Failed to load compliance policies: {detail}", ex);
                    errors.Add($"Compliance Policies: {detail}");
                }
            }

            if (_applicationService != null)
            {
                try
                {
                    StatusText = "Loading applications...";
                    var apps = await _applicationService.ListApplicationsAsync(cancellationToken);
                    Applications = new ObservableCollection<MobileApp>(apps);
                    DebugLog.Log("Graph", $"Loaded {apps.Count} application(s)");
                }
                catch (Exception ex)
                {
                    var detail = FormatGraphError(ex);
                    DebugLog.LogError($"Failed to load applications: {detail}", ex);
                    errors.Add($"Applications: {detail}");
                }
            }

            if (_settingsCatalogService != null)
            {
                try
                {
                    StatusText = "Loading settings catalog policies...";
                    var settingsPolicies = await _settingsCatalogService.ListSettingsCatalogPoliciesAsync(cancellationToken);
                    SettingsCatalogPolicies = new ObservableCollection<DeviceManagementConfigurationPolicy>(settingsPolicies);
                    DebugLog.Log("Graph", $"Loaded {settingsPolicies.Count} settings catalog policy(ies)");
                }
                catch (Exception ex)
                {
                    var detail = FormatGraphError(ex);
                    DebugLog.LogError($"Failed to load settings catalog: {detail}", ex);
                    errors.Add($"Settings Catalog: {detail}");
                }
            }

            var totalItems = DeviceConfigurations.Count + CompliancePolicies.Count + Applications.Count + SettingsCatalogPolicies.Count;
            StatusText = $"Loaded {totalItems} item(s) ({DeviceConfigurations.Count} configs, {CompliancePolicies.Count} policies, {Applications.Count} apps, {SettingsCatalogPolicies.Count} settings catalog)";

            if (errors.Count > 0)
                SetError($"Some data failed to load — {string.Join("; ", errors)}");

            // Save successful loads to cache
            if (ActiveProfile?.TenantId != null)
                SaveToCache(ActiveProfile.TenantId);

            ApplyFilter();

            // Reset lazy-load state; actual loading is triggered when navigating to those tabs
            _appAssignmentsLoaded = false;
            _dynamicGroupsLoaded = false;
            _assignedGroupsLoaded = false;

            // Invalidate lazy-load caches so they reload from Graph on next tab visit
            if (ActiveProfile?.TenantId != null)
            {
                _cacheService.Invalidate(ActiveProfile.TenantId, CacheKeyAppAssignments);
                _cacheService.Invalidate(ActiveProfile.TenantId, CacheKeyDynamicGroups);
                _cacheService.Invalidate(ActiveProfile.TenantId, CacheKeyAssignedGroups);
            }
        }
        catch (Exception ex)
        {
            SetError($"Failed to load data: {FormatGraphError(ex)}");
            StatusText = "Error loading data";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // --- Export ---

    [RelayCommand]
    private async Task ExportSelectedAsync(CancellationToken cancellationToken)
    {
        ClearError();
        IsBusy = true;

        try
        {
            var outputPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "IntuneExport");

            var migrationTable = new MigrationTable();

            if (IsDeviceConfigCategory && SelectedConfiguration != null)
            {
                StatusText = $"Exporting {SelectedConfiguration.DisplayName}...";
                await _exportService.ExportDeviceConfigurationAsync(
                    SelectedConfiguration, outputPath, migrationTable, cancellationToken);
            }
            else if (IsCompliancePolicyCategory && SelectedCompliancePolicy != null)
            {
                StatusText = $"Exporting {SelectedCompliancePolicy.DisplayName}...";
                var assignments = _compliancePolicyService != null && SelectedCompliancePolicy.Id != null
                    ? await _compliancePolicyService.GetAssignmentsAsync(SelectedCompliancePolicy.Id, cancellationToken)
                    : [];
                await _exportService.ExportCompliancePolicyAsync(
                    SelectedCompliancePolicy, assignments, outputPath, migrationTable, cancellationToken);
            }
            else if (IsApplicationCategory && SelectedApplication != null)
            {
                StatusText = $"Exporting {SelectedApplication.DisplayName}...";
                var assignments = _applicationService != null && SelectedApplication.Id != null
                    ? await _applicationService.GetAssignmentsAsync(SelectedApplication.Id, cancellationToken)
                    : [];
                await _exportService.ExportApplicationAsync(
                    SelectedApplication, assignments, outputPath, migrationTable, cancellationToken);
            }
            else
            {
                StatusText = "Nothing selected to export";
                return;
            }

            await _exportService.SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
            StatusText = $"Exported to {outputPath}";
        }
        catch (Exception ex)
        {
            SetError($"Export failed: {FormatGraphError(ex)}");
            StatusText = "Export failed";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportAllAsync(CancellationToken cancellationToken)
    {
        ClearError();
        IsBusy = true;

        try
        {
            var outputPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "IntuneExport");

            var migrationTable = new MigrationTable();
            var count = 0;

            // Export device configs
            if (DeviceConfigurations.Any())
            {
                StatusText = "Exporting device configurations...";
                foreach (var config in DeviceConfigurations)
                {
                    await _exportService.ExportDeviceConfigurationAsync(config, outputPath, migrationTable, cancellationToken);
                    count++;
                }
            }

            // Export compliance policies with assignments
            if (CompliancePolicies.Any() && _compliancePolicyService != null)
            {
                StatusText = "Exporting compliance policies...";
                foreach (var policy in CompliancePolicies)
                {
                    var assignments = policy.Id != null
                        ? await _compliancePolicyService.GetAssignmentsAsync(policy.Id, cancellationToken)
                        : [];
                    await _exportService.ExportCompliancePolicyAsync(policy, assignments, outputPath, migrationTable, cancellationToken);
                    count++;
                }
            }

            // Export applications with assignments
            if (Applications.Any() && _applicationService != null)
            {
                StatusText = "Exporting applications...";
                foreach (var app in Applications)
                {
                    var assignments = app.Id != null
                        ? await _applicationService.GetAssignmentsAsync(app.Id, cancellationToken)
                        : [];
                    await _exportService.ExportApplicationAsync(app, assignments, outputPath, migrationTable, cancellationToken);
                    count++;
                }
            }

            await _exportService.SaveMigrationTableAsync(migrationTable, outputPath, cancellationToken);
            StatusText = $"Exported {count} item(s) to {outputPath}";
        }
        catch (Exception ex)
        {
            SetError($"Export failed: {FormatGraphError(ex)}");
            StatusText = "Export failed";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // --- Import ---

    [RelayCommand]
    private async Task ImportFromFolderAsync(string folderPath, CancellationToken cancellationToken)
    {
        if (_importService == null) return;

        ClearError();
        IsBusy = true;
        StatusText = "Importing...";

        try
        {
            var migrationTable = await _importService.ReadMigrationTableAsync(folderPath, cancellationToken);
            var imported = 0;

            // Import device configurations
            var configs = await _importService.ReadDeviceConfigurationsFromFolderAsync(folderPath, cancellationToken);
            foreach (var config in configs)
            {
                await _importService.ImportDeviceConfigurationAsync(config, migrationTable, cancellationToken);
                imported++;
                StatusText = $"Imported {imported} item(s)...";
            }

            // Import compliance policies
            var policies = await _importService.ReadCompliancePoliciesFromFolderAsync(folderPath, cancellationToken);
            foreach (var export in policies)
            {
                await _importService.ImportCompliancePolicyAsync(export, migrationTable, cancellationToken);
                imported++;
                StatusText = $"Imported {imported} item(s)...";
            }

            // Save updated migration table
            await _exportService.SaveMigrationTableAsync(migrationTable, folderPath, cancellationToken);
            StatusText = $"Imported {imported} item(s)";

            await RefreshAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            SetError($"Import failed: {FormatGraphError(ex)}");
            StatusText = "Import failed";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // --- Cache helpers ---

    /// <summary>
    /// Attempts to populate all collections from cached data.
    /// Returns how many data types were loaded (0–4).
    /// </summary>
    private int TryLoadFromCache(string tenantId)
    {
        if (string.IsNullOrEmpty(tenantId)) return 0;

        var typesLoaded = 0;
        DateTime? oldestCacheTime = null;

        try
        {
            var configs = _cacheService.Get<DeviceConfiguration>(tenantId, CacheKeyDeviceConfigs);
            if (configs != null)
            {
                DeviceConfigurations = new ObservableCollection<DeviceConfiguration>(configs);
                DebugLog.Log("Cache", $"Loaded {configs.Count} device configuration(s) from cache");
                typesLoaded++;
                UpdateOldestCacheTime(ref oldestCacheTime, tenantId, CacheKeyDeviceConfigs);
            }

            var policies = _cacheService.Get<DeviceCompliancePolicy>(tenantId, CacheKeyCompliancePolicies);
            if (policies != null)
            {
                CompliancePolicies = new ObservableCollection<DeviceCompliancePolicy>(policies);
                DebugLog.Log("Cache", $"Loaded {policies.Count} compliance policy(ies) from cache");
                typesLoaded++;
                UpdateOldestCacheTime(ref oldestCacheTime, tenantId, CacheKeyCompliancePolicies);
            }

            var apps = _cacheService.Get<MobileApp>(tenantId, CacheKeyApplications);
            if (apps != null)
            {
                Applications = new ObservableCollection<MobileApp>(apps);
                DebugLog.Log("Cache", $"Loaded {apps.Count} application(s) from cache");
                typesLoaded++;
                UpdateOldestCacheTime(ref oldestCacheTime, tenantId, CacheKeyApplications);
            }

            var settingsPolicies = _cacheService.Get<DeviceManagementConfigurationPolicy>(tenantId, CacheKeySettingsCatalog);
            if (settingsPolicies != null)
            {
                SettingsCatalogPolicies = new ObservableCollection<DeviceManagementConfigurationPolicy>(settingsPolicies);
                DebugLog.Log("Cache", $"Loaded {settingsPolicies.Count} settings catalog policy(ies) from cache");
                typesLoaded++;
                UpdateOldestCacheTime(ref oldestCacheTime, tenantId, CacheKeySettingsCatalog);
            }

            if (typesLoaded > 0)
            {
                var totalItems = DeviceConfigurations.Count + CompliancePolicies.Count + Applications.Count + SettingsCatalogPolicies.Count;
                var ageText = FormatCacheAge(oldestCacheTime);
                StatusText = $"Loaded {totalItems} item(s) from cache ({ageText})";
                ApplyFilter();
            }
            else
            {
                DebugLog.Log("Cache", "No cached data found");
            }
        }
        catch (Exception ex)
        {
            DebugLog.LogError($"Failed to load from cache: {ex.Message}", ex);
        }

        return typesLoaded;
    }

    private void UpdateOldestCacheTime(ref DateTime? oldest, string tenantId, string dataType)
    {
        var meta = _cacheService.GetMetadata(tenantId, dataType);
        if (meta != null)
        {
            if (oldest == null || meta.Value.CachedAt < oldest.Value)
                oldest = meta.Value.CachedAt;
        }
    }

    private static string FormatCacheAge(DateTime? cachedAtUtc)
    {
        if (cachedAtUtc == null) return "unknown age";
        var age = DateTime.UtcNow - cachedAtUtc.Value;
        if (age.TotalMinutes < 1) return "just now";
        if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes}m ago";
        if (age.TotalHours < 24) return $"{(int)age.TotalHours}h {age.Minutes}m ago";
        return $"{(int)age.TotalDays}d ago";
    }

    /// <summary>
    /// Tries to load a lazy-loaded view (app assignments, groups) from cache.
    /// Invokes the onLoaded callback with the data if found. Returns true if cache hit.
    /// </summary>
    private bool TryLoadLazyCacheEntry<T>(string cacheKey, Action<List<T>> onLoaded)
    {
        var tenantId = ActiveProfile?.TenantId;
        if (string.IsNullOrEmpty(tenantId)) return false;

        try
        {
            var cached = _cacheService.Get<T>(tenantId, cacheKey);
            if (cached != null && cached.Count > 0)
            {
                DebugLog.Log("Cache", $"Loaded {cached.Count} {cacheKey} row(s) from cache");
                onLoaded(cached);
                return true;
            }
        }
        catch (Exception ex)
        {
            DebugLog.LogError($"Failed to load {cacheKey} from cache: {ex.Message}", ex);
        }

        return false;
    }

    /// <summary>
    /// Saves all current collections to the cache.
    /// </summary>
    private void SaveToCache(string tenantId)
    {
        if (string.IsNullOrEmpty(tenantId)) return;

        try
        {
            if (DeviceConfigurations.Count > 0)
                _cacheService.Set(tenantId, CacheKeyDeviceConfigs, DeviceConfigurations.ToList());

            if (CompliancePolicies.Count > 0)
                _cacheService.Set(tenantId, CacheKeyCompliancePolicies, CompliancePolicies.ToList());

            if (Applications.Count > 0)
                _cacheService.Set(tenantId, CacheKeyApplications, Applications.ToList());

            if (SettingsCatalogPolicies.Count > 0)
                _cacheService.Set(tenantId, CacheKeySettingsCatalog, SettingsCatalogPolicies.ToList());

            DebugLog.Log("Cache", "Saved data to disk cache");
        }
        catch (Exception ex)
        {
            DebugLog.LogError($"Failed to save to cache: {ex.Message}", ex);
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
        WindowTitle = "IntuneManager";
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
        SettingsCatalogPolicies.Clear();
        SelectedSettingsCatalogPolicy = null;
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
        _importService = null;
        _groupNameCache.Clear();
    }
}
