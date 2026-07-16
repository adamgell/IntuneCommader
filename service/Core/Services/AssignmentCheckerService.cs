using System.Collections.Concurrent;
using Intune.Commander.Core.Models;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

/// <summary>
/// Full .NET port of the IntuneAssignmentChecker PowerShell script.
/// Scans all supported Intune policy types and resolves assignments for
/// users, groups, and devices.
/// </summary>
public class AssignmentCheckerService : IAssignmentCheckerService
{
    private readonly GraphServiceClient _graphClient;
    private readonly ICacheService? _cacheService;
    private readonly string? _tenantId;
    private readonly ConcurrentDictionary<string, string> _groupNameCache = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxConcurrency = 10;
    // Set to true during PrefetchAllToCacheAsync(forceRefresh:true) so every Fetch*Async
    // bypasses TryGetFromCache and always downloads fresh data from Graph.
    private bool _forceRefresh;
    // LiteDB is not thread-safe for concurrent writes; this lock serialises
    // TrySetCache calls that may fire in parallel during PrefetchAllToCacheAsync.
    private readonly object _cacheLock = new();

    public AssignmentCheckerService(GraphServiceClient graphClient,
        ICacheService? cacheService = null, string? tenantId = null)
    {
        _graphClient = graphClient;
        _cacheService = cacheService;
        _tenantId = tenantId;
    }

    private List<T>? TryGetFromCache<T>(string cacheKey) =>
        (!_forceRefresh && _cacheService != null && _tenantId != null)
            ? _cacheService.Get<T>(_tenantId, cacheKey)
            : null;

    private void TrySetCache<T>(string cacheKey, List<T> items)
    {
        if (_cacheService != null && _tenantId != null)
            lock (_cacheLock)
                _cacheService.Set(_tenantId, cacheKey, items);
    }

    // ──────────────────────────────────────────────────────────────────────────────
    //  Public API
    // ──────────────────────────────────────────────────────────────────────────────

    public async Task<List<AssignmentReportRow>> GetUserAssignmentsAsync(
        string userPrincipalName,
        Action<string>? progress = null,
        Action<AssignmentReportRow>? onRow = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Invoke($"Looking up user {userPrincipalName}...");
        var user = await _graphClient.Users[userPrincipalName]
            .GetAsync(req => req.QueryParameters.Select = ["id", "displayName", "userPrincipalName"],
                cancellationToken);

        if (user?.Id == null)
            throw new InvalidOperationException($"User not found: {userPrincipalName}");

        progress?.Invoke("Fetching group memberships...");
        var groupIds = await GetUserTransitiveMemberGroupIdsAsync(user.Id, cancellationToken);

        return await GetEntityAssignmentsAsync(groupIds, isUser: true, progress, onRow, cancellationToken);
    }

    public async Task<List<AssignmentReportRow>> GetGroupAssignmentsAsync(
        string groupId,
        string groupName,
        Action<string>? progress = null,
        Action<AssignmentReportRow>? onRow = null,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<AssignmentReportRow>();
        await ScanAllPoliciesAsync(
            rows, ScanMode.DirectGroup, groupId, groupName, null,
            progress, cancellationToken, onRow);
        return rows;
    }

    public async Task<List<AssignmentReportRow>> GetDeviceAssignmentsAsync(
        string deviceName,
        Action<string>? progress = null,
        Action<AssignmentReportRow>? onRow = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Invoke($"Searching for devices matching '{deviceName}'...");
        var escaped = deviceName.Replace("'", "''");

        // Use startsWith for partial-name matching
        var devices = new List<Device>();
        var devicesPage = await _graphClient.Devices
            .GetAsync(req =>
            {
                req.QueryParameters.Filter = $"startsWith(displayName,'{escaped}')";
                req.QueryParameters.Select = ["id", "displayName", "operatingSystem"];
                req.QueryParameters.Top = 50;
                req.Headers.Add("ConsistencyLevel", "eventual");
                req.QueryParameters.Count = true;
            }, cancellationToken);
        while (devicesPage != null)
        {
            if (devicesPage.Value != null) devices.AddRange(devicesPage.Value);
            if (!string.IsNullOrEmpty(devicesPage.OdataNextLink))
                devicesPage = await _graphClient.Devices
                    .WithUrl(devicesPage.OdataNextLink).GetAsync(cancellationToken: cancellationToken);
            else break;
        }

        if (devices.Count == 0)
            throw new InvalidOperationException($"No devices found in Azure AD matching '{deviceName}'. Try a shorter prefix.");

        progress?.Invoke($"Found {devices.Count} device(s). Fetching assignments in parallel...");

        var allRows = new List<AssignmentReportRow>();
        using var sem = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
        var tasks = devices.Select(async device =>
        {
            await sem.WaitAsync(cancellationToken);
            try
            {
                if (device.Id == null) return;
                var dName = device.DisplayName ?? device.Id;
                var groupIds = await GetDeviceTransitiveMemberGroupIdsAsync(device.Id, cancellationToken);
                var rows = await GetEntityAssignmentsAsync(groupIds, isUser: false, null, null, cancellationToken);
                foreach (var r in rows)
                {
                    var stamped = r with { TargetDevice = dName };
                    lock (allRows) allRows.Add(stamped);
                    onRow?.Invoke(stamped);
                }
            }
            finally { sem.Release(); }
        });
        await Task.WhenAll(tasks);
        cancellationToken.ThrowIfCancellationRequested();

        return allRows;
    }

    public async Task<List<AssignmentReportRow>> GetAllPoliciesWithAssignmentsAsync(
        Action<string>? progress = null,
        Action<AssignmentReportRow>? onRow = null,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<AssignmentReportRow>();
        await ScanAllPoliciesAsync(rows, ScanMode.AllPolicies, null, null, null, progress, cancellationToken, onRow);
        return rows;
    }

    public async Task<List<AssignmentReportRow>> GetAllUsersAssignmentsAsync(
        Action<string>? progress = null,
        Action<AssignmentReportRow>? onRow = null,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<AssignmentReportRow>();
        await ScanAllPoliciesAsync(rows, ScanMode.AllUsers, null, null, null, progress, cancellationToken, onRow);
        return rows;
    }

    public async Task<List<AssignmentReportRow>> GetAllDevicesAssignmentsAsync(
        Action<string>? progress = null,
        Action<AssignmentReportRow>? onRow = null,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<AssignmentReportRow>();
        await ScanAllPoliciesAsync(rows, ScanMode.AllDevices, null, null, null, progress, cancellationToken, onRow);
        return rows;
    }

    public async Task<List<AssignmentReportRow>> GetUnassignedPoliciesAsync(
        Action<string>? progress = null,
        Action<AssignmentReportRow>? onRow = null,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<AssignmentReportRow>();
        await ScanAllPoliciesAsync(rows, ScanMode.Unassigned, null, null, null, progress, cancellationToken, onRow);
        return rows;
    }

    public async Task<List<AssignmentReportRow>> GetEmptyGroupAssignmentsAsync(
        Action<string>? progress = null,
        Action<AssignmentReportRow>? onRow = null,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<AssignmentReportRow>();

        // First pass: collect all group IDs referenced in assignments
        progress?.Invoke("Collecting group IDs from assignments...");
        var seenGroupIds = await CollectAllAssignedGroupIdsAsync(cancellationToken);

        if (seenGroupIds.Count == 0) return rows;

        // Second pass: check which groups are empty
        progress?.Invoke($"Checking {seenGroupIds.Count} groups for empty membership...");
        var emptyGroupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groupNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var sem = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
        var tasks = seenGroupIds.Select(async gid =>
        {
            try
            {
                await sem.WaitAsync(cancellationToken);
                try
                {
                    var group = await _graphClient.Groups[gid]
                        .GetAsync(req => req.QueryParameters.Select = ["id", "displayName"], cancellationToken);
                    if (group == null) return;
                    lock (groupNames) groupNames[gid] = group.DisplayName ?? gid;

                    var members = await _graphClient.Groups[gid].Members
                        .GetAsync(req => req.QueryParameters.Select = ["id"], cancellationToken);
                    if ((members?.Value?.Count ?? 0) == 0)
                        lock (emptyGroupIds) emptyGroupIds.Add(gid);
                }
                catch (OperationCanceledException) { throw; }
                catch { /* skip inaccessible groups */ }
                finally { sem.Release(); }
            }
            catch (OperationCanceledException) { /* consolidated into single throw below */ }
        });
        await Task.WhenAll(tasks);
        cancellationToken.ThrowIfCancellationRequested();

        if (emptyGroupIds.Count == 0) return rows;

        // Third pass: find policies assigned to empty groups
        progress?.Invoke($"Finding policies assigned to {emptyGroupIds.Count} empty group(s)...");
        await ScanAllPoliciesAsync(rows, ScanMode.EmptyGroups, null, null,
            new EmptyGroupContext(emptyGroupIds, groupNames), progress, cancellationToken, onRow);

        return rows;
    }

