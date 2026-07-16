using Intune.Commander.Core.Models;
using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

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

    /// <summary>
    /// Returns detailed member list for the specified group (users, devices, nested groups).
    /// </summary>
    Task<List<GroupMemberInfo>> ListGroupMembersAsync(string groupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for groups by display name (startsWith) or exact GUID.
    /// </summary>
    Task<List<Group>> SearchGroupsAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds all Intune objects (device configs, compliance policies, apps, etc.)
    /// assigned to the specified group.
    /// </summary>
    Task<List<GroupAssignedObject>> GetGroupAssignmentsAsync(
        string groupId,
        IConfigurationProfileService configService,
        ICompliancePolicyService complianceService,
        IApplicationService appService,
        Action<string>? progressCallback = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Categorised member counts for a single group.
/// </summary>
public record GroupMemberCounts(int Users, int Devices, int NestedGroups, int Total);

/// <summary>
/// Detailed info for a single group member.
/// </summary>
public record GroupMemberInfo(
    string MemberType,
    string DisplayName,
    string SecondaryInfo,
    string TertiaryInfo,
    string Status,
    string Id);
