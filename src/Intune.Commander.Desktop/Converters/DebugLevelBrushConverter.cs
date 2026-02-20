using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Intune.Commander.Desktop.Models;

namespace Intune.Commander.Desktop.Converters;

/// <summary>Maps a <see cref="DebugLogLevel"/> to a badge background brush.</summary>
public sealed class DebugLevelBrushConverter : IValueConverter
{
    public static readonly DebugLevelBrushConverter Instance = new();

    private static readonly IBrush DebugBrush   = new SolidColorBrush(Color.Parse("#6c757d"));
    private static readonly IBrush InfoBrush    = new SolidColorBrush(Color.Parse("#0d6efd"));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#fd7e14"));
    private static readonly IBrush ErrorBrush   = new SolidColorBrush(Color.Parse("#dc3545"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            DebugLogLevel.Debug   => DebugBrush,
            DebugLogLevel.Info    => InfoBrush,
            DebugLogLevel.Warning => WarningBrush,
            DebugLogLevel.Error   => ErrorBrush,
            _                     => DebugBrush
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
