using System;

namespace IntuneManager.Desktop.Models;

public enum DebugLogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public sealed record DebugLogEntry(DateTime Timestamp, string Category, DebugLogLevel Level, string Message)
{
    public string Formatted => $"[{Timestamp:HH:mm:ss.fff}] [{Category}] {Message}";
}
