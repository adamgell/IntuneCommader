using Intune.Commander.Core.Models;

namespace Intune.Commander.Core.Services;

/// <summary>
/// Implements the full feature set of the IntuneAssignmentChecker PowerShell script in .NET.
/// All methods scan every supported Intune policy type (device configurations, settings catalog,
/// admin templates, compliance policies, app protection policies, app config policies,
/// applications, platform scripts, health scripts, endpoint security intents).
/// </summary>
public interface IAssignmentCheckerService
{
    /// <summary>
    /// Returns every Intune policy that is effectively assigned to the given user
    /// (via the user's transitive group memberships, "All Users", or "All Licensed Users").
    /// Excluded groups are respected and remove a policy from the results.
    /// </summary>
    Task<List<AssignmentReportRow>> GetUserAssignmentsAsync(
        string userPrincipalName,
        Action<string>? progress = null,
        Action<AssignmentReportRow>? onRow = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every Intune policy directly assigned to (or excluded from) the given group.
    /// </summary>
    Task<List<AssignmentReportRow>> GetGroupAssignmentsAsync(
        string groupId,
        string groupName,
        Action<string>? progress = null,
        Action<AssignmentReportRow>? onRow = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every Intune policy that is effectively assigned to the given device
    /// (via the device's transitive group memberships or "All Devices").
    /// </summary>
    Task<List<AssignmentReportRow>> GetDeviceAssignmentsAsync(
        string deviceName,
        Action<string>? progress = null,
        Action<AssignmentReportRow>? onRow = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all policies with a summary of their assignment targets (All Users, All Devices, group names, exclusions).
    /// </summary>
    Task<List<AssignmentReportRow>> GetAllPoliciesWithAssignmentsAsync(
        Action<string>? progress = null,
        Action<AssignmentReportRow>? onRow = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all policies that have at least one "All Users" / "All Licensed Users" assignment.
    /// </summary>
    Task<List<AssignmentReportRow>> GetAllUsersAssignmentsAsync(
        Action<string>? progress = null,
        Action<AssignmentReportRow>? onRow = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all policies that have at least one "All Devices" assignment.
    /// </summary>
    Task<List<AssignmentReportRow>> GetAllDevicesAssignmentsAsync(
        Action<string>? progress = null,
        Action<AssignmentReportRow>? onRow = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all policies that have zero assignment targets.
    /// </summary>
    Task<List<AssignmentReportRow>> GetUnassignedPoliciesAsync(
        Action<string>? progress = null,
        Action<AssignmentReportRow>? onRow = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every policy assignment that targets a group with zero members.
    /// </summary>
    Task<List<AssignmentReportRow>> GetEmptyGroupAssignmentsAsync(
        Action<string>? progress = null,
        Action<AssignmentReportRow>? onRow = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compares the assignment targets of two groups and returns one row per policy,
    /// showing whether each group is included, excluded, or not assigned.
    /// </summary>
    Task<List<AssignmentReportRow>> CompareGroupAssignmentsAsync(
        string groupId1,
        string groupName1,
        string groupId2,
        string groupName2,
        Action<string>? progress = null,
        Action<AssignmentReportRow>? onRow = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns device configuration and compliance policy deployment failures
    /// (status == "error", "conflict", "notApplicable", or "nonCompliant").
    /// </summary>
    Task<List<AssignmentReportRow>> GetFailedAssignmentsAsync(
        Action<string>? progress = null,
        Action<AssignmentReportRow>? onRow = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pre-fetches all policy lists from Graph and stores them in the cache.
    /// Subsequent report runs will use cached data instead of hitting Graph.
    /// </summary>
    /// <param name="forceRefresh">
    /// When <c>true</c>, bypasses any existing cached data and always downloads
    /// fresh data from Graph. Use this when the user explicitly clicks the
    /// "Download All to Cache" button.
    /// </param>
    Task PrefetchAllToCacheAsync(
        Action<string>? progress = null,
        CancellationToken cancellationToken = default,
        bool forceRefresh = false);
}
