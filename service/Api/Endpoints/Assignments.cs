using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Intune.Commander.Core.Services;

namespace CmProjectX.Api;

// Normalized assignment row shared by every assignable surface. The Graph
// assignment *wrapper* type differs per resource (DeviceCompliancePolicyAssignment,
// MobileAppAssignment, …) but they all carry a DeviceAndAppManagementAssignmentTarget,
// so the target translation lives here once. `Intent` is apps-only (null elsewhere).
public sealed record AssignmentDto(
    string Kind,          // group | exclusionGroup | allDevices | allUsers
    string? GroupId,
    string? GroupName,
    string? FilterId,
    string? FilterMode,   // include | exclude | null
    string? Intent);      // apps: required | available | uninstall | availableWithoutEnrollment

internal static class Assignments
{
    // Normalized row -> a typed Graph assignment target (with filter).
    public static DeviceAndAppManagementAssignmentTarget BuildTarget(AssignmentDto a)
    {
        DeviceAndAppManagementAssignmentTarget t = a.Kind switch
        {
            "exclusionGroup" => new ExclusionGroupAssignmentTarget { GroupId = a.GroupId },
            "allDevices" => new AllDevicesAssignmentTarget(),
            "allUsers" => new AllLicensedUsersAssignmentTarget(),
            _ => new GroupAssignmentTarget { GroupId = a.GroupId },
        };
        if (!string.IsNullOrEmpty(a.FilterId))
        {
            t.DeviceAndAppManagementAssignmentFilterId = a.FilterId;
            t.DeviceAndAppManagementAssignmentFilterType = a.FilterMode switch
            {
                "exclude" => DeviceAndAppManagementAssignmentFilterType.Exclude,
                "include" => DeviceAndAppManagementAssignmentFilterType.Include,
                _ => DeviceAndAppManagementAssignmentFilterType.None,
            };
        }
        return t;
    }

    // A typed Graph target -> the normalized row. `intent` is passed through for apps.
    public static AssignmentDto ReadTarget(DeviceAndAppManagementAssignmentTarget? t, string? intent = null)
    {
        if (t is null) return new AssignmentDto("group", null, null, null, null, intent);
        var (kind, gid) = t switch
        {
            ExclusionGroupAssignmentTarget e => ("exclusionGroup", e.GroupId),
            AllDevicesAssignmentTarget => ("allDevices", (string?)null),
            AllLicensedUsersAssignmentTarget => ("allUsers", (string?)null),
            GroupAssignmentTarget g => ("group", g.GroupId),
            _ => ("group", (string?)null),
        };
        var fmode = t.DeviceAndAppManagementAssignmentFilterType switch
        {
            DeviceAndAppManagementAssignmentFilterType.Include => "include",
            DeviceAndAppManagementAssignmentFilterType.Exclude => "exclude",
            _ => (string?)null,
        };
        return new AssignmentDto(kind, gid, null, t.DeviceAndAppManagementAssignmentFilterId, fmode, intent);
    }

    // Batch-resolve GroupId -> GroupName across a set of normalized rows in ONE
    // directory lookup, filling GroupName where the directory knows the object.
    // Group/exclusionGroup rows carry a GroupId; all-users/all-devices rows don't.
    // Best-effort: on any resolver failure the rows pass through unchanged (names
    // stay null), so the detail panel still renders with raw IDs rather than erroring.
    public static async Task<List<AssignmentDto>> ResolveNamesAsync(
        List<AssignmentDto> rows, GraphServiceClient g, CancellationToken ct)
    {
        var ids = rows.Where(r => !string.IsNullOrEmpty(r.GroupId))
                      .Select(r => r.GroupId!)
                      .Distinct()
                      .ToList();
        if (ids.Count == 0) return rows;
        IReadOnlyDictionary<string, string> names;
        try { names = await new DirectoryObjectResolver(g).ResolveAsync(ids, ct); }
        catch { return rows; }
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].GroupId is { } gid && names.TryGetValue(gid, out var n))
                rows[i] = rows[i] with { GroupName = n };
        }
        return rows;
    }

    // App install intent <-> string.
    public static InstallIntent ParseIntent(string? s) => s switch
    {
        "required" => InstallIntent.Required,
        "uninstall" => InstallIntent.Uninstall,
        "availableWithoutEnrollment" => InstallIntent.AvailableWithoutEnrollment,
        _ => InstallIntent.Available,
    };
}
