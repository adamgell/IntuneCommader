using Microsoft.Graph.Beta.Models;

namespace CmProjectX.Api;

// Projects the Graph `MobileApp` union type into the flat AppListItemDto the client
// renders. The AppType/Platform switch logic is ported from IntuneCommander's
// ApplicationDataMapper so the labels match what users saw in the original app.
internal static class ApplicationMapper
{
    public static AppListItemDto MapAppListItem(MobileApp a) => new(
        a.Id ?? "",
        a.DisplayName ?? "(unnamed)",
        a.Description,
        a.Publisher,
        FormatAppType(a),
        DetectPlatform(a),
        a.CreatedDateTime?.ToString("o") ?? "",
        a.LastModifiedDateTime?.ToString("o") ?? "",
        a.IsAssigned ?? false,
        a.PublishingState?.ToString() ?? "unknown",
        a.IsFeatured ?? false);

    public static string FormatAppType(MobileApp app) => app switch
    {
        Win32LobApp => "Win32",
        WindowsMobileMSI => "MSI",
        WebApp => "Web Link",
        MicrosoftStoreForBusinessApp => "Store (Business)",
        WindowsAppX => "AppX",
        WindowsUniversalAppX => "UWP",
        IosVppApp => "iOS VPP",
        IosStoreApp => "iOS Store",
        IosLobApp => "iOS LOB",
        AndroidStoreApp => "Android Store",
        AndroidLobApp => "Android LOB",
        AndroidManagedStoreApp => "Android Managed",
        ManagedIOSStoreApp => "Managed iOS",
        ManagedAndroidStoreApp => "Managed Android",
        ManagedIOSLobApp => "Managed iOS LOB",
        ManagedAndroidLobApp => "Managed Android LOB",
        MacOSLobApp => "macOS LOB",
        MacOSDmgApp => "macOS DMG",
        MacOSMicrosoftDefenderApp => "macOS Defender",
        MacOSMicrosoftEdgeApp => "macOS Edge",
        MacOSOfficeSuiteApp => "macOS Office",
        _ => app.OdataType?.Split('.').LastOrDefault()?.Replace("App", " App") ?? "Unknown"
    };

    public static string DetectPlatform(MobileApp app) => app switch
    {
        Win32LobApp or WindowsMobileMSI or WindowsAppX or WindowsUniversalAppX
            or MicrosoftStoreForBusinessApp => "Windows",
        IosVppApp or IosStoreApp or IosLobApp or ManagedIOSStoreApp or ManagedIOSLobApp => "iOS",
        AndroidStoreApp or AndroidLobApp or AndroidManagedStoreApp
            or ManagedAndroidStoreApp or ManagedAndroidLobApp => "Android",
        MacOSLobApp or MacOSDmgApp or MacOSMicrosoftDefenderApp
            or MacOSMicrosoftEdgeApp or MacOSOfficeSuiteApp => "macOS",
        WebApp => "Web",
        _ => "Other"
    };
}
