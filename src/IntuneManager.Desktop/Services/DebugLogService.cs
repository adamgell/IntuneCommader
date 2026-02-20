using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using IntuneManager.Desktop.Models;

namespace IntuneManager.Desktop.Services;

public sealed class DebugLogService
{
    private static readonly Lazy<DebugLogService> _instance = new(() => new DebugLogService());
    public static DebugLogService Instance => _instance.Value;

    public ObservableCollection<DebugLogEntry> Entries { get; } = new();

    private const int MaxEntries = 2000;

    private DebugLogService() { }

    public void Log(string message)
    {
        Log(DebugLogLevel.Info, "App", message);
    }

    public void Log(string category, string message)
    {
        Log(DebugLogLevel.Info, category, message);
    }

    public void Log(DebugLogLevel level, string category, string message)
    {
        var entry = new DebugLogEntry(DateTime.Now, category, level, message);

        if (Dispatcher.UIThread.CheckAccess())
        {
            AddEntry(entry);
        }
        else
        {
            Dispatcher.UIThread.Post(() => AddEntry(entry));
        }
    }

    public void LogError(string message, Exception? ex = null)
    {
        var detail = ex != null ? $"{message} — {ex.GetType().Name}: {ex.Message}" : message;
        Log(DebugLogLevel.Error, "Error", detail);
    }

    public void Clear()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Entries.Clear();
        }
        else
        {
            Dispatcher.UIThread.Post(() => Entries.Clear());
        }
    }

    private void AddEntry(DebugLogEntry entry)
    {
        Entries.Add(entry);
        while (Entries.Count > MaxEntries)
            Entries.RemoveAt(0);
    }
}
