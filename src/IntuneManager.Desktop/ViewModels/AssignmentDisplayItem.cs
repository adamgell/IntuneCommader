namespace IntuneManager.Desktop.ViewModels;

/// <summary>
/// Lightweight display model for showing assignments in the detail pane.
/// Avoids binding directly to Graph SDK types.
/// </summary>
public class AssignmentDisplayItem
{
    /// <summary>"All Devices", "All Users", group display name, etc.</summary>
    public required string Target { get; init; }

    /// <summary>The raw group GUID, empty for All Devices / All Users.</summary>
    public string GroupId { get; init; } = "";

    /// <summary>"Include" or "Exclude"</summary>
    public required string TargetKind { get; init; }

    /// <summary>For apps only – "Required", "Available", "Uninstall", etc. Empty for configs/policies.</summary>
    public string Intent { get; init; } = "";
}
