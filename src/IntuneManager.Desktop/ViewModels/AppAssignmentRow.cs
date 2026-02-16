namespace IntuneManager.Desktop.ViewModels;

/// <summary>
/// Flattened display model: one row per app × assignment combination.
/// All fields are pre-computed strings for direct DataGrid binding.
/// </summary>
public class AppAssignmentRow
{
    // --- App fields ---
    public string AppName { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string Description { get; init; } = "";
    public string AppType { get; init; } = "";
    public string Version { get; init; } = "";
    public string Platform { get; init; } = "";
    public string BundleId { get; init; } = "";
    public string PackageId { get; init; } = "";
    public string IsFeatured { get; init; } = "";
    public string CreatedDate { get; init; } = "";
    public string LastModified { get; init; } = "";

    // --- Assignment fields ---
    public string AssignmentType { get; init; } = "";   // "All Users", "All Devices", "Group"
    public string TargetName { get; init; } = "";        // resolved group name or built-in target
    public string TargetGroupId { get; init; } = "";
    public string InstallIntent { get; init; } = "";     // required, available, uninstall
    public string AssignmentSettings { get; init; } = "";
    public string IsExclusion { get; init; } = "";

    // --- URLs ---
    public string AppStoreUrl { get; init; } = "";
    public string PrivacyUrl { get; init; } = "";
    public string InformationUrl { get; init; } = "";

    // --- Win32 / platform-specific ---
    public string MinimumOsVersion { get; init; } = "";
    public string MinimumFreeDiskSpaceMB { get; init; } = "";
    public string MinimumMemoryMB { get; init; } = "";
    public string MinimumProcessors { get; init; } = "";

    // --- Metadata ---
    public string Categories { get; init; } = "";
    public string Notes { get; init; } = "";

    /// <summary>Underlying app ID (not shown in grid, useful for linking).</summary>
    public string AppId { get; init; } = "";
}
