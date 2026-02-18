namespace IntuneManager.Desktop.ViewModels;

/// <summary>
/// Flattened display model for a group member (user, device, or nested group).
/// Pre-computed strings for direct binding in the detail pane.
/// </summary>
public class GroupMemberItem
{
    /// <summary>"User", "Device", or "Group"</summary>
    public string MemberType { get; init; } = "";

    public string DisplayName { get; init; } = "";

    /// <summary>UPN for users, OS info for devices, group ID for nested groups.</summary>
    public string SecondaryInfo { get; init; } = "";

    /// <summary>Additional detail line (e.g. mail for users, model for devices).</summary>
    public string TertiaryInfo { get; init; } = "";

    /// <summary>Enabled / Managed / Compliant status text.</summary>
    public string Status { get; init; } = "";

    /// <summary>The member's Graph object ID.</summary>
    public string Id { get; init; } = "";
}