    public async Task<List<AssignmentReportRow>> CompareGroupAssignmentsAsync(
        string groupId1, string groupName1,
        string groupId2, string groupName2,
        Action<string>? progress = null,
        Action<AssignmentReportRow>? onRow = null,
        CancellationToken cancellationToken = default)
    {
        // Scan both groups in parallel — major speed-up vs sequential
        progress?.Invoke($"Scanning assignments for {groupName1} and {groupName2} in parallel...");
        var t1 = GetGroupAssignmentsAsync(groupId1, groupName1, cancellationToken: cancellationToken);
        var t2 = GetGroupAssignmentsAsync(groupId2, groupName2, cancellationToken: cancellationToken);
        await Task.WhenAll(t1, t2);

        var g1 = t1.Result;
        var g2 = t2.Result;

        var g1Map = g1.ToDictionary(r => r.PolicyId + "|" + r.PolicyType);
        var g2Map = g2.ToDictionary(r => r.PolicyId + "|" + r.PolicyType);

        var allKeys = g1Map.Keys.Union(g2Map.Keys).ToList();
        var result = new List<AssignmentReportRow>(allKeys.Count);

        foreach (var key in allKeys)
        {
            g1Map.TryGetValue(key, out var r1);
            g2Map.TryGetValue(key, out var r2);
            var template = r1 ?? r2!;
            var row = new AssignmentReportRow
            {
                PolicyId = template.PolicyId,
                PolicyName = template.PolicyName,
                PolicyType = template.PolicyType,
                Platform = template.Platform,
                Group1Status = r1 != null ? r1.AssignmentReason : "",
                Group2Status = r2 != null ? r2.AssignmentReason : ""
            };
            result.Add(row);
        }

        result.Sort(PolicyTypeNameComparer);
        // Stream rows to the UI immediately (they're already sorted)
        foreach (var row in result) onRow?.Invoke(row);
        return result;
    }

    public async Task<List<AssignmentReportRow>> GetFailedAssignmentsAsync(
        Action<string>? progress = null,
        Action<AssignmentReportRow>? onRow = null,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<AssignmentReportRow>();
        var failedStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "error", "conflict", "notApplicable" };

        // Device Configuration Failures
        progress?.Invoke("Checking device configuration failures...");
        try
        {
            var configs = await FetchDeviceConfigurationsAsync(cancellationToken);
            using var sem = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
            var tasks = configs.Select(async config =>
            {
                await sem.WaitAsync(cancellationToken);
                try
                {
                    if (config.Id == null) return;
                    var statusResp = await _graphClient.DeviceManagement
                        .DeviceConfigurations[config.Id].DeviceStatuses
                        .GetAsync(req =>
                        {
                            req.QueryParameters.Select = ["id", "deviceDisplayName", "status",
                                "userPrincipalName", "lastReportedDateTime"];
                            req.QueryParameters.Top = 999;
                        }, cancellationToken);

                    while (statusResp != null)
                    {
                        foreach (var s in statusResp.Value ?? [])
                        {
                            var statusStr = s.Status?.ToString().ToLowerInvariant() ?? "";
                            if (!failedStatuses.Contains(statusStr)) continue;
                            var row = new AssignmentReportRow
                            {
                                PolicyId = config.Id,
                                PolicyName = config.DisplayName ?? config.Id,
                                PolicyType = "Device Configuration",
                                TargetDevice = s.DeviceDisplayName ?? "",
                                Status = s.Status?.ToString() ?? "",
                                UserPrincipalName = s.UserPrincipalName ?? "",
                                LastReported = s.LastReportedDateTime?.ToString("g") ?? ""
                            };
                            lock (rows) rows.Add(row);
                            onRow?.Invoke(row);
                        }

                        if (string.IsNullOrEmpty(statusResp.OdataNextLink)) break;
                        statusResp = await _graphClient.DeviceManagement
                            .DeviceConfigurations[config.Id].DeviceStatuses
                            .WithUrl(statusResp.OdataNextLink)
                            .GetAsync(cancellationToken: cancellationToken);
                    }
                }
                finally { sem.Release(); }
            });
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            rows.Add(new AssignmentReportRow { PolicyType = "Device Configuration", PolicyName = $"Error: {ex.Message}" });
        }

