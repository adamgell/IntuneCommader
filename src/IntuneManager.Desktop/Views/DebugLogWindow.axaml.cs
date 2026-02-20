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
using IntuneManager.Desktop.Services;
using IntuneManager.Desktop.ViewModels;

namespace IntuneManager.Desktop.Views;

public partial class DebugLogWindow : Window
{
    private readonly DebugLogViewModel _viewModel;
    private readonly ListBox? _listBox;

    public DebugLogWindow()
    {
        InitializeComponent();
        _viewModel = new DebugLogViewModel();
        DataContext = _viewModel;

        // Auto-scroll to bottom when new entries are added
        _listBox = this.FindControl<ListBox>("LogListBox");
        if (_listBox != null)
        {
            var vm = (DebugLogViewModel)DataContext;
            vm.LogEntries.CollectionChanged += (_, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Add && vm.LogEntries.Count > 0)
                {
                    _listBox.ScrollIntoView(vm.LogEntries[^1]);
                }
            };
        }
    }

    private async void OnCopySelected(object? sender, RoutedEventArgs e)
    {
        if (_listBox == null || _viewModel.LogEntries.Count == 0)
            return;

        var selected = _listBox.SelectedItems?.OfType<string>().ToList();
        if (selected == null || selected.Count == 0)
        {
            selected = _viewModel.LogEntries.ToList();
        }

        await CopyToClipboardAsync(selected);
    }

    private Task OnCopyAll(object? sender, RoutedEventArgs e)
    {
        return CopyToClipboardAsync(_viewModel.LogEntries);
    }

    private async void OnSaveLog(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.LogEntries.Count == 0)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null)
            return;

        var entries = _viewModel.LogEntries.ToList();
        var result = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Debug Log",
            SuggestedFileName = $"IntuneCommander-DebugLog-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        });

        if (result?.TryGetLocalPath() is { } path)
        {
            var content = string.Join(Environment.NewLine, entries);
            await File.WriteAllTextAsync(path, content);
            DebugLogService.Instance.Log($"Log exported to {path}");
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
}
