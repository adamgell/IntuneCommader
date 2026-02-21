using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using IntuneManager.Desktop.Models;

namespace IntuneManager.Desktop.Services;

public static class AppSettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IntuneManager",
        "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load app settings from '{SettingsPath}': {ex}");
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var tempPath = Path.Combine(directory, Path.GetRandomFileName());

            File.WriteAllText(tempPath, json);

            if (File.Exists(SettingsPath))
            {
                // Atomically replace existing settings with the new file.
                File.Replace(tempPath, SettingsPath, destinationBackupFileName: null);
            }
            else
            {
                // First-time save: move the temp file into place.
                File.Move(tempPath, SettingsPath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save app settings to '{SettingsPath}': {ex}");
        }
    }
}
