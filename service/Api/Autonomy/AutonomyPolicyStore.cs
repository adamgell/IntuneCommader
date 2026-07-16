using System.Text.Json;
using CmProjectX.Store;

namespace CmProjectX.Api;

// M18 — read/write the per-tenant autonomy policy. The policy is OPERATIONAL state
// (like the sync watermarks), not history, so it lives in the mutable sync_state
// table keyed by tenant, serialized as the AutonomyPolicyDto wire JSON. When none is
// saved we return the safe defaults: drift ON (the corrective body is provably the
// prior approved state), posture-regression OPT-IN, advisory OFF (M18 open-question
// #1 lean). LOCKED: nothing here auto-applies — every toggle is enqueue-eligibility.
internal static class AutonomyPolicyStore
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static string Key(string? tenantId) => $"autonomy_policy:{tenantId ?? "default"}";

    public static AutonomyPolicyDto Defaults(string? tenantId) => new(
        TenantId: tenantId,
        Enabled: true,
        CadenceMinutes: 15,
        Scope: new AutonomyScopeDto(
            ObjectTypes: new[] { "DeviceCompliancePolicy", "SettingsCatalogPolicy" },
            AssignmentGroupAllowlist: Array.Empty<string>()),
        Signals: new AutonomySignalsDto(
            Drift: new AutonomySignalConfigDto(Enabled: true, MinSeverity: "Medium", MinScoreDrop: null),
            PostureRegression: new AutonomySignalConfigDto(Enabled: false, MinSeverity: null, MinScoreDrop: 3),
            Advisory: new AutonomySignalConfigDto(Enabled: false, MinSeverity: "High", MinScoreDrop: null)),
        Throttle: new AutonomyThrottleDto(MaxProposalsPerRun: 5, MaxInflightPending: 20),
        Note: "Autonomy may only ENQUEUE proposals. No value here auto-applies a write.");

    public static async Task<AutonomyPolicyDto> GetAsync(ISnapshotStore store, string? tenantId, CancellationToken ct = default)
    {
        var raw = await store.GetSyncStateAsync(Key(tenantId), ct);
        if (string.IsNullOrWhiteSpace(raw)) return Defaults(tenantId);
        try
        {
            var dto = JsonSerializer.Deserialize<AutonomyPolicyDto>(raw, Web);
            // Normalize on read too: an old/partial stored blob must never hand the loop a
            // null scope/signals or a zero cadence (which would spin the scheduler).
            return dto is null ? Defaults(tenantId) : Normalize(dto, tenantId);
        }
        catch { return Defaults(tenantId); }
    }

    public static async Task<AutonomyPolicyDto> SetAsync(
        ISnapshotStore store, string? tenantId, AutonomyPolicyDto policy, CancellationToken ct = default)
    {
        // Normalize at the write boundary: PUT /autonomy/policy is unvalidated minimal-API
        // model-binding, so clamp/coalesce before persisting and return the effective form.
        var stamped = Normalize(policy, tenantId);
        await store.SetSyncStateAsync(Key(tenantId), JsonSerializer.Serialize(stamped, Web), ct);
        return stamped;
    }

    // Clamp/coalesce a policy to a safe shape: tenant stamped, cadence floored at 1 minute
    // (a 0/negative cadence would spin the scheduler's Task.Delay), non-negative throttle,
    // and no null sub-objects (a partial PUT body deserializes those to null, which would
    // NRE the engine). Defaults backfill any missing section.
    internal static AutonomyPolicyDto Normalize(AutonomyPolicyDto p, string? tenantId)
    {
        var d = Defaults(tenantId);
        var scope = p.Scope is null
            ? d.Scope
            : new AutonomyScopeDto(
                ObjectTypes: p.Scope.ObjectTypes ?? Array.Empty<string>(),
                AssignmentGroupAllowlist: p.Scope.AssignmentGroupAllowlist ?? Array.Empty<string>());
        var signals = p.Signals is null
            ? d.Signals
            : new AutonomySignalsDto(
                Drift: p.Signals.Drift ?? d.Signals.Drift,
                PostureRegression: p.Signals.PostureRegression ?? d.Signals.PostureRegression,
                Advisory: p.Signals.Advisory ?? d.Signals.Advisory);
        var throttle = p.Throttle is null
            ? d.Throttle
            : new AutonomyThrottleDto(
                MaxProposalsPerRun: Math.Max(0, p.Throttle.MaxProposalsPerRun),
                MaxInflightPending: Math.Max(0, p.Throttle.MaxInflightPending));
        return p with
        {
            TenantId = tenantId,
            CadenceMinutes = Math.Max(1, p.CadenceMinutes),
            Scope = scope,
            Signals = signals,
            Throttle = throttle,
        };
    }
}
