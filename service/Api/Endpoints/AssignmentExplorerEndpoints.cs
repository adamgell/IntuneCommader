// AssignmentExplorer group — surfaces the IntuneAssignmentChecker report engine
// (Core AssignmentCheckerService) in every mode the desktop app offered: all
// policies, All Users / All Devices targets, unassigned, empty-group, failed, and
// a single-group lookup. Each mode scans the supported policy types and resolves
// targets (All Users / All Devices / named groups, incl. exclusions). A companion
// /assignment-explorer/report returns the same rows as a downloadable HTML or CSV
// (Core AssignmentReportExporter). 409 (Conflict) = signed out (auth.Graph null).
using Intune.Commander.Core.Models;
using Intune.Commander.Core.Services;

namespace CmProjectX.Api;

public static class AssignmentExplorerEndpoints
{
    // Cap the projected list so a very large tenant can't flood the client; the
    // report download is uncapped.
    private const int MaxRows = 500;

    public static void MapAssignmentExplorer(this Microsoft.AspNetCore.Builder.WebApplication app)
    {
        // ─── assignment-explorer — read-only report, mode-selectable ────────────
        app.MapGet("/assignment-explorer",
            async (AuthSession auth, string? mode, string? group, string? groupName, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            List<AssignmentReportRow> rows;
            try { rows = await RunModeAsync(new AssignmentCheckerService(g), mode, group, groupName, ct); }
            catch (Exception ex) { return ApiResults.BadRequest(ex.Message); }

            var items = rows.Take(MaxRows).Select((r, i) => Project(r, i)).ToList();
            if (rows.Count > MaxRows)
                items.Add(new ListItemDto(
                    "assignment-explorer-cap",
                    $"… {rows.Count - MaxRows} more row(s) hidden",
                    $"Showing first {MaxRows} of {rows.Count} rows — export the report for the full set",
                    "capped"));
            return Results.Ok(items);
        });

        // ─── assignment-explorer/report — the same rows as HTML or CSV download ──
        app.MapGet("/assignment-explorer/report",
            async (AuthSession auth, string? mode, string? group, string? groupName, string? format, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            List<AssignmentReportRow> rows;
            try { rows = await RunModeAsync(new AssignmentCheckerService(g), mode, group, groupName, ct); }
            catch (Exception ex) { return ApiResults.BadRequest(ex.Message); }

            var m = string.IsNullOrWhiteSpace(mode) ? "all" : mode;
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
                return Results.File(
                    System.Text.Encoding.UTF8.GetBytes(AssignmentReportExporter.GenerateCsv(m, rows)),
                    "text/csv", $"assignments-{m}-{stamp}.csv");
            return Results.File(
                System.Text.Encoding.UTF8.GetBytes(AssignmentReportExporter.GenerateHtml(m, rows)),
                "text/html", $"assignments-{m}-{stamp}.html");
        });
    }

    private static async Task<List<AssignmentReportRow>> RunModeAsync(
        AssignmentCheckerService checker, string? mode, string? group, string? groupName, CancellationToken ct) =>
        (mode ?? "all") switch
        {
            "all-users" => await checker.GetAllUsersAssignmentsAsync(cancellationToken: ct),
            "all-devices" => await checker.GetAllDevicesAssignmentsAsync(cancellationToken: ct),
            "unassigned" => await checker.GetUnassignedPoliciesAsync(cancellationToken: ct),
            "empty-groups" => await checker.GetEmptyGroupAssignmentsAsync(cancellationToken: ct),
            "failed" => await checker.GetFailedAssignmentsAsync(cancellationToken: ct),
            "group" => string.IsNullOrWhiteSpace(group)
                ? throw new InvalidOperationException("a group id is required for the group lookup")
                : await checker.GetGroupAssignmentsAsync(
                    group, string.IsNullOrWhiteSpace(groupName) ? group : groupName, cancellationToken: ct),
            _ => await checker.GetAllPoliciesWithAssignmentsAsync(cancellationToken: ct),
        };

    // AssignmentReportRow → the normalized list row. Title = policy; subtitle joins
    // the type/platform with the mode-relevant detail (target summary, group, device,
    // reason); badge = deployment status if present else the assignment reason.
    private static ListItemDto Project(AssignmentReportRow r, int i)
    {
        var id = string.IsNullOrEmpty(r.PolicyId) ? $"row-{i}" : $"{r.PolicyId}#{i}";
        var title = string.IsNullOrWhiteSpace(r.PolicyName) ? "(unnamed)" : r.PolicyName;
        var detail = new[]
        {
            NullIfBlank(r.PolicyType),
            NullIfBlank(r.Platform),
            NullIfBlank(r.AssignmentSummary),
            NullIfBlank(r.GroupName),
            NullIfBlank(r.TargetDevice),
            NullIfBlank(r.UserPrincipalName),
            NullIfBlank(r.AssignmentReason),
        }.Where(s => s is not null);
        var badge = !string.IsNullOrWhiteSpace(r.Status) ? r.Status
            : !string.IsNullOrWhiteSpace(r.AssignmentReason) ? r.AssignmentReason
            : "assigned";
        return new ListItemDto(id, title, string.Join(" · ", detail), badge);
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
