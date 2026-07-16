namespace Intune.Commander.Core.Models;

/// <summary>
/// Represents a single Intune object (config, policy, app, etc.) that is assigned to a group.
/// </summary>
public class GroupAssignedObject
{
    public string ObjectId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string ObjectType { get; init; } = "";
    public string Category { get; init; } = "";
    public string Platform { get; init; } = "";
    public string AssignmentIntent { get; init; } = "";
    public bool IsExclusion { get; init; }
}
