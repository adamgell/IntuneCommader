using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using IntuneManager.Desktop.ViewModels;

namespace IntuneManager.Desktop.Converters;

/// <summary>
/// Converts an OData type string (e.g. "#microsoft.graph.win32LobApp")
/// into a friendly type name.
/// </summary>
public class ODataTypeConverter : IValueConverter
{
    public static readonly ODataTypeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string odataType || string.IsNullOrEmpty(odataType))
            return "";

        var name = odataType.Split('.')[^1];
        // Insert spaces before capitals: "win32LobApp" → "Win32 Lob App"
        var spaced = System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ");
        return char.ToUpper(spaced[0]) + spaced[1..];
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}

/// <summary>
/// Converts an OData type string into a platform name (Windows, iOS, macOS, Android, Web).
/// </summary>
public class PlatformConverter : IValueConverter
{
    public static readonly PlatformConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return MainWindowViewModel.InferPlatform(value as string);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}

/// <summary>
/// Converts a List&lt;string&gt; or IList&lt;string&gt; into a comma-separated string.
/// </summary>
public class StringListConverter : IValueConverter
{
    public static readonly StringListConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is System.Collections.Generic.IList<string> list && list.Count > 0)
            return string.Join(", ", list);
        return "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}

/// <summary>
/// Converts DateTime/DateTimeOffset (or parseable strings) into a human-readable local date/time.
/// </summary>
public class HumanDateTimeConverter : IValueConverter
{
    public static readonly HumanDateTimeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
            return "—";

        if (value is DateTimeOffset dto)
            return dto.ToLocalTime().ToString("MMM d, yyyy h:mm tt", culture);

        if (value is DateTime dt)
            return dt.ToLocalTime().ToString("MMM d, yyyy h:mm tt", culture);

        if (value is string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "—";

            if (DateTimeOffset.TryParse(text, culture, DateTimeStyles.AssumeUniversal, out var parsedDto))
                return parsedDto.ToLocalTime().ToString("MMM d, yyyy h:mm tt", culture);

            if (DateTime.TryParse(text, culture, DateTimeStyles.AssumeLocal, out var parsedDt))
                return parsedDt.ToLocalTime().ToString("MMM d, yyyy h:mm tt", culture);

            return text;
        }

        return value.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}
