using IntuneManager.Core.Models;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace IntuneManager.Core.Services;

public class GroupService : IGroupService
{
    private readonly GraphServiceClient _graphClient;

    public GroupService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<Group>> ListDynamicGroupsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<Group>();

        var response = await _graphClient.Groups.GetAsync(req =>
        {
            req.QueryParameters.Filter = "groupTypes/any(g:g eq 'DynamicMembership')";
            req.QueryParameters.Select = new[]
            {
                "id", "displayName", "description", "groupTypes",
                "membershipRule", "membershipRuleProcessingState",
                "securityEnabled", "mailEnabled", "createdDateTime",
                "mail"
            };
            req.QueryParameters.Top = 999;
            req.Headers.Add("ConsistencyLevel", "eventual");
            req.QueryParameters.Count = true;
        }, cancellationToken);

        while (response != null)
        {
            if (response.Value != null)
                result.AddRange(response.Value);

            if (!string.IsNullOrEmpty(response.OdataNextLink))
            {
                response = await _graphClient.Groups
                    .WithUrl(response.OdataNextLink)
                    .GetAsync(cancellationToken: cancellationToken);
            }
            else
            {
                break;
            }
        }

        return result;
    }

    public async Task<List<Group>> ListAssignedGroupsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<Group>();

        var response = await _graphClient.Groups.GetAsync(req =>
        {
            req.QueryParameters.Select = new[]
            {
                "id", "displayName", "description", "groupTypes",
                "membershipRule", "membershipRuleProcessingState",
                "securityEnabled", "mailEnabled", "createdDateTime",
                "mail"
            };
            req.QueryParameters.Top = 999;
            req.Headers.Add("ConsistencyLevel", "eventual");
            req.QueryParameters.Count = true;
        }, cancellationToken);

        while (response != null)
        {
            if (response.Value != null)
            {
                foreach (var item in response.Value)
                {
                    if (item.GroupTypes == null ||
                        !item.GroupTypes.Contains("DynamicMembership", StringComparer.OrdinalIgnoreCase))
                    {
                        result.Add(item);
                    }
                }
            }

            if (!string.IsNullOrEmpty(response.OdataNextLink))
            {
                response = await _graphClient.Groups
                    .WithUrl(response.OdataNextLink)
                    .GetAsync(cancellationToken: cancellationToken);
            }
            else
            {
                break;
            }
        }

        return result;
    }

    public async Task<GroupMemberCounts> GetMemberCountsAsync(string groupId, CancellationToken cancellationToken = default)
    {
        int users = 0, devices = 0, nestedGroups = 0;

        var response = await _graphClient.Groups[groupId].Members
            .GetAsync(req =>
            {
                req.QueryParameters.Select = new[] { "id" };
                req.QueryParameters.Top = 999;
            }, cancellationToken);

        while (response?.Value != null)
        {
            foreach (var member in response.Value)
            {
                switch (member.OdataType)
                {
                    case "#microsoft.graph.user":
                        users++;
                        break;
                    case "#microsoft.graph.device":
                        devices++;
                        break;
                    case "#microsoft.graph.group":
                        nestedGroups++;
                        break;
                }
            }

            if (!string.IsNullOrEmpty(response.OdataNextLink))
            {
                response = await _graphClient.Groups[groupId].Members
                    .WithUrl(response.OdataNextLink)
                    .GetAsync(cancellationToken: cancellationToken);
            }
            else
            {
                break;
            }
        }

        return new GroupMemberCounts(users, devices, nestedGroups, users + devices + nestedGroups);
    }

    public async Task<List<GroupMemberInfo>> ListGroupMembersAsync(string groupId, CancellationToken cancellationToken = default)
    {
        var result = new List<GroupMemberInfo>();

        var response = await _graphClient.Groups[groupId].Members
            .GetAsync(req =>
            {
                req.QueryParameters.Select = new[]
                {
                    "id", "displayName", "userPrincipalName", "mail",
                    "accountEnabled", "userType",
                    "operatingSystem", "operatingSystemVersion",
                    "isManaged", "isCompliant", "manufacturer", "model"
                };
                req.QueryParameters.Top = 999;
            }, cancellationToken);

        while (response?.Value != null)
        {
            foreach (var member in response.Value)
            {
                switch (member)
                {
                    case User u:
                        result.Add(new GroupMemberInfo(
                            MemberType: "User",
                            DisplayName: u.DisplayName ?? "",
                            SecondaryInfo: u.UserPrincipalName ?? "",
                            TertiaryInfo: u.Mail ?? "",
                            Status: u.AccountEnabled == true ? "Enabled" : u.AccountEnabled == false ? "Disabled" : "",
                            Id: u.Id ?? ""));
                        break;
                    case Device d:
                        var deviceStatus = new List<string>();
                        if (d.IsManaged == true) deviceStatus.Add("Managed");
                        if (d.IsCompliant == true) deviceStatus.Add("Compliant");
                        else if (d.IsCompliant == false) deviceStatus.Add("Not Compliant");

                        result.Add(new GroupMemberInfo(
                            MemberType: "Device",
                            DisplayName: d.DisplayName ?? "",
                            SecondaryInfo: string.IsNullOrEmpty(d.OperatingSystem) ? ""
                                : $"{d.OperatingSystem} {d.OperatingSystemVersion}".Trim(),
                            TertiaryInfo: string.IsNullOrEmpty(d.Manufacturer) ? ""
                                : $"{d.Manufacturer} {d.Model}".Trim(),
                            Status: string.Join(", ", deviceStatus),
                            Id: d.Id ?? ""));
                        break;
                    case Group g:
                        result.Add(new GroupMemberInfo(
                            MemberType: "Group",
                            DisplayName: g.DisplayName ?? "",
                            SecondaryInfo: InferGroupType(g),
                            TertiaryInfo: "",
                            Status: "",
                            Id: g.Id ?? ""));
                        break;
                }
            }

            if (!string.IsNullOrEmpty(response.OdataNextLink))
            {
                response = await _graphClient.Groups[groupId].Members
                    .WithUrl(response.OdataNextLink)
                    .GetAsync(cancellationToken: cancellationToken);
            }
            else
            {
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// Derives a friendly group-type label from the Graph <see cref="Group"/> properties.
    /// </summary>
    public static string InferGroupType(Group group)
    {
        if (group.GroupTypes?.Contains("Unified", StringComparer.OrdinalIgnoreCase) == true)
            return "Microsoft 365";

        if (group.SecurityEnabled == true)
            return group.MailEnabled == true ? "Mail-enabled Security" : "Security";

        if (group.MailEnabled == true)
            return "Distribution";

        return "Security";
    }

    public async Task<List<Group>> SearchGroupsAsync(string query, CancellationToken cancellationToken = default)
    {
        var result = new List<Group>();
        if (string.IsNullOrWhiteSpace(query)) return result;

        var trimmed = query.Trim();

        // Check if query is a GUID — if so, fetch by ID directly
        if (Guid.TryParse(trimmed, out _))
        {
            try
            {
                var group = await _graphClient.Groups[trimmed]
                    .GetAsync(req =>
                    {
                        req.QueryParameters.Select = new[]
                        {
                            "id", "displayName", "description", "groupTypes",
                            "membershipRule", "membershipRuleProcessingState",
                            "securityEnabled", "mailEnabled", "createdDateTime"
                        };
                    }, cancellationToken);

                if (group != null) result.Add(group);
            }
            catch
            {
                // Group not found — return empty
            }
            return result;
        }

        // Search by displayName startsWith
        var escaped = trimmed.Replace("'", "''");
        var response = await _graphClient.Groups.GetAsync(req =>
        {
            req.QueryParameters.Filter = $"startsWith(displayName, '{escaped}')";
            req.QueryParameters.Select = new[]
            {
                "id", "displayName", "description", "groupTypes",
                "membershipRule", "membershipRuleProcessingState",
                "securityEnabled", "mailEnabled", "createdDateTime"
            };
            req.QueryParameters.Top = 50;
            req.QueryParameters.Orderby = new[] { "displayName" };
            req.Headers.Add("ConsistencyLevel", "eventual");
            req.QueryParameters.Count = true;
        }, cancellationToken);

        if (response?.Value != null)
            result.AddRange(response.Value);

        return result;
    }

    public async Task<List<GroupAssignedObject>> GetGroupAssignmentsAsync(
        string groupId,
        IConfigurationProfileService configService,
        ICompliancePolicyService complianceService,
        IApplicationService appService,
        Action<string>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<GroupAssignedObject>();

        // --- Device Configurations ---
        progressCallback?.Invoke("Scanning device configurations...");
        var configs = await configService.ListDeviceConfigurationsAsync(cancellationToken);
        var configTotal = configs.Count;
        var configProcessed = 0;

        using var configSemaphore = new SemaphoreSlim(5, 5);
        var configTasks = configs.Select(async config =>
        {
            await configSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (config.Id == null) return;
                var assignments = await configService.GetAssignmentsAsync(config.Id, cancellationToken);
                foreach (var a in assignments)
                {
                    if (MatchesGroup(a.Target, groupId, out var isExclusion))
                    {
                        lock (results)
                        {
                            results.Add(new GroupAssignedObject
                            {
                                ObjectId = config.Id,
                                DisplayName = config.DisplayName ?? "",
                                ObjectType = FriendlyTypeName(config.OdataType),
                                Category = "Device Configuration",
                                Platform = InferPlatform(config.OdataType),
                                AssignmentIntent = isExclusion ? "Exclude" : "Include",
                                IsExclusion = isExclusion
                            });
                        }
                    }
                }
                var processed = Interlocked.Increment(ref configProcessed);
                if (processed % 10 == 0 || processed == configTotal)
                    progressCallback?.Invoke($"Scanning device configurations... {processed}/{configTotal}");
            }
            finally { configSemaphore.Release(); }
        }).ToList();
        await Task.WhenAll(configTasks);

        // --- Compliance Policies ---
        progressCallback?.Invoke("Scanning compliance policies...");
        var policies = await complianceService.ListCompliancePoliciesAsync(cancellationToken);
        var policyTotal = policies.Count;
        var policyProcessed = 0;

        using var policySemaphore = new SemaphoreSlim(5, 5);
        var policyTasks = policies.Select(async policy =>
        {
            await policySemaphore.WaitAsync(cancellationToken);
            try
            {
                if (policy.Id == null) return;
                var assignments = await complianceService.GetAssignmentsAsync(policy.Id, cancellationToken);
                foreach (var a in assignments)
                {
                    if (MatchesGroup(a.Target, groupId, out var isExclusion))
                    {
                        lock (results)
                        {
                            results.Add(new GroupAssignedObject
                            {
                                ObjectId = policy.Id,
                                DisplayName = policy.DisplayName ?? "",
                                ObjectType = FriendlyTypeName(policy.OdataType),
                                Category = "Compliance Policy",
                                Platform = InferPlatform(policy.OdataType),
                                AssignmentIntent = isExclusion ? "Exclude" : "Include",
                                IsExclusion = isExclusion
                            });
                        }
                    }
                }
                var processed = Interlocked.Increment(ref policyProcessed);
                if (processed % 10 == 0 || processed == policyTotal)
                    progressCallback?.Invoke($"Scanning compliance policies... {processed}/{policyTotal}");
            }
            finally { policySemaphore.Release(); }
        }).ToList();
        await Task.WhenAll(policyTasks);

        // --- Applications ---
        progressCallback?.Invoke("Scanning applications...");
        var apps = await appService.ListApplicationsAsync(cancellationToken);
        var appTotal = apps.Count;
        var appProcessed = 0;

        using var appSemaphore = new SemaphoreSlim(5, 5);
        var appTasks = apps.Select(async app =>
        {
            await appSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (app.Id == null) return;
                var assignments = await appService.GetAssignmentsAsync(app.Id, cancellationToken);
                foreach (var a in assignments)
                {
                    if (MatchesGroup(a.Target, groupId, out var isExclusion))
                    {
                        var intent = a.Intent?.ToString() ?? "";
                        lock (results)
                        {
                            results.Add(new GroupAssignedObject
                            {
                                ObjectId = app.Id,
                                DisplayName = app.DisplayName ?? "",
                                ObjectType = FriendlyTypeName(app.OdataType),
                                Category = "Application",
                                Platform = InferPlatform(app.OdataType),
                                AssignmentIntent = isExclusion ? "Exclude" : intent,
                                IsExclusion = isExclusion
                            });
                        }
                    }
                }
                var processed = Interlocked.Increment(ref appProcessed);
                if (processed % 10 == 0 || processed == appTotal)
                    progressCallback?.Invoke($"Scanning applications... {processed}/{appTotal}");
            }
            finally { appSemaphore.Release(); }
        }).ToList();
        await Task.WhenAll(appTasks);

        // Sort by category then name
        results.Sort((a, b) =>
        {
            var cmp = string.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase);
            return cmp != 0 ? cmp : string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
        });

        progressCallback?.Invoke($"Found {results.Count} assignment(s)");
        return results;
    }

    /// <summary>
    /// Checks whether an assignment target references the specified group ID.
    /// Also matches "All Users" and "All Devices" targets.
    /// </summary>
    private static bool MatchesGroup(DeviceAndAppManagementAssignmentTarget? target, string groupId, out bool isExclusion)
    {
        isExclusion = false;
        if (target == null) return false;

        switch (target)
        {
            case ExclusionGroupAssignmentTarget exclusionTarget:
                isExclusion = true;
                return string.Equals(exclusionTarget.GroupId, groupId, StringComparison.OrdinalIgnoreCase);

            case GroupAssignmentTarget groupTarget:
                return string.Equals(groupTarget.GroupId, groupId, StringComparison.OrdinalIgnoreCase);

            // AllDevicesAssignmentTarget and AllLicensedUsersAssignmentTarget are
            // not group-specific — they won't match a particular group ID.
            default:
                return false;
        }
    }

    private static string InferPlatform(string? odataType)
    {
        if (string.IsNullOrEmpty(odataType)) return "";
        var lower = odataType.ToLowerInvariant();
        if (lower.Contains("windows") || lower.Contains("win32") || lower.Contains("msi")) return "Windows";
        if (lower.Contains("ios") || lower.Contains("iphone")) return "iOS";
        if (lower.Contains("macos") || lower.Contains("mac")) return "macOS";
        if (lower.Contains("android")) return "Android";
        if (lower.Contains("webapp")) return "Web";
        return "";
    }

    private static string FriendlyTypeName(string? odataType)
    {
        if (string.IsNullOrEmpty(odataType)) return "";
        // "#microsoft.graph.windows10GeneralConfiguration" → "Windows10GeneralConfiguration"
        var lastDot = odataType.LastIndexOf('.');
        if (lastDot < 0) return odataType;
        var name = odataType[(lastDot + 1)..];
        return char.ToUpperInvariant(name[0]) + name[1..];
    }
}
