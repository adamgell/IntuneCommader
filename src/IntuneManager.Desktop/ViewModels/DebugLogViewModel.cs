using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntuneManager.Desktop.Models;
using IntuneManager.Desktop.Services;

namespace IntuneManager.Desktop.ViewModels;

public partial class DebugLogViewModel : ObservableObject
{
    public ObservableCollection<DebugLogEntry> LogEntries => DebugLogService.Instance.Entries;

    public ObservableCollection<string> Categories { get; } = new() { "All" };
    public ObservableCollection<DebugLogEntry> FilteredEntries { get; } = new();
    public ObservableCollection<DebugLogGroup> GroupedEntries { get; } = new();

    [ObservableProperty]
    private string? searchText;

    [ObservableProperty]
    private string selectedCategory = "All";

    [ObservableProperty]
    private bool showDebug = true;

    [ObservableProperty]
    private bool showInfo = true;

    [ObservableProperty]
    private bool showWarning = true;

    [ObservableProperty]
    private bool showError = true;

    [ObservableProperty]
    private bool autoScroll = true;

    public DebugLogViewModel()
    {
        LogEntries.CollectionChanged += OnLogEntriesChanged;
        RefreshCategories();
        ApplyFilters();
    }

    partial void OnSearchTextChanged(string? value) => ApplyFilters();
    partial void OnSelectedCategoryChanged(string value) => ApplyFilters();
    partial void OnShowDebugChanged(bool value) => ApplyFilters();
    partial void OnShowInfoChanged(bool value) => ApplyFilters();
    partial void OnShowWarningChanged(bool value) => ApplyFilters();
    partial void OnShowErrorChanged(bool value) => ApplyFilters();

    [RelayCommand]
    private void ClearLog()
    {
        DebugLogService.Instance.Clear();
    }

    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (var item in e.NewItems.OfType<DebugLogEntry>())
            {
                if (!Categories.Contains(item.Category))
                    Categories.Add(item.Category);
            }
        }
        RefreshCategories();
        ApplyFilters();
    }

    private void RefreshCategories()
    {
        var existing = new HashSet<string>(Categories);
        foreach (var cat in LogEntries.Select(x => x.Category).Distinct())
        {
            if (!existing.Contains(cat))
                Categories.Add(cat);
        }
    }

    private void ApplyFilters()
    {
        var filtered = LogEntries.Where(PassesFilters).ToList();

        FilteredEntries.Clear();
        foreach (var entry in filtered)
            FilteredEntries.Add(entry);

        var grouped = filtered.GroupBy(e => e.Category)
            .Select(g => new DebugLogGroup(g.Key, g.ToList()))
            .OrderBy(g => g.Category)
            .ToList();

        GroupedEntries.Clear();
        foreach (var group in grouped)
            GroupedEntries.Add(group);
    }

    private bool PassesFilters(DebugLogEntry entry)
    {
        if (!ShowDebug && entry.Level == DebugLogLevel.Debug) return false;
        if (!ShowInfo && entry.Level == DebugLogLevel.Info) return false;
        if (!ShowWarning && entry.Level == DebugLogLevel.Warning) return false;
        if (!ShowError && entry.Level == DebugLogLevel.Error) return false;

        if (SelectedCategory != "All" && !string.Equals(entry.Category, SelectedCategory, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            if (!entry.Message.Contains(term, StringComparison.OrdinalIgnoreCase)
                && !entry.Category.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}

public sealed record DebugLogGroup(string Category, IReadOnlyList<DebugLogEntry> Entries);
