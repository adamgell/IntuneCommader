using Microsoft.Graph;
using Microsoft.Graph.Models;

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
            // Dynamic groups have "DynamicMembership" in groupTypes
            req.QueryParameters.Filter = "groupTypes/any(g:g eq 'DynamicMembership')";
            req.QueryParameters.Select = new[]
            {
                "id", "displayName", "description", "groupTypes",
                "membershipRule", "membershipRuleProcessingState",
                "securityEnabled", "mailEnabled", "createdDateTime",
                "mail"
            };
            req.Headers.Add("ConsistencyLevel", "eventual");
            req.QueryParameters.Count = true;
        }, cancellationToken);

        if (response != null)
        {
            var pageIterator = PageIterator<Group, GroupCollectionResponse>
                .CreatePageIterator(_graphClient, response, item =>
                {
                    result.Add(item);
                    return true;
                });

            await pageIterator.IterateAsync(cancellationToken);
        }

        return result;
    }

    public async Task<List<Group>> ListAssignedGroupsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<Group>();

        // Assigned groups: those that do NOT have DynamicMembership in groupTypes.
        // OData 'not' filter with lambda on groupTypes is not supported for groups,
        // so we fetch all groups and filter client-side.
        var response = await _graphClient.Groups.GetAsync(req =>
        {
            req.QueryParameters.Select = new[]
            {
                "id", "displayName", "description", "groupTypes",
                "membershipRule", "membershipRuleProcessingState",
                "securityEnabled", "mailEnabled", "createdDateTime",
                "mail"
            };
            req.Headers.Add("ConsistencyLevel", "eventual");
            req.QueryParameters.Count = true;
        }, cancellationToken);

        if (response != null)
        {
            var pageIterator = PageIterator<Group, GroupCollectionResponse>
                .CreatePageIterator(_graphClient, response, item =>
                {
                    // Exclude groups that have "DynamicMembership" in groupTypes
                    if (item.GroupTypes == null ||
                        !item.GroupTypes.Contains("DynamicMembership", StringComparer.OrdinalIgnoreCase))
                    {
                        result.Add(item);
                    }
                    return true;
                });

            await pageIterator.IterateAsync(cancellationToken);
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

        if (response?.Value != null)
        {
            var pageIterator = PageIterator<DirectoryObject, DirectoryObjectCollectionResponse>
                .CreatePageIterator(_graphClient, response, member =>
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
                        default:
                            // Service principals, contacts, etc. — count toward total only
                            break;
                    }
                    return true;
                });

            await pageIterator.IterateAsync(cancellationToken);
        }

        return new GroupMemberCounts(users, devices, nestedGroups, users + devices + nestedGroups);
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
}
