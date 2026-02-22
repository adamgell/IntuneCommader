namespace Intune.Commander.Core.Models;

/// <summary>
/// A single row in an assignment report. Covers all report modes via optional fields.
/// Pre-computed string properties for direct DataGrid binding.
/// </summary>
public record AssignmentReportRow
{
    public string PolicyId { get; init; } = "";

    /// <summary>Display name of the policy / app / script.</summary>
    public string PolicyName { get; init; } = "";

    /// <summary>Human-readable policy type (e.g. "Device Configuration", "Compliance Policy").</summary>
    public string PolicyType { get; init; } = "";

    /// <summary>Inferred platform (e.g. "Windows", "iOS", "macOS").</summary>
    public string Platform { get; init; } = "";

    /// <summary>Comma-separated assignment summary for overview / user / group / device modes.</summary>
    public string AssignmentSummary { get; init; } = "";

    /// <summary>Assignment reason for entity-specific checks: "All Users", "Group Assignment", "Excluded", etc.</summary>
    public string AssignmentReason { get; init; } = "";

    // ── Empty-group mode ──
    public string GroupId { get; init; } = "";
    public string GroupName { get; init; } = "";

    // ── Compare-groups mode ──
    public string Group1Status { get; init; } = "";
    public string Group2Status { get; init; } = "";

    // ── Failed-assignments mode ──
    public string TargetDevice { get; init; } = "";
    public string UserPrincipalName { get; init; } = "";
    public string Status { get; init; } = "";
    public string LastReported { get; init; } = "";
}
