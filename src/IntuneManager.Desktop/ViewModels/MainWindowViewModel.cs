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
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace IntuneManager.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ProfileService _profileService;
    private readonly IntuneGraphClientFactory _graphClientFactory;
    private readonly IExportService _exportService;

    private GraphServiceClient? _graphClient;
    private IConfigurationProfileService? _configProfileService;
    private IImportService? _importService;
    private ICompliancePolicyService? _compliancePolicyService;
    private IApplicationService? _applicationService;

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

    // --- Navigation ---
    [ObservableProperty]
    private NavCategory? _selectedCategory;

    public ObservableCollection<NavCategory> NavCategories { get; } =
    [
        new NavCategory { Name = "Overview", Icon = "📊" },
        new NavCategory { Name = "Device Configurations", Icon = "⚙" },
        new NavCategory { Name = "Compliance Policies", Icon = "✓" },
        new NavCategory { Name = "Applications", Icon = "📦" },
        new NavCategory { Name = "Application Assignments", Icon = "📋" }
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

    // --- Application Assignments (flattened view) ---
    [ObservableProperty]
    private ObservableCollection<AppAssignmentRow> _appAssignmentRows = [];

    [ObservableProperty]
    private AppAssignmentRow? _selectedAppAssignmentRow;

    private bool _appAssignmentsLoaded;

    // --- Detail pane ---
    [ObservableProperty]
    private ObservableCollection<AssignmentDisplayItem> _selectedItemAssignments = [];

    [ObservableProperty]
    private string _selectedItemTypeName = "";

    [ObservableProperty]
    private bool _isLoadingDetails;

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

    private void ApplyFilter()
    {
        var q = SearchText.Trim();

        if (string.IsNullOrEmpty(q))
        {
            FilteredDeviceConfigurations = new ObservableCollection<DeviceConfiguration>(DeviceConfigurations);
            FilteredCompliancePolicies = new ObservableCollection<DeviceCompliancePolicy>(CompliancePolicies);
            FilteredApplications = new ObservableCollection<MobileApp>(Applications);
            FilteredAppAssignmentRows = new ObservableCollection<AppAssignmentRow>(AppAssignmentRows);
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
        IExportService exportService)
    {
        _profileService = profileService;
        _graphClientFactory = graphClientFactory;
        _exportService = exportService;

        LoginViewModel = new LoginViewModel(profileService, graphClientFactory);
        LoginViewModel.LoginSucceeded += OnLoginSucceeded;

        CurrentView = LoginViewModel;

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
        LoginViewModel = null!;
    }

    // --- Navigation ---

    // Computed visibility helpers for the view
    public bool IsOverviewCategory => SelectedCategory?.Name == "Overview";
    public bool IsDeviceConfigCategory => SelectedCategory?.Name == "Device Configurations";
    public bool IsCompliancePolicyCategory => SelectedCategory?.Name == "Compliance Policies";
    public bool IsApplicationCategory => SelectedCategory?.Name == "Applications";
    public bool IsAppAssignmentsCategory => SelectedCategory?.Name == "Application Assignments";

    partial void OnSelectedCategoryChanged(NavCategory? value)
    {
        // Clear selections when switching categories
        SelectedConfiguration = null;
        SelectedCompliancePolicy = null;
        SelectedApplication = null;
        SelectedAppAssignmentRow = null;
        SelectedItemAssignments.Clear();
        SelectedItemTypeName = "";
        SearchText = "";
        OnPropertyChanged(nameof(IsOverviewCategory));
        OnPropertyChanged(nameof(IsDeviceConfigCategory));
        OnPropertyChanged(nameof(IsCompliancePolicyCategory));
        OnPropertyChanged(nameof(IsApplicationCategory));
        OnPropertyChanged(nameof(IsAppAssignmentsCategory));
        OnPropertyChanged(nameof(ActiveColumns));

        // Lazy-load assignments when navigating to tabs that require them
        if ((value?.Name == "Application Assignments" || value?.Name == "Overview") && !_appAssignmentsLoaded)
            _ = LoadAppAssignmentRowsAsync();
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

    partial void OnSelectedApplicationChanged(MobileApp? value)
    {
        SelectedItemAssignments.Clear();
        SelectedItemTypeName = FriendlyODataType(value?.OdataType);
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
            SetError($"Failed to load Application Assignments: {ex.Message}");
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

        try
        {
            ActiveProfile = profile;
            IsConnected = true;
            WindowTitle = $"IntuneManager - {profile.Name}";
            CurrentView = null;

            _graphClient = await _graphClientFactory.CreateClientAsync(profile);
            _configProfileService = new ConfigurationProfileService(_graphClient);
            _compliancePolicyService = new CompliancePolicyService(_graphClient);
            _applicationService = new ApplicationService(_graphClient);
            _importService = new ImportService(_configProfileService, _compliancePolicyService);

            RefreshSwitcherProfiles();
            SelectedSwitchProfile = profile;

            // Default to first nav category
            SelectedCategory = NavCategories.FirstOrDefault();

            StatusText = $"Connected to {profile.Name}";
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            SetError($"Connection failed: {ex.Message}");
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

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        ClearError();
        IsBusy = true;

        try
        {
            if (_configProfileService != null)
            {
                StatusText = "Loading device configurations...";
                var configs = await _configProfileService.ListDeviceConfigurationsAsync(cancellationToken);
                DeviceConfigurations = new ObservableCollection<DeviceConfiguration>(configs);
            }

            if (_compliancePolicyService != null)
            {
                StatusText = "Loading compliance policies...";
                var policies = await _compliancePolicyService.ListCompliancePoliciesAsync(cancellationToken);
                CompliancePolicies = new ObservableCollection<DeviceCompliancePolicy>(policies);
            }

            if (_applicationService != null)
            {
                StatusText = "Loading applications...";
                var apps = await _applicationService.ListApplicationsAsync(cancellationToken);
                Applications = new ObservableCollection<MobileApp>(apps);
            }

            var totalItems = DeviceConfigurations.Count + CompliancePolicies.Count + Applications.Count;
            StatusText = $"Loaded {totalItems} item(s) ({DeviceConfigurations.Count} configs, {CompliancePolicies.Count} policies, {Applications.Count} apps)";

            ApplyFilter();

            // Reset assignment state; actual loading is triggered lazily when needed
            _appAssignmentsLoaded = false;
        }
        catch (Exception ex)
        {
            SetError($"Failed to load data: {ex.Message}");
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
            SetError($"Export failed: {ex.Message}");
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
            SetError($"Export failed: {ex.Message}");
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
            SetError($"Import failed: {ex.Message}");
            StatusText = "Import failed";
        }
        finally
        {
            IsBusy = false;
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
        SelectedItemAssignments.Clear();
        SelectedItemTypeName = "";
        _graphClient = null;
        _configProfileService = null;
        _compliancePolicyService = null;
        _applicationService = null;
        _importService = null;
    }
}