        // Compliance Policy Failures
        progress?.Invoke("Checking compliance policy failures...");
        var complianceFailed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "error", "conflict", "notApplicable", "nonCompliant" };
        try
        {
            var policies = await FetchCompliancePoliciesAsync(cancellationToken);
            using var sem2 = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
            var tasks2 = policies.Select(async policy =>
            {
                await sem2.WaitAsync(cancellationToken);
                try
                {
                    if (policy.Id == null) return;
                    var statusResp = await _graphClient.DeviceManagement
                        .DeviceCompliancePolicies[policy.Id].DeviceStatuses
                        .GetAsync(req =>
                        {
                            req.QueryParameters.Select = ["id", "deviceDisplayName", "status",
                                "userPrincipalName", "lastReportedDateTime"];
                            req.QueryParameters.Top = 999;
                        }, cancellationToken);

                    while (statusResp != null)
                    {
                        foreach (var s in statusResp.Value ?? [])
                        {
                            var statusStr = s.Status?.ToString().ToLowerInvariant() ?? "";
                            if (!complianceFailed.Contains(statusStr)) continue;
                            var row = new AssignmentReportRow
                            {
                                PolicyId = policy.Id,
                                PolicyName = policy.DisplayName ?? policy.Id,
                                PolicyType = "Compliance Policy",
                                TargetDevice = s.DeviceDisplayName ?? "",
                                Status = s.Status?.ToString() ?? "",
                                UserPrincipalName = s.UserPrincipalName ?? "",
                                LastReported = s.LastReportedDateTime?.ToString("g") ?? ""
                            };
                            lock (rows) rows.Add(row);
                            onRow?.Invoke(row);
                        }

                        if (string.IsNullOrEmpty(statusResp.OdataNextLink)) break;
                        statusResp = await _graphClient.DeviceManagement
                            .DeviceCompliancePolicies[policy.Id].DeviceStatuses
                            .WithUrl(statusResp.OdataNextLink)
                            .GetAsync(cancellationToken: cancellationToken);
                    }
                }
                finally { sem2.Release(); }
            });
            await Task.WhenAll(tasks2);
        }
        catch (Exception ex)
        {
            rows.Add(new AssignmentReportRow { PolicyType = "Compliance Policy", PolicyName = $"Error: {ex.Message}" });
        }

        rows.Sort(PolicyTypeNameComparer);
        return rows;
    }

    // ──────────────────────────────────────────────────────────────────────────────
    //  Internal scan modes
    // ──────────────────────────────────────────────────────────────────────────────

    private enum ScanMode
    {
        EntityMatch,   // user or device — group IDs + allUsers/allDevices
        DirectGroup,   // single specific group ID
        AllPolicies,   // all policies with any assignments
        AllUsers,      // policies assigned to All Users
        AllDevices,    // policies assigned to All Devices
        Unassigned,    // policies with no assignments
        EmptyGroups    // policies assigned to groups with 0 members
    }

    private sealed record EmptyGroupContext(
        HashSet<string> EmptyGroupIds,
        Dictionary<string, string> GroupNames);

    private async Task<List<AssignmentReportRow>> GetEntityAssignmentsAsync(
        HashSet<string> groupIds, bool isUser,
        Action<string>? progress, Action<AssignmentReportRow>? onRow, CancellationToken ct)
    {
        var rows = new List<AssignmentReportRow>();
        await ScanAllPoliciesAsync(rows, ScanMode.EntityMatch, null, null,
            new EntityContext(groupIds, isUser), progress, ct, onRow);
        return rows;
    }

    private sealed record EntityContext(HashSet<string> GroupIds, bool IsUser);

    /// <summary>
    /// Master scan that iterates all policy types and applies the given mode filter.
    /// </summary>
    private async Task ScanAllPoliciesAsync(
        List<AssignmentReportRow> rows,
        ScanMode mode,
        string? directGroupId,
        string? directGroupName,
        object? context,
        Action<string>? progress,
        CancellationToken ct,
        Action<AssignmentReportRow>? onRow = null)
    {
        var entityCtx = context as EntityContext;
        var emptyCtx = context as EmptyGroupContext;

        // Fetch all policy lists in parallel first (all lightweight list calls)
        progress?.Invoke("Fetching policy lists in parallel...");
        var fetchConfigsTask    = FetchDeviceConfigurationsAsync(ct);
        var fetchScTask         = FetchSettingsCatalogAsync(ct);     // shared by ES + SC
        var fetchAdminTask      = FetchAdminTemplatesAsync(ct);
        var fetchComplianceTask = FetchCompliancePoliciesAsync(ct);
        var fetchAppProtTask    = FetchAppProtectionPoliciesAsync(ct);
        var fetchAppConfigTask  = FetchMobileAppConfigurationsAsync(ct);
        var fetchAppsTask       = FetchApplicationsAsync(ct);
        var fetchScriptsTask    = FetchPlatformScriptsAsync(ct);
        var fetchHealthTask     = FetchHealthScriptsAsync(ct);
        var fetchIntentsTask    = FetchEndpointSecurityIntentsAsync(ct);
        var fetchEnrollTask     = FetchEnrollmentConfigurationsAsync(ct);

        await Task.WhenAll(
            fetchConfigsTask, fetchScTask, fetchAdminTask, fetchComplianceTask,
            fetchAppProtTask, fetchAppConfigTask, fetchAppsTask,
            fetchScriptsTask, fetchHealthTask, fetchIntentsTask, fetchEnrollTask);
        ct.ThrowIfCancellationRequested();

        var endpointFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "endpointSecurityAntivirus", "endpointSecurityDiskEncryption",
            "endpointSecurityFirewall", "endpointSecurityEndpointDetectionAndResponse",
            "endpointSecurityAttackSurfaceReduction", "endpointSecurityAccountProtection"
        };
        var allConfigPolicies = fetchScTask.Result;
        var esPolicies = allConfigPolicies
            .Where(p => p.TemplateReference?.TemplateFamily != null &&
                        endpointFamilies.Contains(p.TemplateReference.TemplateFamily.ToString()!))
            .ToList();

        // Now scan all policy types in parallel — each type uses its own internal concurrency
        progress?.Invoke("Scanning all policy types in parallel...");
        var scanTasks = new List<Task>
        {
            ScanPolicyTypeAsync(rows, mode, directGroupId, entityCtx, emptyCtx, ct,
                "Device Configuration",
                () => Task.FromResult(fetchConfigsTask.Result),
                p => p.Id!, p => p.DisplayName ?? p.Id!,
                p => PlatformFromOData(p.OdataType),
                async id => await FetchAllAssignmentPagesAsync(
                    () => _graphClient.DeviceManagement.DeviceConfigurations[id]
                        .Assignments.GetAsync(cancellationToken: ct),
                    r => r.OdataNextLink, r => r.Value,
                    url => _graphClient.DeviceManagement.DeviceConfigurations[id]
                        .Assignments.WithUrl(url).GetAsync(cancellationToken: ct)),
                onRow),

            ScanPolicyTypeAsync(rows, mode, directGroupId, entityCtx, emptyCtx, ct,
                "Settings Catalog",
                () => Task.FromResult(allConfigPolicies),
                p => p.Id!, p => p.Name ?? p.Id!,
                p => p.Platforms?.ToString() ?? "",
                async id => await FetchAllAssignmentPagesAsync(
                    () => _graphClient.DeviceManagement.ConfigurationPolicies[id]
                        .Assignments.GetAsync(cancellationToken: ct),
                    r => r.OdataNextLink, r => r.Value,
                    url => _graphClient.DeviceManagement.ConfigurationPolicies[id]
                        .Assignments.WithUrl(url).GetAsync(cancellationToken: ct)),
                onRow),

            ScanPolicyTypeAsync(rows, mode, directGroupId, entityCtx, emptyCtx, ct,
                "Administrative Template",
                () => Task.FromResult(fetchAdminTask.Result),
                p => p.Id!, p => p.DisplayName ?? p.Id!,
                _ => "Windows",
                async id => await FetchAllAssignmentPagesAsync(
                    () => _graphClient.DeviceManagement.GroupPolicyConfigurations[id]
                        .Assignments.GetAsync(cancellationToken: ct),
                    r => r.OdataNextLink, r => r.Value,
                    url => _graphClient.DeviceManagement.GroupPolicyConfigurations[id]
                        .Assignments.WithUrl(url).GetAsync(cancellationToken: ct)),
                onRow),

            ScanPolicyTypeAsync(rows, mode, directGroupId, entityCtx, emptyCtx, ct,
                "Compliance Policy",
                () => Task.FromResult(fetchComplianceTask.Result),
                p => p.Id!, p => p.DisplayName ?? p.Id!,
                p => PlatformFromOData(p.OdataType),
                async id => await FetchAllAssignmentPagesAsync(
                    () => _graphClient.DeviceManagement.DeviceCompliancePolicies[id]
                        .Assignments.GetAsync(cancellationToken: ct),
                    r => r.OdataNextLink, r => r.Value,
                    url => _graphClient.DeviceManagement.DeviceCompliancePolicies[id]
                        .Assignments.WithUrl(url).GetAsync(cancellationToken: ct)),
                onRow),

            // App Protection Policies (type-specific endpoints, handled inline)
            Task.Run(async () =>
            {
                using var appProtSem = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
                var appProtTasks = fetchAppProtTask.Result.Select(async policy =>
                {
                    try
                    {
                        await appProtSem.WaitAsync(ct);
                        try
                        {
                            if (policy.Id == null) return;
                            var flat = await FetchAppProtectionAssignmentsAsync(policy, ct);
                            if (mode == ScanMode.AllPolicies)
                                await ResolveGroupNamesAsync(flat, ct);
                            var row = BuildRow("App Protection Policy", policy.Id,
                                policy.DisplayName ?? policy.Id,
                                PlatformFromOData(policy.OdataType), flat, mode, directGroupId,
                                entityCtx, emptyCtx,
                                mode == ScanMode.AllPolicies ? _groupNameCache : null);
                            if (row != null) { lock (rows) rows.Add(row); onRow?.Invoke(row); }
                        }
                        finally { appProtSem.Release(); }
                    }
                    catch (OperationCanceledException) { }
                });
                await Task.WhenAll(appProtTasks);
                ct.ThrowIfCancellationRequested();
            }, ct),

            ScanPolicyTypeAsync(rows, mode, directGroupId, entityCtx, emptyCtx, ct,
                "App Configuration Policy",
                () => Task.FromResult(fetchAppConfigTask.Result),
                p => p.Id!, p => p.DisplayName ?? p.Id!,
                p => PlatformFromOData(p.OdataType),
                async id => await FetchAllAssignmentPagesAsync(
                    () => _graphClient.DeviceAppManagement.MobileAppConfigurations[id]
                        .Assignments.GetAsync(cancellationToken: ct),
                    r => r.OdataNextLink, r => r.Value,
                    url => _graphClient.DeviceAppManagement.MobileAppConfigurations[id]
                        .Assignments.WithUrl(url).GetAsync(cancellationToken: ct)),
                onRow),

            ScanPolicyTypeAsync(rows, mode, directGroupId, entityCtx, emptyCtx, ct,
                "Application",
                () => Task.FromResult(fetchAppsTask.Result),
                p => p.Id!, p => p.DisplayName ?? p.Id!,
                p => PlatformFromOData(p.OdataType),
                async id => await FetchAllAssignmentPagesAsync(
                    () => _graphClient.DeviceAppManagement.MobileApps[id]
                        .Assignments.GetAsync(cancellationToken: ct),
                    r => r.OdataNextLink, r => r.Value,
                    url => _graphClient.DeviceAppManagement.MobileApps[id]
                        .Assignments.WithUrl(url).GetAsync(cancellationToken: ct)),
                onRow),

            ScanPolicyTypeAsync(rows, mode, directGroupId, entityCtx, emptyCtx, ct,
                "Platform Script",
                () => Task.FromResult(fetchScriptsTask.Result),
                p => p.Id!, p => p.DisplayName ?? p.Id!,
                _ => "Windows",
                async id => await FetchAllAssignmentPagesAsync(
                    () => _graphClient.DeviceManagement.DeviceManagementScripts[id]
                        .Assignments.GetAsync(cancellationToken: ct),
                    r => r.OdataNextLink, r => r.Value,
                    url => _graphClient.DeviceManagement.DeviceManagementScripts[id]
                        .Assignments.WithUrl(url).GetAsync(cancellationToken: ct)),
                onRow),

            ScanPolicyTypeAsync(rows, mode, directGroupId, entityCtx, emptyCtx, ct,
                "Health Script",
                () => Task.FromResult(fetchHealthTask.Result),
                p => p.Id!, p => p.DisplayName ?? p.Id!,
                _ => "Windows",
                async id => await FetchAllAssignmentPagesAsync(
                    () => _graphClient.DeviceManagement.DeviceHealthScripts[id]
                        .Assignments.GetAsync(cancellationToken: ct),
                    r => r.OdataNextLink, r => r.Value,
                    url => _graphClient.DeviceManagement.DeviceHealthScripts[id]
                        .Assignments.WithUrl(url).GetAsync(cancellationToken: ct)),
                onRow),

            ScanPolicyTypeAsync(rows, mode, directGroupId, entityCtx, emptyCtx, ct,
                "Endpoint Security",
                () => Task.FromResult(esPolicies),
                p => p.Id!, p => p.Name ?? p.Id!,
                p => p.Platforms?.ToString() ?? "",
                async id => await FetchAllAssignmentPagesAsync(
                    () => _graphClient.DeviceManagement.ConfigurationPolicies[id]
                        .Assignments.GetAsync(cancellationToken: ct),
                    r => r.OdataNextLink, r => r.Value,
                    url => _graphClient.DeviceManagement.ConfigurationPolicies[id]
                        .Assignments.WithUrl(url).GetAsync(cancellationToken: ct)),
                onRow),

            ScanPolicyTypeAsync(rows, mode, directGroupId, entityCtx, emptyCtx, ct,
                "Endpoint Security (Legacy)",
                () => Task.FromResult(fetchIntentsTask.Result),
                p => p.Id!, p => p.DisplayName ?? p.Id!,
                _ => "",
                async id => await FetchAllAssignmentPagesAsync(
                    () => _graphClient.DeviceManagement.Intents[id]
                        .Assignments.GetAsync(cancellationToken: ct),
                    r => r.OdataNextLink, r => r.Value,
                    url => _graphClient.DeviceManagement.Intents[id]
                        .Assignments.WithUrl(url).GetAsync(cancellationToken: ct)),
                onRow),

            ScanPolicyTypeAsync(rows, mode, directGroupId, entityCtx, emptyCtx, ct,
                "Enrollment Configuration",
                () => Task.FromResult(fetchEnrollTask.Result),
                p => p.Id!, p => p.DisplayName ?? p.Id!,
                _ => "",
                async id => await FetchAllAssignmentPagesAsync(
                    () => _graphClient.DeviceManagement.DeviceEnrollmentConfigurations[id]
                        .Assignments.GetAsync(cancellationToken: ct),
                    r => r.OdataNextLink, r => r.Value,
                    url => _graphClient.DeviceManagement.DeviceEnrollmentConfigurations[id]
                        .Assignments.WithUrl(url).GetAsync(cancellationToken: ct)))
        };

        await Task.WhenAll(scanTasks);
        ct.ThrowIfCancellationRequested();

        rows.Sort(PolicyTypeNameComparer);
    }

    // ──────────────────────────────────────────────────────────────────────────────
    //  Generic type scanner
    // ──────────────────────────────────────────────────────────────────────────────

    private async Task ScanPolicyTypeAsync<T>(
        List<AssignmentReportRow> rows,
        ScanMode mode,
        string? directGroupId,
        EntityContext? entityCtx,
        EmptyGroupContext? emptyCtx,
        CancellationToken ct,
        string policyType,
        Func<Task<List<T>>> fetchPolicies,
        Func<T, string> getId,
        Func<T, string> getName,
        Func<T, string> getPlatform,
        Func<string, Task<List<FlatAssignment>>> getAssignments,
        Action<AssignmentReportRow>? onRow = null)
    {
        var items = await fetchPolicies();
        using var sem = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
        var tasks = items.Select(async item =>
        {
            try
            {
                await sem.WaitAsync(ct);
                try
                {
                    var id = getId(item);
                    if (string.IsNullOrEmpty(id)) return;
                    List<FlatAssignment> flat;
                    try { flat = await getAssignments(id); }
                    catch (OperationCanceledException) { throw; }
                    catch { return; } // skip 403s / 404s
                    if (mode == ScanMode.AllPolicies)
                        await ResolveGroupNamesAsync(flat, ct);
                    var row = BuildRow(policyType, id, getName(item), getPlatform(item),
                        flat, mode, directGroupId, entityCtx, emptyCtx,
                        mode == ScanMode.AllPolicies ? _groupNameCache : null);
                    if (row != null) { lock (rows) rows.Add(row); onRow?.Invoke(row); }
                }
                finally { sem.Release(); }
            }
            catch (OperationCanceledException) { /* consolidated into single throw below */ }
        });
        await Task.WhenAll(tasks);
        ct.ThrowIfCancellationRequested();
    }

    // ──────────────────────────────────────────────────────────────────────────────
    //  Row builder — applies the scan mode filter
    // ──────────────────────────────────────────────────────────────────────────────

    private static AssignmentReportRow? BuildRow(
        string policyType, string id, string name, string platform,
        List<FlatAssignment> assignments,
        ScanMode mode, string? directGroupId,
        EntityContext? entityCtx, EmptyGroupContext? emptyCtx,
        IReadOnlyDictionary<string, string>? groupNames = null)
    {
        switch (mode)
        {
            case ScanMode.Unassigned:
                return assignments.Count == 0
                    ? new AssignmentReportRow { PolicyId = id, PolicyName = name, PolicyType = policyType, Platform = platform }
                    : null;

            case ScanMode.AllPolicies:
                return assignments.Count > 0
                    ? new AssignmentReportRow
                    {
                        PolicyId = id, PolicyName = name, PolicyType = policyType, Platform = platform,
                        AssignmentSummary = SummariseAssignments(assignments, groupNames)
                    }
                    : null;

            case ScanMode.AllUsers:
                return assignments.Any(a => a.IsAllUsers)
                    ? new AssignmentReportRow { PolicyId = id, PolicyName = name, PolicyType = policyType, Platform = platform,
                        AssignmentSummary = "All Users" }
                    : null;

            case ScanMode.AllDevices:
                return assignments.Any(a => a.IsAllDevices)
                    ? new AssignmentReportRow { PolicyId = id, PolicyName = name, PolicyType = policyType, Platform = platform,
                        AssignmentSummary = "All Devices" }
                    : null;

            case ScanMode.DirectGroup when directGroupId != null:
            {
                var matched = assignments
                    .Where(a => string.Equals(a.GroupId, directGroupId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (matched.Count == 0) return null;
                var reason = matched.Any(a => a.IsExclusion) ? "Excluded" : "Direct Assignment";
                return new AssignmentReportRow
                {
                    PolicyId = id, PolicyName = name, PolicyType = policyType, Platform = platform,
                    AssignmentReason = reason,
                    AssignmentSummary = reason
                };
            }

            case ScanMode.EntityMatch when entityCtx != null:
            {
                // Exclusion wins — if any exclusion targets one of the entity's groups, skip
                if (assignments.Any(a => a.IsExclusion && a.GroupId != null && entityCtx.GroupIds.Contains(a.GroupId)))
                    return null;

                string? reason = null;
                foreach (var a in assignments.Where(x => !x.IsExclusion))
                {
                    if (entityCtx.IsUser && a.IsAllUsers) { reason = "All Users"; break; }
                    if (!entityCtx.IsUser && a.IsAllDevices) { reason = "All Devices"; break; }
                    if (a.GroupId != null && entityCtx.GroupIds.Contains(a.GroupId)) { reason = "Group Assignment"; break; }
                }
                return reason == null ? null
                    : new AssignmentReportRow
                    {
                        PolicyId = id, PolicyName = name, PolicyType = policyType, Platform = platform,
                        AssignmentReason = reason, AssignmentSummary = reason
                    };
            }

            case ScanMode.EmptyGroups when emptyCtx != null:
            {
                var emptyMatches = assignments
                    .Where(a => a.GroupId != null && emptyCtx.EmptyGroupIds.Contains(a.GroupId))
                    .ToList();
                if (emptyMatches.Count == 0) return null;
                var first = emptyMatches[0];
                emptyCtx.GroupNames.TryGetValue(first.GroupId!, out var gName);
                return new AssignmentReportRow
                {
                    PolicyId = id, PolicyName = name, PolicyType = policyType, Platform = platform,
                    GroupId = first.GroupId!,
                    GroupName = gName ?? first.GroupId!,
                    AssignmentReason = first.IsExclusion ? "Excluded" : "Include"
                };
            }

            default:
                return null;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────────
    //  Assignment flattening
    // ──────────────────────────────────────────────────────────────────────────────

    private sealed record FlatAssignment(
        string? GroupId,
        bool IsExclusion,
        bool IsAllUsers,
        bool IsAllDevices);

    private static List<FlatAssignment> Flatten<T>(IList<T>? assignments) where T : class
    {
        if (assignments == null) return [];
        var result = new List<FlatAssignment>(assignments.Count);
        foreach (var a in assignments)
        {
            var target = a.GetType().GetProperty("Target")?.GetValue(a)
                as DeviceAndAppManagementAssignmentTarget;
            AppendTarget(result, target);
        }
        return result;
    }

    /// <summary>
    /// Fetches all pages of an assignment collection, flattening into <see cref="FlatAssignment"/> list.
    /// Use this instead of calling <c>.Assignments.GetAsync()</c> directly to avoid truncated results.
    /// </summary>
    private static async Task<List<FlatAssignment>> FetchAllAssignmentPagesAsync<TResponse, TItem>(
        Func<Task<TResponse?>> fetchFirst,
        Func<TResponse, string?> getNextLink,
        Func<TResponse, IList<TItem>?> getItems,
        Func<string, Task<TResponse?>> fetchByUrl)
        where TResponse : class
        where TItem : class
    {
        var result = new List<FlatAssignment>();
        var page = await fetchFirst();
        while (page != null)
        {
            result.AddRange(Flatten(getItems(page)));
            var next = getNextLink(page);
            if (string.IsNullOrEmpty(next)) break;
            page = await fetchByUrl(next);
        }
        return result;
    }

    private static void AppendTarget(List<FlatAssignment> result,
        DeviceAndAppManagementAssignmentTarget? target)
    {
        switch (target)
        {
            case ExclusionGroupAssignmentTarget ex:
                result.Add(new FlatAssignment(ex.GroupId, true, false, false));
                break;
            case GroupAssignmentTarget g:
                result.Add(new FlatAssignment(g.GroupId, false, false, false));
                break;
            case AllLicensedUsersAssignmentTarget:
                result.Add(new FlatAssignment(null, false, true, false));
                break;
            case AllDevicesAssignmentTarget:
                result.Add(new FlatAssignment(null, false, false, true));
                break;
        }
    }

    private async Task<List<FlatAssignment>> FetchAppProtectionAssignmentsAsync(
        ManagedAppPolicy policy, CancellationToken ct)
    {
        var odataType = policy.OdataType ?? "";
        var id = policy.Id!;
        try
        {
            if (odataType.Contains("android", StringComparison.OrdinalIgnoreCase))
            {
                return await FetchAllAssignmentPagesAsync(
                    () => _graphClient.DeviceAppManagement
                        .AndroidManagedAppProtections[id].Assignments.GetAsync(cancellationToken: ct),
                    r => r.OdataNextLink, r => r.Value,
                    url => _graphClient.DeviceAppManagement
                        .AndroidManagedAppProtections[id].Assignments.WithUrl(url).GetAsync(cancellationToken: ct));
            }
            if (odataType.Contains("ios", StringComparison.OrdinalIgnoreCase))
            {
                return await FetchAllAssignmentPagesAsync(
                    () => _graphClient.DeviceAppManagement
                        .IosManagedAppProtections[id].Assignments.GetAsync(cancellationToken: ct),
                    r => r.OdataNextLink, r => r.Value,
                    url => _graphClient.DeviceAppManagement
                        .IosManagedAppProtections[id].Assignments.WithUrl(url).GetAsync(cancellationToken: ct));
            }
            if (odataType.Contains("windows", StringComparison.OrdinalIgnoreCase))
            {
                return await FetchAllAssignmentPagesAsync(
                    () => _graphClient.DeviceAppManagement
                        .WindowsManagedAppProtections[id].Assignments.GetAsync(cancellationToken: ct),
                    r => r.OdataNextLink, r => r.Value,
                    url => _graphClient.DeviceAppManagement
                        .WindowsManagedAppProtections[id].Assignments.WithUrl(url).GetAsync(cancellationToken: ct));
            }
        }
        catch { /* ignore per-policy errors */ }
        return [];
    }

    private static List<FlatAssignment> FlattenTargetedManagedApp(
        IList<TargetedManagedAppPolicyAssignment>? assignments)
    {
        if (assignments == null) return [];
        var result = new List<FlatAssignment>(assignments.Count);
        foreach (var a in assignments)
            AppendTarget(result, a.Target);
        return result;
    }

    private async Task ResolveGroupNamesAsync(IEnumerable<FlatAssignment> flat, CancellationToken ct)
    {
        var unresolved = flat
            .Where(f => f.GroupId != null && !_groupNameCache.ContainsKey(f.GroupId!))
            .Select(f => f.GroupId!)
            .Distinct()
            .ToList();

        if (unresolved.Count == 0) return;

        using var sem = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
        var tasks = unresolved.Select(async groupId =>
        {
            await sem.WaitAsync(ct);
            try
            {
                if (_groupNameCache.ContainsKey(groupId)) return;
                var group = await _graphClient.Groups[groupId]
                    .GetAsync(req => req.QueryParameters.Select = ["id", "displayName"], ct);
                _groupNameCache.TryAdd(groupId, group?.DisplayName ?? groupId);
            }
            catch (OperationCanceledException) { throw; }
            catch { _groupNameCache.TryAdd(groupId, groupId); }
            finally { sem.Release(); }
        });
        await Task.WhenAll(tasks);
        ct.ThrowIfCancellationRequested();
    }

    private static string SummariseAssignments(List<FlatAssignment> assignments,
        IReadOnlyDictionary<string, string>? groupNames = null)
    {
        var parts = new List<string>();
        foreach (var a in assignments)
        {
            if (a.IsAllUsers) parts.Add("All Users");
            else if (a.IsAllDevices) parts.Add("All Devices");
            else if (a.GroupId != null)
            {
                var displayName = groupNames?.TryGetValue(a.GroupId, out var n) == true ? n : a.GroupId;
                if (a.IsExclusion) parts.Add($"[Excluded] {displayName}");
                else parts.Add($"Group: {displayName}");
            }
        }
        return parts.Count > 0 ? string.Join(", ", parts.Distinct()) : "Not Assigned";
    }

    // ──────────────────────────────────────────────────────────────────────────────
    //  Group ID collector (for empty-group mode first pass)
    // ──────────────────────────────────────────────────────────────────────────────

    private async Task<HashSet<string>> CollectAllAssignedGroupIdsAsync(CancellationToken ct)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        async Task CollectFromAsync<T>(List<T> items, Func<T, string> getId,
            Func<string, Task<List<FlatAssignment>>> getAssignments)
        {
            using var sem = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
            var tasks = items.Select(async item =>
            {
                try
                {
                    await sem.WaitAsync(ct);
                    try
                    {
                        var id = getId(item);
                        if (string.IsNullOrEmpty(id)) return;
                        var flat = await getAssignments(id);
                        foreach (var fa in flat.Where(f => f.GroupId != null))
                            lock (ids) ids.Add(fa.GroupId!);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { }
                    finally { sem.Release(); }
                }
                catch (OperationCanceledException) { /* consolidated into single throw below */ }
            });
            await Task.WhenAll(tasks);
            ct.ThrowIfCancellationRequested();
        }

        // ── All 11 policy types ──────────────────────────────────────────────────

        var configs = await FetchDeviceConfigurationsAsync(ct);
        await CollectFromAsync(configs, p => p.Id!,
            id => FetchAllAssignmentPagesAsync(
                () => _graphClient.DeviceManagement.DeviceConfigurations[id]
                    .Assignments.GetAsync(cancellationToken: ct),
                r => r.OdataNextLink, r => r.Value,
                url => _graphClient.DeviceManagement.DeviceConfigurations[id]
                    .Assignments.WithUrl(url).GetAsync(cancellationToken: ct)));

        var scPolicies = await FetchSettingsCatalogAsync(ct);
        await CollectFromAsync(scPolicies, p => p.Id!,
            id => FetchAllAssignmentPagesAsync(
                () => _graphClient.DeviceManagement.ConfigurationPolicies[id]
                    .Assignments.GetAsync(cancellationToken: ct),
                r => r.OdataNextLink, r => r.Value,
                url => _graphClient.DeviceManagement.ConfigurationPolicies[id]
                    .Assignments.WithUrl(url).GetAsync(cancellationToken: ct)));

        var adminTemplates = await FetchAdminTemplatesAsync(ct);
        await CollectFromAsync(adminTemplates, p => p.Id!,
            id => FetchAllAssignmentPagesAsync(
                () => _graphClient.DeviceManagement.GroupPolicyConfigurations[id]
                    .Assignments.GetAsync(cancellationToken: ct),
                r => r.OdataNextLink, r => r.Value,
                url => _graphClient.DeviceManagement.GroupPolicyConfigurations[id]
                    .Assignments.WithUrl(url).GetAsync(cancellationToken: ct)));

        var cpPolicies = await FetchCompliancePoliciesAsync(ct);
        await CollectFromAsync(cpPolicies, p => p.Id!,
            id => FetchAllAssignmentPagesAsync(
                () => _graphClient.DeviceManagement.DeviceCompliancePolicies[id]
                    .Assignments.GetAsync(cancellationToken: ct),
                r => r.OdataNextLink, r => r.Value,
                url => _graphClient.DeviceManagement.DeviceCompliancePolicies[id]
                    .Assignments.WithUrl(url).GetAsync(cancellationToken: ct)));

        var appConfigPolicies = await FetchMobileAppConfigurationsAsync(ct);
        await CollectFromAsync(appConfigPolicies, p => p.Id!,
            id => FetchAllAssignmentPagesAsync(
                () => _graphClient.DeviceAppManagement.MobileAppConfigurations[id]
                    .Assignments.GetAsync(cancellationToken: ct),
                r => r.OdataNextLink, r => r.Value,
                url => _graphClient.DeviceAppManagement.MobileAppConfigurations[id]
                    .Assignments.WithUrl(url).GetAsync(cancellationToken: ct)));

        var apps = await FetchApplicationsAsync(ct);
        await CollectFromAsync(apps, p => p.Id!,
            id => FetchAllAssignmentPagesAsync(
                () => _graphClient.DeviceAppManagement.MobileApps[id]
                    .Assignments.GetAsync(cancellationToken: ct),
                r => r.OdataNextLink, r => r.Value,
                url => _graphClient.DeviceAppManagement.MobileApps[id]
                    .Assignments.WithUrl(url).GetAsync(cancellationToken: ct)));

        var scripts = await FetchPlatformScriptsAsync(ct);
        await CollectFromAsync(scripts, p => p.Id!,
            id => FetchAllAssignmentPagesAsync(
                () => _graphClient.DeviceManagement.DeviceManagementScripts[id]
                    .Assignments.GetAsync(cancellationToken: ct),
                r => r.OdataNextLink, r => r.Value,
                url => _graphClient.DeviceManagement.DeviceManagementScripts[id]
                    .Assignments.WithUrl(url).GetAsync(cancellationToken: ct)));

        var healthScripts = await FetchHealthScriptsAsync(ct);
        await CollectFromAsync(healthScripts, p => p.Id!,
            id => FetchAllAssignmentPagesAsync(
                () => _graphClient.DeviceManagement.DeviceHealthScripts[id]
                    .Assignments.GetAsync(cancellationToken: ct),
                r => r.OdataNextLink, r => r.Value,
                url => _graphClient.DeviceManagement.DeviceHealthScripts[id]
                    .Assignments.WithUrl(url).GetAsync(cancellationToken: ct)));

        var intents = await FetchEndpointSecurityIntentsAsync(ct);
        await CollectFromAsync(intents, p => p.Id!,
            id => FetchAllAssignmentPagesAsync(
                () => _graphClient.DeviceManagement.Intents[id]
                    .Assignments.GetAsync(cancellationToken: ct),
                r => r.OdataNextLink, r => r.Value,
                url => _graphClient.DeviceManagement.Intents[id]
                    .Assignments.WithUrl(url).GetAsync(cancellationToken: ct)));

        var enrollmentConfigs = await FetchEnrollmentConfigurationsAsync(ct);
        await CollectFromAsync(enrollmentConfigs, p => p.Id!,
            id => FetchAllAssignmentPagesAsync(
                () => _graphClient.DeviceManagement.DeviceEnrollmentConfigurations[id]
                    .Assignments.GetAsync(cancellationToken: ct),
                r => r.OdataNextLink, r => r.Value,
                url => _graphClient.DeviceManagement.DeviceEnrollmentConfigurations[id]
                    .Assignments.WithUrl(url).GetAsync(cancellationToken: ct)));

        // App protection policies need special handling (type-specific endpoints)
        var appProtPolicies = await FetchAppProtectionPoliciesAsync(ct);
        using var appProtSem = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
        var appProtTasks = appProtPolicies.Select(async policy =>
        {
            try
            {
                await appProtSem.WaitAsync(ct);
                try
                {
                    if (policy.Id == null) return;
                    var flat = await FetchAppProtectionAssignmentsAsync(policy, ct);
                    foreach (var fa in flat.Where(f => f.GroupId != null))
                        lock (ids) ids.Add(fa.GroupId!);
                }
                catch (OperationCanceledException) { throw; }
                catch { }
                finally { appProtSem.Release(); }
            }
            catch (OperationCanceledException) { }
        });
        await Task.WhenAll(appProtTasks);
        ct.ThrowIfCancellationRequested();

        return ids;
    }

    // ──────────────────────────────────────────────────────────────────────────────
    //  Transitive group membership helpers
    // ──────────────────────────────────────────────────────────────────────────────

    private async Task<HashSet<string>> GetUserTransitiveMemberGroupIdsAsync(
        string userId, CancellationToken ct)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resp = await _graphClient.Users[userId].TransitiveMemberOf
            .GetAsync(req => req.QueryParameters.Select = ["id"], ct);

        while (resp?.Value != null)
        {
            foreach (var obj in resp.Value)
                if (!string.IsNullOrEmpty(obj.Id)) ids.Add(obj.Id!);

            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.Users[userId].TransitiveMemberOf
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        return ids;
    }

    private async Task<HashSet<string>> GetDeviceTransitiveMemberGroupIdsAsync(
        string deviceId, CancellationToken ct)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resp = await _graphClient.Devices[deviceId].TransitiveMemberOf
            .GetAsync(req => req.QueryParameters.Select = ["id"], ct);

        while (resp?.Value != null)
        {
            foreach (var obj in resp.Value)
                if (!string.IsNullOrEmpty(obj.Id)) ids.Add(obj.Id!);

            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.Devices[deviceId].TransitiveMemberOf
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        return ids;
    }

    public async Task PrefetchAllToCacheAsync(
        Action<string>? progress = null,
        CancellationToken cancellationToken = default,
        bool forceRefresh = false)
    {
        _forceRefresh = forceRefresh;
        try
        {
            // Build the complete ordered list of fetch tasks.
            // Each Fetch*Async checks the cache first internally, so if all entries are warm
            // they all return instantly.  When hitting Graph we use RunBatchedAsync to cap
            // concurrent Graph calls at 5 at a time, avoiding throttling.
            var fetchers = new List<(string Label, Func<Task> Fetch)>
            {
                ("device configurations",             () => FetchDeviceConfigurationsAsync(cancellationToken)),
                ("settings catalog policies",         () => FetchSettingsCatalogAsync(cancellationToken)),
                ("administrative templates",          () => FetchAdminTemplatesAsync(cancellationToken)),
                ("compliance policies",               () => FetchCompliancePoliciesAsync(cancellationToken)),
                ("app protection policies",           () => FetchAppProtectionPoliciesAsync(cancellationToken)),
                ("app configuration policies",        () => FetchMobileAppConfigurationsAsync(cancellationToken)),
                ("applications",                      () => FetchApplicationsAsync(cancellationToken)),
                ("platform scripts",                  () => FetchPlatformScriptsAsync(cancellationToken)),
                ("health scripts",                    () => FetchHealthScriptsAsync(cancellationToken)),
                ("endpoint security intents",         () => FetchEndpointSecurityIntentsAsync(cancellationToken)),
                ("enrollment configurations",         () => FetchEnrollmentConfigurationsAsync(cancellationToken)),
                ("conditional access policies",       () => FetchConditionalAccessPoliciesAsync(cancellationToken)),
                ("assignment filters",                () => FetchAssignmentFiltersAsync(cancellationToken)),
                ("policy sets",                       () => FetchPolicySetsAsync(cancellationToken)),
                ("terms and conditions",              () => FetchTermsAndConditionsAsync(cancellationToken)),
                ("scope tags",                        () => FetchScopeTagsAsync(cancellationToken)),
                ("role definitions",                  () => FetchRoleDefinitionsAsync(cancellationToken)),
                ("Intune branding profiles",          () => FetchIntuneBrandingProfilesAsync(cancellationToken)),
                ("Azure branding localizations",      () => FetchAzureBrandingLocalizationsAsync(cancellationToken)),
                ("Autopilot profiles",                () => FetchAutopilotProfilesAsync(cancellationToken)),
                ("Mac custom attributes",             () => FetchMacCustomAttributesAsync(cancellationToken)),
                ("feature update profiles",           () => FetchFeatureUpdateProfilesAsync(cancellationToken)),
                ("device shell scripts",              () => FetchDeviceShellScriptsAsync(cancellationToken)),
                ("compliance scripts",                () => FetchComplianceScriptsAsync(cancellationToken)),
                ("named locations",                   () => FetchNamedLocationsAsync(cancellationToken)),
                ("authentication strength policies",  () => FetchAuthenticationStrengthPoliciesAsync(cancellationToken)),
                ("authentication context references", () => FetchAuthenticationContextClassReferencesAsync(cancellationToken)),
                ("terms of use agreements",           () => FetchTermsOfUseAgreementsAsync(cancellationToken)),
                ("targeted managed app configs",      () => FetchTargetedManagedAppConfigurationsAsync(cancellationToken)),
                ("dynamic groups",                    () => FetchDynamicGroupsAsync(cancellationToken)),
                ("assigned groups",                   () => FetchAssignedGroupsAsync(cancellationToken)),
                // Note: DeviceShellScripts key reused by FetchPlatformScriptsAsync; separate entry kept for DeviceManagementScripts
                ("device management scripts",         () => FetchPlatformScriptsAsync(cancellationToken)),
            };

            // Determine whether every fetcher would hit cache (i.e. all entries are warm).
            // We do this by first trying all TryGetFromCache calls — zero I/O cost.
            // If everything is cached, run all 32 in parallel (instant returns).
            // If anything needs Graph, use batches of 5 to cap concurrent Graph calls.
            bool allCached = !forceRefresh && AllFetchersAreCached();

            // Per-fetcher isolation: a single type that errors — a permission gap, or a
            // Graph endpoint that rejects a query option — must NOT abort the whole warm.
            // Cache what we can, skip what we can't; cancellation still propagates.
            async Task SafeFetch((string Label, Func<Task> Fetch) f)
            {
                try { await f.Fetch(); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { progress?.Invoke($"Skipped {f.Label}: {ex.Message}"); }
            }

            if (allCached)
            {
                progress?.Invoke("Loading all policy data from cache...");
                await Task.WhenAll(fetchers.Select(f => SafeFetch(f)));
            }
            else
            {
                const int batchSize = 5;
                int completed = 0;
                for (int i = 0; i < fetchers.Count; i += batchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var batch = fetchers.Skip(i).Take(batchSize).ToList();
                    var labels = string.Join(", ", batch.Select(f => f.Label));
                    progress?.Invoke($"Downloading {labels}...");
                    await Task.WhenAll(batch.Select(f => SafeFetch(f)));
                    completed += batch.Count;
                    progress?.Invoke($"Downloaded {completed} of {fetchers.Count} types...");
                }
            }

            progress?.Invoke("All policy data downloaded and cached.");
        }
        finally
        {
            _forceRefresh = false;
        }
    }

    /// <summary>
    /// Returns true only when every well-known cache key already has a live (non-expired) entry.
    /// Uses GetMetadata (no JSON deserialization) for an efficient existence check.
    /// </summary>
    private bool AllFetchersAreCached()
    {
        if (_cacheService == null || _tenantId == null) return false;
        string[] keys =
        [
            "DeviceConfigurations", "SettingsCatalog", "AdministrativeTemplates",
            "CompliancePolicies", "AppProtectionPolicies", "ManagedDeviceAppConfigurations",
            "Applications", "DeviceManagementScripts", "DeviceHealthScripts",
            "EndpointSecurityIntents", "EnrollmentConfigurations", "ConditionalAccessPolicies",
            "AssignmentFilters", "PolicySets", "TermsAndConditions", "ScopeTags",
            "RoleDefinitions", "IntuneBrandingProfiles", "AzureBrandingLocalizations",
            "AutopilotProfiles", "MacCustomAttributes", "FeatureUpdateProfiles",
            "DeviceShellScripts", "ComplianceScripts", "NamedLocations",
            "AuthenticationStrengths", "AuthenticationContexts", "TermsOfUseAgreements",
            "TargetedManagedAppConfigurations", CacheKeyCheckerDynamic, CacheKeyCheckerAssigned,
        ];
        return keys.All(k => _cacheService.GetMetadata(_tenantId, k) != null);
    }

    // ──────────────────────────────────────────────────────────────────────────────
    //  Policy fetchers (with pagination)
    // ──────────────────────────────────────────────────────────────────────────────

    private async Task<List<DeviceConfiguration>> FetchDeviceConfigurationsAsync(CancellationToken ct)
    {
        if (TryGetFromCache<DeviceConfiguration>("DeviceConfigurations") is { } cached) return cached;
        var result = new List<DeviceConfiguration>();
        var resp = await _graphClient.DeviceManagement.DeviceConfigurations.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "displayName"];
                req.QueryParameters.Top = 999;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.DeviceManagement.DeviceConfigurations
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("DeviceConfigurations", result);
        return result;
    }

    private async Task<List<DeviceManagementConfigurationPolicy>> FetchSettingsCatalogAsync(CancellationToken ct)
    {
        if (TryGetFromCache<DeviceManagementConfigurationPolicy>("SettingsCatalog") is { } cached) return cached;
        var result = new List<DeviceManagementConfigurationPolicy>();
        var resp = await _graphClient.DeviceManagement.ConfigurationPolicies.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "name", "platforms", "technologies", "templateReference"];
                req.QueryParameters.Top = 999;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.DeviceManagement.ConfigurationPolicies
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("SettingsCatalog", result);
        return result;
    }

    private async Task<List<GroupPolicyConfiguration>> FetchAdminTemplatesAsync(CancellationToken ct)
    {
        if (TryGetFromCache<GroupPolicyConfiguration>("AdministrativeTemplates") is { } cached) return cached;
        var result = new List<GroupPolicyConfiguration>();
        var resp = await _graphClient.DeviceManagement.GroupPolicyConfigurations.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "displayName"];
                req.QueryParameters.Top = 999;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.DeviceManagement.GroupPolicyConfigurations
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("AdministrativeTemplates", result);
        return result;
    }

    private async Task<List<DeviceCompliancePolicy>> FetchCompliancePoliciesAsync(CancellationToken ct)
    {
        if (TryGetFromCache<DeviceCompliancePolicy>("CompliancePolicies") is { } cached) return cached;
        var result = new List<DeviceCompliancePolicy>();
        var resp = await _graphClient.DeviceManagement.DeviceCompliancePolicies.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "displayName"];
                req.QueryParameters.Top = 999;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.DeviceManagement.DeviceCompliancePolicies
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("CompliancePolicies", result);
        return result;
    }

    private async Task<List<ManagedAppPolicy>> FetchAppProtectionPoliciesAsync(CancellationToken ct)
    {
        if (TryGetFromCache<ManagedAppPolicy>("AppProtectionPolicies") is { } cached) return cached;
        var result = new List<ManagedAppPolicy>();
        var resp = await _graphClient.DeviceAppManagement.ManagedAppPolicies.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "displayName"];
                req.QueryParameters.Top = 999;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.DeviceAppManagement.ManagedAppPolicies
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("AppProtectionPolicies", result);
        return result;
    }

    private async Task<List<ManagedDeviceMobileAppConfiguration>> FetchMobileAppConfigurationsAsync(CancellationToken ct)
    {
        if (TryGetFromCache<ManagedDeviceMobileAppConfiguration>("ManagedDeviceAppConfigurations") is { } cached) return cached;
        var result = new List<ManagedDeviceMobileAppConfiguration>();
        var resp = await _graphClient.DeviceAppManagement.MobileAppConfigurations.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "displayName"];
                req.QueryParameters.Top = 999;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.DeviceAppManagement.MobileAppConfigurations
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("ManagedDeviceAppConfigurations", result);
        return result;
    }

    private async Task<List<MobileApp>> FetchApplicationsAsync(CancellationToken ct)
    {
        if (TryGetFromCache<MobileApp>("Applications") is { } cached) return cached;
        var result = new List<MobileApp>();
        var resp = await _graphClient.DeviceAppManagement.MobileApps.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "displayName", "isFeatured", "publisher"];
                req.QueryParameters.Filter = "isAssigned eq true";
                req.QueryParameters.Top = 999;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null)
                result.AddRange(resp.Value.Where(a => a.IsFeatured != true));
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.DeviceAppManagement.MobileApps
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("Applications", result);
        return result;
    }

    private async Task<List<DeviceManagementScript>> FetchPlatformScriptsAsync(CancellationToken ct)
    {
        if (TryGetFromCache<DeviceManagementScript>("DeviceManagementScripts") is { } cached) return cached;
        var result = new List<DeviceManagementScript>();
        var resp = await _graphClient.DeviceManagement.DeviceManagementScripts.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "displayName"];
                req.QueryParameters.Top = 999;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.DeviceManagement.DeviceManagementScripts
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("DeviceManagementScripts", result);
        return result;
    }

    private async Task<List<DeviceHealthScript>> FetchHealthScriptsAsync(CancellationToken ct)
    {
        if (TryGetFromCache<DeviceHealthScript>("DeviceHealthScripts") is { } cached) return cached;
        var result = new List<DeviceHealthScript>();
        var resp = await _graphClient.DeviceManagement.DeviceHealthScripts.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "displayName"];
                req.QueryParameters.Top = 999;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.DeviceManagement.DeviceHealthScripts
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("DeviceHealthScripts", result);
        return result;
    }

    private async Task<List<DeviceManagementIntent>> FetchEndpointSecurityIntentsAsync(CancellationToken ct)
    {
        if (TryGetFromCache<DeviceManagementIntent>("EndpointSecurityIntents") is { } cached) return cached;
        var result = new List<DeviceManagementIntent>();
        var resp = await _graphClient.DeviceManagement.Intents.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "displayName", "templateId"];
                req.QueryParameters.Top = 999;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.DeviceManagement.Intents
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("EndpointSecurityIntents", result);
        return result;
    }

    private async Task<List<DeviceEnrollmentConfiguration>> FetchEnrollmentConfigurationsAsync(CancellationToken ct)
    {
        if (TryGetFromCache<DeviceEnrollmentConfiguration>("EnrollmentConfigurations") is { } cached) return cached;
        var result = new List<DeviceEnrollmentConfiguration>();
        var resp = await _graphClient.DeviceManagement.DeviceEnrollmentConfigurations.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "displayName"];
                req.QueryParameters.Top = 999;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.DeviceManagement.DeviceEnrollmentConfigurations
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("EnrollmentConfigurations", result);
        return result;
    }

    private async Task<List<ConditionalAccessPolicy>> FetchConditionalAccessPoliciesAsync(CancellationToken ct)
    {
        if (TryGetFromCache<ConditionalAccessPolicy>("ConditionalAccessPolicies") is { } cached) return cached;
        var result = new List<ConditionalAccessPolicy>();
        var resp = await _graphClient.Identity.ConditionalAccess.Policies.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "displayName", "state"];
                req.QueryParameters.Top = 200;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.Identity.ConditionalAccess.Policies
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("ConditionalAccessPolicies", result);
        return result;
    }

    private async Task<List<DeviceAndAppManagementAssignmentFilter>> FetchAssignmentFiltersAsync(CancellationToken ct)
    {
        if (TryGetFromCache<DeviceAndAppManagementAssignmentFilter>("AssignmentFilters") is { } cached) return cached;
        var result = new List<DeviceAndAppManagementAssignmentFilter>();
        var resp = await _graphClient.DeviceManagement.AssignmentFilters.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "displayName", "platform"];
                req.QueryParameters.Top = 200;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.DeviceManagement.AssignmentFilters
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("AssignmentFilters", result);
        return result;
    }

    private async Task<List<PolicySet>> FetchPolicySetsAsync(CancellationToken ct)
    {
        if (TryGetFromCache<PolicySet>("PolicySets") is { } cached) return cached;
        var result = new List<PolicySet>();
        var resp = await _graphClient.DeviceAppManagement.PolicySets.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "displayName", "status"];
                req.QueryParameters.Top = 200;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.DeviceAppManagement.PolicySets
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("PolicySets", result);
        return result;
    }

    private async Task<List<TermsAndConditions>> FetchTermsAndConditionsAsync(CancellationToken ct)
    {
        if (TryGetFromCache<TermsAndConditions>("TermsAndConditions") is { } cached) return cached;
        var result = new List<TermsAndConditions>();
        var resp = await _graphClient.DeviceManagement.TermsAndConditions.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "displayName", "version"];
                req.QueryParameters.Top = 200;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.DeviceManagement.TermsAndConditions
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("TermsAndConditions", result);
        return result;
    }

    private async Task<List<RoleScopeTag>> FetchScopeTagsAsync(CancellationToken ct)
    {
        if (TryGetFromCache<RoleScopeTag>("ScopeTags") is { } cached) return cached;
        var result = new List<RoleScopeTag>();
        var resp = await _graphClient.DeviceManagement.RoleScopeTags.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "displayName"];
                req.QueryParameters.Top = 200;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.DeviceManagement.RoleScopeTags
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("ScopeTags", result);
        return result;
    }

    private async Task<List<RoleDefinition>> FetchRoleDefinitionsAsync(CancellationToken ct)
    {
        if (TryGetFromCache<RoleDefinition>("RoleDefinitions") is { } cached) return cached;
        var result = new List<RoleDefinition>();
        var resp = await _graphClient.DeviceManagement.RoleDefinitions.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "displayName", "isBuiltIn"];
                req.QueryParameters.Top = 200;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.DeviceManagement.RoleDefinitions
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("RoleDefinitions", result);
        return result;
    }

    private async Task<List<IntuneBrandingProfile>> FetchIntuneBrandingProfilesAsync(CancellationToken ct)
    {
        if (TryGetFromCache<IntuneBrandingProfile>("IntuneBrandingProfiles") is { } cached) return cached;
        var result = new List<IntuneBrandingProfile>();
        var resp = await _graphClient.DeviceManagement.IntuneBrandingProfiles.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "displayName"];
                req.QueryParameters.Top = 200;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.DeviceManagement.IntuneBrandingProfiles
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("IntuneBrandingProfiles", result);
        return result;
    }

    private async Task<List<OrganizationalBrandingLocalization>> FetchAzureBrandingLocalizationsAsync(CancellationToken ct)
    {
        if (TryGetFromCache<OrganizationalBrandingLocalization>("AzureBrandingLocalizations") is { } cached) return cached;
        var result = new List<OrganizationalBrandingLocalization>();
        var orgResp = await _graphClient.Organization.GetAsync(req => req.QueryParameters.Top = 1, ct);
        var orgId = orgResp?.Value?.FirstOrDefault()?.Id;
        if (string.IsNullOrEmpty(orgId)) return result;
        var resp = await _graphClient.Organization[orgId].Branding.Localizations.GetAsync(
            req => req.QueryParameters.Top = 200, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.Organization[orgId].Branding.Localizations
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("AzureBrandingLocalizations", result);
        return result;
    }

    private async Task<List<WindowsAutopilotDeploymentProfile>> FetchAutopilotProfilesAsync(CancellationToken ct)
    {
        if (TryGetFromCache<WindowsAutopilotDeploymentProfile>("AutopilotProfiles") is { } cached) return cached;
        var result = new List<WindowsAutopilotDeploymentProfile>();
        var resp = await _graphClient.DeviceManagement.WindowsAutopilotDeploymentProfiles.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "displayName"];
                req.QueryParameters.Top = 200;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.DeviceManagement.WindowsAutopilotDeploymentProfiles
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("AutopilotProfiles", result);
        return result;
    }

    private async Task<List<DeviceCustomAttributeShellScript>> FetchMacCustomAttributesAsync(CancellationToken ct)
    {
        if (TryGetFromCache<DeviceCustomAttributeShellScript>("MacCustomAttributes") is { } cached) return cached;
        var result = new List<DeviceCustomAttributeShellScript>();
        var resp = await _graphClient.DeviceManagement.DeviceCustomAttributeShellScripts.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "displayName"];
                req.QueryParameters.Top = 200;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.DeviceManagement.DeviceCustomAttributeShellScripts
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("MacCustomAttributes", result);
        return result;
    }

    private async Task<List<WindowsFeatureUpdateProfile>> FetchFeatureUpdateProfilesAsync(CancellationToken ct)
    {
        if (TryGetFromCache<WindowsFeatureUpdateProfile>("FeatureUpdateProfiles") is { } cached) return cached;
        var result = new List<WindowsFeatureUpdateProfile>();
        var resp = await _graphClient.DeviceManagement.WindowsFeatureUpdateProfiles.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "displayName"];
                req.QueryParameters.Top = 200;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.DeviceManagement.WindowsFeatureUpdateProfiles
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("FeatureUpdateProfiles", result);
        return result;
    }

    private async Task<List<DeviceShellScript>> FetchDeviceShellScriptsAsync(CancellationToken ct)
    {
        if (TryGetFromCache<DeviceShellScript>("DeviceShellScripts") is { } cached) return cached;
        var result = new List<DeviceShellScript>();
        var resp = await _graphClient.DeviceManagement.DeviceShellScripts.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "displayName"];
                req.QueryParameters.Top = 200;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.DeviceManagement.DeviceShellScripts
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("DeviceShellScripts", result);
        return result;
    }

    private async Task<List<DeviceComplianceScript>> FetchComplianceScriptsAsync(CancellationToken ct)
    {
        if (TryGetFromCache<DeviceComplianceScript>("ComplianceScripts") is { } cached) return cached;
        var result = new List<DeviceComplianceScript>();
        var resp = await _graphClient.DeviceManagement.DeviceComplianceScripts.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "displayName"];
                req.QueryParameters.Top = 200;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.DeviceManagement.DeviceComplianceScripts
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("ComplianceScripts", result);
        return result;
    }

    private async Task<List<NamedLocation>> FetchNamedLocationsAsync(CancellationToken ct)
    {
        if (TryGetFromCache<NamedLocation>("NamedLocations") is { } cached) return cached;
        var result = new List<NamedLocation>();
        var resp = await _graphClient.Identity.ConditionalAccess.NamedLocations.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "displayName"];
                req.QueryParameters.Top = 200;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.Identity.ConditionalAccess.NamedLocations
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("NamedLocations", result);
        return result;
    }

    private async Task<List<AuthenticationStrengthPolicy>> FetchAuthenticationStrengthPoliciesAsync(CancellationToken ct)
    {
        if (TryGetFromCache<AuthenticationStrengthPolicy>("AuthenticationStrengths") is { } cached) return cached;
        var result = new List<AuthenticationStrengthPolicy>();
        var resp = await _graphClient.Identity.ConditionalAccess.AuthenticationStrength.Policies.GetAsync(
            req =>
            {
                // NOTE: the authenticationStrength/policies endpoint rejects $top
                // ("Query option 'Top' is not allowed") — it returns the full (small)
                // set in one page, so we only $select and never paginate with $top.
                req.QueryParameters.Select = ["id", "displayName", "policyType"];
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.Identity.ConditionalAccess.AuthenticationStrength.Policies
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("AuthenticationStrengths", result);
        return result;
    }

    private async Task<List<AuthenticationContextClassReference>> FetchAuthenticationContextClassReferencesAsync(CancellationToken ct)
    {
        if (TryGetFromCache<AuthenticationContextClassReference>("AuthenticationContexts") is { } cached) return cached;
        var result = new List<AuthenticationContextClassReference>();
        var resp = await _graphClient.Identity.ConditionalAccess.AuthenticationContextClassReferences.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "displayName", "isAvailable"];
                req.QueryParameters.Top = 200;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.Identity.ConditionalAccess.AuthenticationContextClassReferences
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("AuthenticationContexts", result);
        return result;
    }

    private async Task<List<Agreement>> FetchTermsOfUseAgreementsAsync(CancellationToken ct)
    {
        if (TryGetFromCache<Agreement>("TermsOfUseAgreements") is { } cached) return cached;
        var result = new List<Agreement>();
        var resp = await _graphClient.IdentityGovernance.TermsOfUse.Agreements.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "displayName"];
                req.QueryParameters.Top = 200;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.IdentityGovernance.TermsOfUse.Agreements
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("TermsOfUseAgreements", result);
        return result;
    }

    private async Task<List<TargetedManagedAppConfiguration>> FetchTargetedManagedAppConfigurationsAsync(CancellationToken ct)
    {
        if (TryGetFromCache<TargetedManagedAppConfiguration>("TargetedManagedAppConfigurations") is { } cached) return cached;
        var result = new List<TargetedManagedAppConfiguration>();
        var resp = await _graphClient.DeviceAppManagement.TargetedManagedAppConfigurations.GetAsync(
            req =>
            {
                req.QueryParameters.Select = ["id", "displayName"];
                req.QueryParameters.Top = 200;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.DeviceAppManagement.TargetedManagedAppConfigurations
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache("TargetedManagedAppConfigurations", result);
        return result;
    }

    // Separate cache key from the ViewModel's "DynamicGroups" (which stores List<GroupRow>)
    // to avoid type collision in LiteDB.
    private const string CacheKeyCheckerDynamic = "checker_DynamicGroups";
    private const string CacheKeyCheckerAssigned = "checker_AssignedGroups";

    private async Task<List<Group>> FetchDynamicGroupsAsync(CancellationToken ct)
    {
        if (TryGetFromCache<Group>(CacheKeyCheckerDynamic) is { } cached) return cached;
        var result = new List<Group>();
        var resp = await _graphClient.Groups.GetAsync(
            req =>
            {
                req.QueryParameters.Filter = "groupTypes/any(g:g eq 'DynamicMembership')";
                req.QueryParameters.Select = ["id", "displayName", "description", "groupTypes",
                    "membershipRule", "membershipRuleProcessingState",
                    "securityEnabled", "mailEnabled", "createdDateTime", "mail"];
                req.QueryParameters.Top = 200;
                req.Headers.Add("ConsistencyLevel", "eventual");
                req.QueryParameters.Count = true;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.Groups
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache(CacheKeyCheckerDynamic, result);
        return result;
    }

    private async Task<List<Group>> FetchAssignedGroupsAsync(CancellationToken ct)
    {
        if (TryGetFromCache<Group>(CacheKeyCheckerAssigned) is { } cached) return cached;
        var result = new List<Group>();
        // Server-side filter: exclude dynamic-membership groups to avoid downloading all groups
        var resp = await _graphClient.Groups.GetAsync(
            req =>
            {
                req.QueryParameters.Filter = "not groupTypes/any(g:g eq 'DynamicMembership')";
                req.QueryParameters.Select = ["id", "displayName", "description", "groupTypes",
                    "membershipRule", "membershipRuleProcessingState",
                    "securityEnabled", "mailEnabled", "createdDateTime", "mail"];
                req.QueryParameters.Top = 200;
                req.Headers.Add("ConsistencyLevel", "eventual");
                req.QueryParameters.Count = true;
            }, ct);
        while (resp != null)
        {
            if (resp.Value != null) result.AddRange(resp.Value);
            if (!string.IsNullOrEmpty(resp.OdataNextLink))
                resp = await _graphClient.Groups
                    .WithUrl(resp.OdataNextLink).GetAsync(cancellationToken: ct);
            else break;
        }
        TrySetCache(CacheKeyCheckerAssigned, result);
        return result;
    }

    // ──────────────────────────────────────────────────────────────────────────────
    //  Utility
    // ──────────────────────────────────────────────────────────────────────────────

    private static string PlatformFromOData(string? odataType)
    {
        if (string.IsNullOrEmpty(odataType)) return "";
        var lower = odataType.ToLowerInvariant();
        if (lower.Contains("windows") || lower.Contains("win32") || lower.Contains("msi")) return "Windows";
        if (lower.Contains("ios") || lower.Contains("iphone")) return "iOS";
        if (lower.Contains("macos") || lower.Contains("mac")) return "macOS";
        if (lower.Contains("android")) return "Android";
        if (lower.Contains("web")) return "Web";
        return "";
    }

    private static readonly Comparison<AssignmentReportRow> PolicyTypeNameComparer = (a, b) =>
    {
        var t = string.Compare(a.PolicyType, b.PolicyType, StringComparison.OrdinalIgnoreCase);
        return t != 0 ? t : string.Compare(a.PolicyName, b.PolicyName, StringComparison.OrdinalIgnoreCase);
    };
}
