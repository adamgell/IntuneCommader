using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Intune.Commander.Core.Models;
using Intune.Commander.Core.Services;
using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Desktop.ViewModels;


/// <summary>
/// ViewModel for the Assignment Report window.
/// Mirrors the 10 report modes in the IntuneAssignmentChecker PowerShell script.
/// </summary>
public partial class AssignmentReportViewModel : ViewModelBase
{
    private readonly IAssignmentCheckerService _checkerService;
    private readonly IGroupService _groupService;
    private readonly IUserService _userService;

    // ── Tabs / modes ─────────────────────────────────────────────────────────────

    public static readonly string[] ReportModes =
    [
        "User Assignments",
        "Group Assignments",
        "Device Assignments",
        "All Policies Overview",
        "All Users Assignments",
        "All Devices Assignments",
        "Unassigned Policies",
        "Empty Group Assignments",
        "Compare Groups",
        "Failed Assignments"
    ];

    /// <summary>Instance-level accessor for compiled bindings in AXAML.</summary>
    public string[] ReportModeItems => ReportModes;

    [ObservableProperty]
    private int _selectedModeIndex;

    public string SelectedModeName => ReportModes[SelectedModeIndex];

    // ── Input fields ─────────────────────────────────────────────────────────────

    // User search (mode 0)
    [ObservableProperty]
    private string _userSearchQuery = "";
    [ObservableProperty]
    private bool _isSearchingUser;
    [ObservableProperty]
    private string _selectedUsersInfo = "";
    [ObservableProperty]
    private bool _showNoUserResults;
    public ObservableCollection<User> UserSearchResults { get; } = [];
    public ObservableCollection<User> SelectedUsers { get; } = [];

    [ObservableProperty]
    private string _deviceInput = "";

    // Group Assignment mode
    [ObservableProperty]
    private string _groupSearchQuery = "";
    [ObservableProperty]
    private bool _isSearchingGroup;
    [ObservableProperty]
    private Group? _selectedGroup;
    [ObservableProperty]
    private string _selectedGroupInfo = "";
    public ObservableCollection<Group> GroupSearchResults { get; } = [];

    // Compare Groups mode
    [ObservableProperty]
    private string _compareGroup1Query = "";
    [ObservableProperty]
    private string _compareGroup2Query = "";
    [ObservableProperty]
    private bool _isSearchingCompareGroup1;
    [ObservableProperty]
    private bool _isSearchingCompareGroup2;
    [ObservableProperty]
    private Group? _selectedCompareGroup1;
    [ObservableProperty]
    private Group? _selectedCompareGroup2;
    [ObservableProperty]
    private string _selectedCompareGroup1Info = "";
    [ObservableProperty]
    private string _selectedCompareGroup2Info = "";
    public ObservableCollection<Group> CompareGroup1Results { get; } = [];
    public ObservableCollection<Group> CompareGroup2Results { get; } = [];

    // ── Status ───────────────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _statusText = "Select a report mode and provide input, then click Run Report.";

    [ObservableProperty]
    private bool _isRunning;

    // ── Results ──────────────────────────────────────────────────────────────────

    public ObservableCollection<AssignmentReportRow> Results { get; } = [];

    public bool CanExport => !IsRunning && ResultCount > 0;

    [ObservableProperty]
    private int _resultCount;

    [ObservableProperty]
    private string _resultSummary = "";

    // ── Column visibility (driven by mode) ──────────────────────────────────────

    public bool ShowAssignmentSummary =>
        SelectedModeIndex is 3 or 4 or 5;  // All Policies, All Users, All Devices

    public bool ShowAssignmentReason =>
        SelectedModeIndex is 0 or 1 or 2;  // User, Group, Device

    public bool ShowGroupColumns =>
        SelectedModeIndex == 7;  // Empty Groups

    public bool ShowCompareColumns =>
        SelectedModeIndex == 8;  // Compare Groups

    public bool ShowFailureColumns =>
        SelectedModeIndex == 9;  // Failed Assignments

    // ── Input visibility ─────────────────────────────────────────────────────────

    public bool ShowUserInput => SelectedModeIndex == 0;
    public bool ShowGroupSearch => SelectedModeIndex == 1;
    public bool ShowDeviceInput => SelectedModeIndex == 2;
    public bool ShowCompareGroupInputs => SelectedModeIndex == 8;

    /// <summary>Auto-run modes (no user input required).</summary>
    public bool IsAutoRunMode =>
        SelectedModeIndex is 3 or 4 or 5 or 6 or 7 or 9;

    private CancellationTokenSource? _cts;

