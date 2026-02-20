using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using IntuneManager.Desktop.Models;
using IntuneManager.Desktop.Services;
using IntuneManager.Desktop.ViewModels;

namespace IntuneManager.Desktop.Views;

public partial class DebugLogWindow : Window
{
    private readonly DebugLogViewModel _viewModel;
    private readonly ListBox? _listBox;
    private readonly string _placementPath;

    public DebugLogWindow()
    {
        InitializeComponent();
        _viewModel = new DebugLogViewModel();
        DataContext = _viewModel;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _placementPath = Path.Combine(appData, "IntuneManager", "debuglog-window.json");

        _listBox = this.FindControl<ListBox>("LogListBox");
        if (_listBox != null)
        {
            var vm = (DebugLogViewModel)DataContext;
            vm.LogEntries.CollectionChanged += (_, e) =>
            {
                if (_viewModel.AutoScroll && e.Action == NotifyCollectionChangedAction.Add && vm.LogEntries.Count > 0)
                {
                    _listBox.ScrollIntoView(vm.LogEntries[^1]);
                }
            };
        }

        LoadPlacement();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        SavePlacement();
    }

    private async void OnCopySelected(object? sender, RoutedEventArgs e)
    {
        if (_listBox == null || _viewModel.LogEntries.Count == 0)
            return;

        var selected = _listBox.SelectedItems?.OfType<DebugLogEntry>().ToList();
        if (selected == null || selected.Count == 0)
        {
            selected = _viewModel.FilteredEntries.ToList();
        }

        await CopyToClipboardAsync(selected.Select(s => s.Formatted));
    }

    private Task OnCopyAll(object? sender, RoutedEventArgs e)
    {
        return CopyToClipboardAsync(_viewModel.FilteredEntries.Select(x => x.Formatted));
    }

    private async void OnSaveLog(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.FilteredEntries.Count == 0)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null)
            return;

        var entries = _viewModel.FilteredEntries.ToList();
        var result = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Debug Log",
            SuggestedFileName = $"IntuneCommander-DebugLog-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        });

        if (result?.TryGetLocalPath() is { } path)
        {
            var content = string.Join(Environment.NewLine, entries.Select(e => e.Formatted));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content);
            DebugLogService.Instance.Log(DebugLogLevel.Info, "Log", $"Log exported to {path}");
        }
    }

    private async Task CopyToClipboardAsync(IEnumerable<string> entries)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard == null)
            return;

        var text = string.Join(Environment.NewLine, entries);
        await topLevel.Clipboard.SetTextAsync(text);
    }

    private void LoadPlacement()
    {
        try
        {
            if (File.Exists(_placementPath))
            {
                var json = File.ReadAllText(_placementPath);
                var parts = json.Split(',');
                if (parts.Length == 4 &&
                    double.TryParse(parts[0], out var x) &&
                    double.TryParse(parts[1], out var y) &&
                    double.TryParse(parts[2], out var w) &&
                    double.TryParse(parts[3], out var h))
                {
                    Position = new PixelPoint((int)x, (int)y);
                    Width = w;
                    Height = h;
                }
            }
        }
        catch
        {
            // ignore placement load failures
        }
    }

    private void SavePlacement()
    {
        try
        {
            var dir = Path.GetDirectoryName(_placementPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var data = $"{Position.X},{Position.Y},{Width},{Height}";
            File.WriteAllText(_placementPath, data);
        }
        catch
        {
            // ignore placement save failures
        }
    }
}
