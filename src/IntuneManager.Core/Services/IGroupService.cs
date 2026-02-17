using Microsoft.Graph.Models;

namespace IntuneManager.Core.Services;

public interface IGroupService
{
    /// <summary>
    /// Lists all groups whose <c>groupTypes</c> contains "DynamicMembership".
    /// </summary>
    Task<List<Group>> ListDynamicGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all groups that are NOT dynamic (assigned / static membership).
    /// </summary>
    Task<List<Group>> ListAssignedGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns typed member counts for the specified group.
    /// </summary>
    Task<GroupMemberCounts> GetMemberCountsAsync(string groupId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Categorised member counts for a single group.
/// </summary>
public record GroupMemberCounts(int Users, int Devices, int NestedGroups, int Total);