    public AssignmentReportViewModel(
        IAssignmentCheckerService checkerService,
        IGroupService groupService,
        IUserService userService)
    {
        _checkerService = checkerService;
        _groupService = groupService;
        _userService = userService;
    }

    // ── User search (mode 0) ─────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SearchUserAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(UserSearchQuery)) return;
        IsSearchingUser = true;
        ShowNoUserResults = false;
        UserSearchResults.Clear();
        SelectedUsers.Clear();
        SelectedUsersInfo = "";
        try
        {
            var users = await _userService.SearchUsersAsync(UserSearchQuery.Trim(), cancellationToken);
            foreach (var u in users) UserSearchResults.Add(u);
            // Auto-select when exactly one result, mirroring group search behaviour
            if (users.Count == 1)
                UpdateSelectedUsers([users[0]]);
            else if (users.Count == 0)
                ShowNoUserResults = true;
        }
        catch (Exception ex) { SetError($"User search failed: {ex.Message}"); }
        finally { IsSearchingUser = false; }
    }

    /// <summary>Called from code-behind when the results ListBox selection changes.</summary>
    public void UpdateSelectedUsers(IEnumerable<User> selected)
    {
        SelectedUsers.Clear();
        foreach (var u in selected) SelectedUsers.Add(u);
        SelectedUsersInfo = SelectedUsers.Count switch
        {
            0 => "",
            1 => $"{SelectedUsers[0].DisplayName} — {SelectedUsers[0].UserPrincipalName}",
            _ => $"{SelectedUsers.Count} users selected: {string.Join(", ", SelectedUsers.Select(u => u.UserPrincipalName ?? u.DisplayName))}"
        };
    }

    // ── Mode selection ───────────────────────────────────────────────────────────

    partial void OnSelectedModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedModeName));
        OnPropertyChanged(nameof(ShowUserInput));
        OnPropertyChanged(nameof(ShowGroupSearch));
        OnPropertyChanged(nameof(ShowDeviceInput));
        OnPropertyChanged(nameof(ShowCompareGroupInputs));
        OnPropertyChanged(nameof(IsAutoRunMode));
        OnPropertyChanged(nameof(ShowAssignmentSummary));
        OnPropertyChanged(nameof(ShowAssignmentReason));
        OnPropertyChanged(nameof(ShowGroupColumns));
        OnPropertyChanged(nameof(ShowCompareColumns));
        OnPropertyChanged(nameof(ShowFailureColumns));

        // Clear results and user search state when switching modes
        Results.Clear();
        ResultCount = 0;
        ResultSummary = "";
        UserSearchResults.Clear();
        SelectedUsers.Clear();
        SelectedUsersInfo = "";
        ShowNoUserResults = false;
        ClearError();
        OnPropertyChanged(nameof(CanExport));
        StatusText = IsAutoRunMode
            ? "Click 'Run Report' to fetch data."
            : GetInputPrompt(value);
    }

    private static string GetInputPrompt(int modeIndex) => modeIndex switch
    {
        0 => "Search for users by name or UPN, select one or more, then click Run Report.",
        1 => "Search for a group by name or GUID, select it, then click Run Report.",
        2 => "Enter a device name prefix (e.g. 'LAPTOP-') and click Run Report. Matches all devices with that prefix.",
        8 => "Search for two groups to compare, select them, then click Run Report.",
        _ => "Click 'Run Report' to fetch data."
    };

    // ── Group search (Assignment mode) ───────────────────────────────────────────

    [RelayCommand]
    private async Task SearchGroupAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(GroupSearchQuery)) return;
        IsSearchingGroup = true;
        GroupSearchResults.Clear();
        SelectedGroup = null;
        SelectedGroupInfo = "";
        try
        {
            var groups = await _groupService.SearchGroupsAsync(GroupSearchQuery.Trim(), cancellationToken);
            foreach (var g in groups) GroupSearchResults.Add(g);
            if (groups.Count == 1) SelectedGroup = groups[0];
        }
        catch (Exception ex) { SetError($"Group search failed: {ex.Message}"); }
        finally { IsSearchingGroup = false; }
    }

    partial void OnSelectedGroupChanged(Group? value)
    {
        SelectedGroupInfo = value != null
            ? $"{value.DisplayName}  ({GroupService.InferGroupType(value)})  •  {value.Id}"
            : "";
    }

    // ── Compare-groups search ────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SearchCompareGroup1Async(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(CompareGroup1Query)) return;
        IsSearchingCompareGroup1 = true;
        CompareGroup1Results.Clear();
        SelectedCompareGroup1 = null;
        SelectedCompareGroup1Info = "";
        try
        {
            var groups = await _groupService.SearchGroupsAsync(CompareGroup1Query.Trim(), cancellationToken);
            foreach (var g in groups) CompareGroup1Results.Add(g);
            if (groups.Count == 1) SelectedCompareGroup1 = groups[0];
        }
        catch (Exception ex) { SetError($"Group 1 search failed: {ex.Message}"); }
        finally { IsSearchingCompareGroup1 = false; }
    }

    [RelayCommand]
    private async Task SearchCompareGroup2Async(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(CompareGroup2Query)) return;
        IsSearchingCompareGroup2 = true;
        CompareGroup2Results.Clear();
        SelectedCompareGroup2 = null;
        SelectedCompareGroup2Info = "";
        try
        {
            var groups = await _groupService.SearchGroupsAsync(CompareGroup2Query.Trim(), cancellationToken);
            foreach (var g in groups) CompareGroup2Results.Add(g);
            if (groups.Count == 1) SelectedCompareGroup2 = groups[0];
        }
        catch (Exception ex) { SetError($"Group 2 search failed: {ex.Message}"); }
        finally { IsSearchingCompareGroup2 = false; }
    }

    partial void OnSelectedCompareGroup1Changed(Group? value)
    {
        SelectedCompareGroup1Info = value != null
            ? $"{value.DisplayName}  •  {value.Id}"
            : "";
    }

    partial void OnSelectedCompareGroup2Changed(Group? value)
    {
        SelectedCompareGroup2Info = value != null
            ? $"{value.DisplayName}  •  {value.Id}"
            : "";
    }

    // ── Run Report command ────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task RunReportAsync(CancellationToken cancellationToken)
    {
        if (IsRunning) _cts?.Cancel();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _cts.Token;

        ClearError();
        IsRunning = true;
        IsBusy = true;
        Results.Clear();
        ResultCount = 0;
        ResultSummary = "";

        DebugLog.Log("Report", $"Starting report: {SelectedModeName}");

        // Stream rows into the DataGrid as they are found
        void OnRow(AssignmentReportRow row) =>
            Dispatcher.UIThread.Post(() =>
            {
                Results.Add(row);
                ResultCount = Results.Count;
            });

        try
        {
            List<AssignmentReportRow> rows = SelectedModeIndex switch
            {
                0 => await RunUserReportAsync(ct, OnRow),
                1 => await RunGroupReportAsync(ct, OnRow),
                2 => await RunDeviceReportAsync(ct, OnRow),
                3 => await _checkerService.GetAllPoliciesWithAssignmentsAsync(ReportProgress, OnRow, ct),
                4 => await _checkerService.GetAllUsersAssignmentsAsync(ReportProgress, OnRow, ct),
                5 => await _checkerService.GetAllDevicesAssignmentsAsync(ReportProgress, OnRow, ct),
                6 => await _checkerService.GetUnassignedPoliciesAsync(ReportProgress, OnRow, ct),
                7 => await _checkerService.GetEmptyGroupAssignmentsAsync(ReportProgress, OnRow, ct),
                8 => await RunCompareReportAsync(ct, OnRow),
                9 => await _checkerService.GetFailedAssignmentsAsync(ReportProgress, OnRow, ct),
                _ => []
            };

            ResultCount = rows.Count;
            ResultSummary = BuildSummary(rows);
            var statusMsg = ResultCount == 0 ? "No results found." : $"Found {ResultCount} result(s).";
            StatusText = statusMsg;
            DebugLog.Log("Report", $"Completed: {SelectedModeName} — {statusMsg}");
            if (!string.IsNullOrEmpty(ResultSummary))
                DebugLog.Log("Report", $"Summary: {ResultSummary}");
        }
        catch (OperationCanceledException)
        {
            StatusText = "Report cancelled.";
            DebugLog.Log("Report", $"Cancelled: {SelectedModeName}");
        }
        catch (Exception ex)
        {
            SetError($"Report failed: {ex.Message}");
            StatusText = "Report failed — see error above.";
            DebugLog.Log("Report", $"{SelectedModeName} failed: {ex.Message}");
            DebugLog.LogError($"[Report] {SelectedModeName}", ex);
        }
        finally
        {
            IsRunning = false;
            IsBusy = false;
            OnPropertyChanged(nameof(CanExport));
        }
    }

    [RelayCommand]
    private void CancelReport()
    {
        _cts?.Cancel();
        StatusText = "Cancelling...";
    }

    [RelayCommand]
    private async Task PrefetchAllAsync(CancellationToken cancellationToken)
    {
        if (IsRunning) return;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _cts.Token;

        ClearError();
        IsRunning = true;
        IsBusy = true;

        DebugLog.Log("Cache", "Starting full policy prefetch...");
        try
        {
            await _checkerService.PrefetchAllToCacheAsync(ReportProgress, ct);
            StatusText = "All policy data downloaded and cached. Reports will now run faster.";
            DebugLog.Log("Cache", "Prefetch complete.");
        }
        catch (OperationCanceledException)
        {
            StatusText = "Download cancelled.";
            DebugLog.Log("Cache", "Prefetch cancelled.");
        }
        catch (Exception ex)
        {
            SetError($"Download failed: {ex.Message}");
            DebugLog.LogError($"[Cache] Prefetch failed", ex);
        }
        finally
        {
            IsRunning = false;
            IsBusy = false;
        }
    }

    // ── Per-mode runners ─────────────────────────────────────────────────────────

    private async Task<List<AssignmentReportRow>> RunUserReportAsync(
        CancellationToken ct, Action<AssignmentReportRow> onRow)
    {
        if (SelectedUsers.Count == 0)
            throw new InvalidOperationException("Please search for and select at least one user.");

        var all = new List<AssignmentReportRow>();
        var multiUser = SelectedUsers.Count > 1;
        foreach (var user in SelectedUsers)
        {
            var upn = user.UserPrincipalName ?? user.Id!;
            ReportProgress($"Checking assignments for {upn}...");
            // For multi-user, transform each row to stamp the UPN before streaming
            Action<AssignmentReportRow> rowCallback = multiUser
                ? r => onRow(r with { UserPrincipalName = upn })
                : onRow;
            var rows = await _checkerService.GetUserAssignmentsAsync(upn, ReportProgress, rowCallback, ct);
            if (multiUser)
                foreach (var r in rows) all.Add(r with { UserPrincipalName = upn });
            else
                all.AddRange(rows);
        }
        return all;
    }

    private async Task<List<AssignmentReportRow>> RunGroupReportAsync(
        CancellationToken ct, Action<AssignmentReportRow> onRow)
    {
        if (SelectedGroup?.Id == null)
            throw new InvalidOperationException("Please search for and select a group.");
        return await _checkerService.GetGroupAssignmentsAsync(
            SelectedGroup.Id, SelectedGroup.DisplayName ?? SelectedGroup.Id, ReportProgress, onRow, ct);
    }

    private async Task<List<AssignmentReportRow>> RunDeviceReportAsync(
        CancellationToken ct, Action<AssignmentReportRow> onRow)
    {
        var deviceName = DeviceInput.Trim();
        if (string.IsNullOrEmpty(deviceName))
            throw new InvalidOperationException("Please enter a device name prefix.");

        // Support comma-separated multiple search terms (each may match multiple devices)
        var terms = deviceName.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var all = new List<AssignmentReportRow>();
        foreach (var term in terms)
        {
            ReportProgress($"Searching devices matching '{term}'...");
            // Service already stamps TargetDevice and streams rows via onRow
            var rows = await _checkerService.GetDeviceAssignmentsAsync(term, ReportProgress, onRow, ct);
            all.AddRange(rows);
        }
        return all;
    }

    private async Task<List<AssignmentReportRow>> RunCompareReportAsync(
        CancellationToken ct, Action<AssignmentReportRow> onRow)
    {
        if (SelectedCompareGroup1?.Id == null || SelectedCompareGroup2?.Id == null)
            throw new InvalidOperationException("Please select both groups to compare.");

        return await _checkerService.CompareGroupAssignmentsAsync(
            SelectedCompareGroup1.Id,
            SelectedCompareGroup1.DisplayName ?? SelectedCompareGroup1.Id,
            SelectedCompareGroup2.Id,
            SelectedCompareGroup2.DisplayName ?? SelectedCompareGroup2.Id,
            ReportProgress, onRow, ct);
    }

    // ── Export ────────────────────────────────────────────────────────────────────

    public string GenerateHtml() =>
        AssignmentReportExporter.GenerateHtml(SelectedModeName, Results.ToList());

    public string GenerateCsv() =>
        AssignmentReportExporter.GenerateCsv(SelectedModeName, Results.ToList());

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private void ReportProgress(string message)
    {
        DebugLog.Log("Report", message);
        Dispatcher.UIThread.Post(() => StatusText = message);
    }

    private static string BuildSummary(List<AssignmentReportRow> rows)
    {
        if (rows.Count == 0) return "";
        var byType = rows.GroupBy(r => r.PolicyType)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Count()} {g.Key}");
        return string.Join("  •  ", byType);
    }
}
