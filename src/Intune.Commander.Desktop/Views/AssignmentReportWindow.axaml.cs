using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Intune.Commander.Desktop.Services;
using Intune.Commander.Desktop.ViewModels;
using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Desktop.Views;

public partial class AssignmentReportWindow : Window
{
    private DataGrid? _grid;

    public AssignmentReportWindow()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _grid = this.FindControl<DataGrid>("ResultsGrid");

        if (DataContext is AssignmentReportViewModel vm)
        {
            vm.PropertyChanged += OnVmPropertyChanged;
            UpdateColumnVisibility(vm);
        }

        LoadWindowPlacement();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        SaveWindowPlacement();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AssignmentReportViewModel.SelectedModeIndex)
            && sender is AssignmentReportViewModel vm)
        {
            UpdateColumnVisibility(vm);
        }
    }

    private void UpdateColumnVisibility(AssignmentReportViewModel vm)
    {
        if (_grid == null) return;

        // Map column header text to whether it should be visible for this mode.
        var visibility = new Dictionary<string, bool>
        {
            ["Assignments"]        = vm.ShowAssignmentSummary,
            ["Assignment Reason"]  = vm.ShowAssignmentReason,
            ["Empty Group"]        = vm.ShowGroupColumns,
            ["Group ID"]           = vm.ShowGroupColumns,
            ["Group 1 Status"]     = vm.ShowCompareColumns,
            ["Group 2 Status"]     = vm.ShowCompareColumns,
            // Device column: show for both Device Assignments mode (2) and Failed Assignments (9)
            ["Device"]             = vm.ShowFailureColumns || vm.SelectedModeIndex == 2,
            ["Status"]             = vm.ShowFailureColumns,
            ["User"]               = vm.ShowFailureColumns || vm.SelectedModeIndex == 0,
            ["Last Reported"]      = vm.ShowFailureColumns,
        };

        foreach (var col in _grid.Columns)
        {
            var header = col.Header?.ToString() ?? "";
            if (visibility.TryGetValue(header, out var isVisible))
                col.IsVisible = isVisible;
        }
    }

    // ── Keyboard shortcuts ───────────────────────────────────────────────────────

    private void OnUserSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not AssignmentReportViewModel vm) return;
        // If users are already selected, Enter runs the report instead of re-searching
        // (re-searching would clear the selection and lose it)
        if (vm.SelectedUsers.Count > 0)
            ExecuteRunReport();
        else if (vm.SearchUserCommand.CanExecute(null))
            vm.SearchUserCommand.Execute(null);
    }

    private void OnUserResultsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox lb || DataContext is not AssignmentReportViewModel vm) return;
        vm.UpdateSelectedUsers(lb.SelectedItems?.OfType<User>() ?? []);
    }

    private void OnDeviceInputBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ExecuteRunReport();
    }

    private void OnGroupSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not AssignmentReportViewModel vm) return;
        // If a group is already selected, Enter runs the report instead of re-searching
        // (re-searching clears SelectedGroup first, destroying the selection)
        if (vm.SelectedGroup != null)
            ExecuteRunReport();
        else if (vm.SearchGroupCommand.CanExecute(null))
            vm.SearchGroupCommand.Execute(null);
    }

    private void OnCompare1BoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not AssignmentReportViewModel vm) return;
        // If both compare groups are already selected, Enter runs the report
        if (vm.SelectedCompareGroup1 != null && vm.SelectedCompareGroup2 != null)
            ExecuteRunReport();
        else if (vm.SearchCompareGroup1Command.CanExecute(null))
            vm.SearchCompareGroup1Command.Execute(null);
    }

    private void OnCompare2BoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not AssignmentReportViewModel vm) return;
        // If both compare groups are already selected, Enter runs the report
        if (vm.SelectedCompareGroup1 != null && vm.SelectedCompareGroup2 != null)
            ExecuteRunReport();
        else if (vm.SearchCompareGroup2Command.CanExecute(null))
            vm.SearchCompareGroup2Command.Execute(null);
    }

    private void ExecuteRunReport()
    {
        if (DataContext is AssignmentReportViewModel vm && vm.RunReportCommand.CanExecute(null))
            vm.RunReportCommand.Execute(null);
    }

    // ── Export ───────────────────────────────────────────────────────────────────

    private async void OnExportHtmlClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AssignmentReportViewModel vm || !vm.CanExport) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save HTML Report",
            SuggestedFileName = $"Assignment-Report-{timestamp}.html",
            FileTypeChoices = [new FilePickerFileType("HTML File") { Patterns = ["*.html"] }]
        });

        if (file?.TryGetLocalPath() is not { } path) return;

        try
        {
            var html = vm.GenerateHtml();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, html, Encoding.UTF8);
            vm.StatusText = $"HTML report saved: {Path.GetFileName(path)}";
            DebugLogService.Instance.Log("Export", $"HTML report exported to {path}");
        }
        catch (Exception ex)
        {
            vm.StatusText = $"Export failed: {ex.Message}";
            DebugLogService.Instance.LogError($"HTML export failed: {ex.Message}", ex);
        }
    }

    private async void OnExportCsvClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AssignmentReportViewModel vm || !vm.CanExport) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export CSV",
            SuggestedFileName = $"Assignment-Report-{timestamp}.csv",
            FileTypeChoices = [new FilePickerFileType("CSV File") { Patterns = ["*.csv"] }]
        });

        if (file?.TryGetLocalPath() is not { } path) return;

        try
        {
            var csv = vm.GenerateCsv();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            vm.StatusText = $"CSV exported: {Path.GetFileName(path)}";
            DebugLogService.Instance.Log("Export", $"CSV exported to {path}");
        }
        catch (Exception ex)
        {
            vm.StatusText = $"Export failed: {ex.Message}";
            DebugLogService.Instance.LogError($"CSV export failed: {ex.Message}", ex);
        }
    }

    // ── Window placement persistence ─────────────────────────────────────────────

    private static readonly string PlacementFile = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Intune.Commander",
        "assignment-report-window.json");

    private void SaveWindowPlacement()
    {
        try
        {
            var pos = Position;
            var size = ClientSize;
            var csv = $"{pos.X},{pos.Y},{(int)size.Width},{(int)size.Height}";
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PlacementFile)!);
            System.IO.File.WriteAllText(PlacementFile, csv);
        }
        catch { /* ignore */ }
    }

    private void LoadWindowPlacement()
    {
        try
        {
            if (!System.IO.File.Exists(PlacementFile)) return;
            var parts = System.IO.File.ReadAllText(PlacementFile).Split(',');
            if (parts.Length < 4) return;
            if (int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y)
                && int.TryParse(parts[2], out var w) && int.TryParse(parts[3], out var h))
            {
                Position = new Avalonia.PixelPoint(x, y);
                Width = w;
                Height = h;
            }
        }
        catch { /* ignore */ }
    }
}
