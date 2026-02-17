namespace IntuneManager.Desktop.ViewModels;

/// <summary>
/// Flattened display model for group views (Dynamic and Assigned).
/// All fields are pre-computed strings for direct DataGrid binding.
/// </summary>
public class GroupRow
{
    public string GroupName { get; init; } = "";
    public string Description { get; init; } = "";
    public string MembershipRule { get; init; } = "";
    public string ProcessingState { get; init; } = "";
    public string GroupType { get; init; } = "";
    public string TotalMembers { get; init; } = "0";
    public string Users { get; init; } = "0";
    public string Devices { get; init; } = "0";
    public string NestedGroups { get; init; } = "0";
    public string SecurityEnabled { get; init; } = "";
    public string MailEnabled { get; init; } = "";
    public string CreatedDate { get; init; } = "";
    public string GroupId { get; init; } = "";
}
