using Microsoft.Graph.Beta.Models;
using Intune.Commander.Core.Services;

namespace CmProjectX.Api;

// Groups (Entra) — live Graph list endpoints projected to ListItemDto, mirroring the
// /device-configs etc. examples in Program.cs. GroupService is READ ONLY here: the
// list combines assigned + dynamic groups into one normalized {id,title,subtitle,badge}
// feed. 409 = signed out. This file is the home for Microsoft.Graph.Beta.Models names
// so Program.cs stays Graph-type-free.
public static class GroupsEndpoints
{
    public static void MapGroups(this Microsoft.AspNetCore.Builder.WebApplication app)
    {
        // ─── groups (READ ONLY) ───────────────────────────────────────────────
        // GET /groups — assigned (static) groups + dynamic-membership groups, merged.
        // Title = DisplayName; Subtitle = GroupService.InferGroupType (Security / M365 /
        // Distribution …); no badge.
        app.MapGet("/groups", async (AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var svc = new GroupService(g);
            var assigned = await svc.ListAssignedGroupsAsync(ct);
            var dynamic = await svc.ListDynamicGroupsAsync(ct);
            var items = assigned.Concat(dynamic);
            return Results.Ok(items.Select(x => new ListItemDto(
                x.Id ?? "",
                x.DisplayName ?? "(unnamed)",
                GroupService.InferGroupType(x),
                null)));
        });

        // GET /groups/{id}/detail — rich panel: properties + member counts + member
        // list (users/devices/nested), mirroring IntuneCommander's GroupDetailPanel.
        // Reverse-assignments (Intune objects targeting this group) are a separate,
        // slower scan and intentionally not bundled here.
        app.MapGet("/groups/{id}/detail", async (string id, AuthSession auth, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var grp = await g.Groups[id].GetAsync(cancellationToken: ct);
            if (grp is null) return Results.NotFound();
            var svc = new GroupService(g);
            var counts = await svc.GetMemberCountsAsync(id, ct);
            var members = await svc.ListGroupMembersAsync(id, ct);
            var dto = new GroupDetailDto(
                grp.Id ?? id,
                grp.DisplayName ?? "(unnamed)",
                grp.Description,
                GroupService.InferGroupType(grp),
                grp.SecurityEnabled ?? false,
                grp.MailEnabled ?? false,
                grp.Mail,
                grp.MembershipRule,
                grp.MembershipRuleProcessingState,
                grp.CreatedDateTime?.ToString("o"),
                new GroupMemberCountsDto(counts.Users, counts.Devices, counts.NestedGroups, counts.Total),
                members.Select(m => new GroupMemberDto(
                    m.MemberType, m.DisplayName, m.SecondaryInfo, m.TertiaryInfo, m.Status, m.Id)).ToList());
            return Results.Ok(dto);
        });
    }
}
