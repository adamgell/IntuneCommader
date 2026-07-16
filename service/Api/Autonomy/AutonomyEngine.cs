using System.Text.Json;
using CmProjectX.Store;
using CmProjectX.Sync;
using Microsoft.Extensions.Logging;
// GraphServiceClient is fully-qualified at every use; no `using Microsoft.Graph.Beta`
// (it would shadow IResult / break Minimal API overload resolution elsewhere).

namespace CmProjectX.Api;

// M18 Autonomy — the closed-loop AI SRE orchestration: watch → detect → plan →
// simulate → propose → (human gate) → … verify → audit. ONE tick per call to
// RunTickAsync; the AutonomyScheduler (service/Sync) drives the cadence.
//
// LOCKED: this engine NEVER applies a write. Its terminal action is POST
// /pending-changes (state=pending) — byte-identical to an MCP propose_* tool. The
// only code that replays an approved write is /pending-changes/{id}/approve, which a
// human must trigger. Everything destructive (simulate, snapshot, audit, apply) is
// reuse; the engine is the orchestration + the planner glue.
//
// It calls its OWN HTTP contract over loopback (the same seam the MCP tools + the
// approval-apply path use), so every per-surface Graph quirk is reused unchanged.
internal sealed class AutonomyEngine
{
    private readonly ISnapshotStore _store;
    private readonly GraphDeltaSync _sync;
    private readonly AuthSession _auth;
    private readonly ILogger _log;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public AutonomyEngine(ISnapshotStore store, GraphDeltaSync sync, AuthSession auth, ILogger log)
    {
        _store = store; _sync = sync; _auth = auth; _log = log;
    }

    private static string LoopWatermarkKey(string tenantId) => $"loop_watermark:{tenantId}";
    private static string PostureWatermarkKey(string tenantId) => $"posture_watermark:{tenantId}";

    // One full loop tick. No-op (returns null) when signed out or autonomy is disabled.
    // Best-effort throughout: a failure in one step degrades that step, never throws out
    // of the scheduler (a thrown tick would kill the BackgroundService).
    public async Task<AutonomyRunDto?> RunTickAsync(CancellationToken ct = default)
    {
        var graph = _auth.Graph;
        var tenantId = _auth.ActiveProfile?.TenantId;
        if (graph is null || tenantId is null) return null; // not signed in — nothing to watch

        var policy = await AutonomyPolicyStore.GetAsync(_store, tenantId, ct);
        if (!policy.Enabled) return null;

        var runId = $"run_{DateTime.UtcNow:yyyy-MM-ddTHH-mm-ssZ}_{Short(tenantId)}";
        var startedUtc = DateTime.UtcNow;

        // ── ① WATCH — delta-sync audit + config snapshots (reuse GraphDeltaSync) ──
        var watched = new AutonomyWatchedDto(0, 0, policy.Scope.ObjectTypes);
        try
        {
            var result = await _sync.RunAsync(graph, tenantId, ct);
            _auth.LastSyncUtc = DateTime.UtcNow;
            watched = new AutonomyWatchedDto(result.AuditEvents, result.Snapshots, policy.Scope.ObjectTypes);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Autonomy watch (sync) failed; continuing on stored snapshots"); }

        // ── ⑧ VERIFY — re-check any prior approved-and-applied run for convergence.
        // Done first so the verify of a *previous* tick's remediation lands on the head
        // snapshot this tick just synced (Graph eventual-consistency permitting).
        await VerifyOpenRunsAsync(tenantId, ct);

        // ── ② DETECT — drift = an object whose newest snapshot diverged from its
        // previous one, captured since the loop watermark (so we don't re-detect old
        // drift every tick). minSeverity from the policy floors the signal.
        var since = ParseUtc(await _store.GetSyncStateAsync(LoopWatermarkKey(tenantId), ct));
        var minSeverity = policy.Signals.Drift.Enabled ? (policy.Signals.Drift.MinSeverity ?? "Low") : null;
        var driftObjects = minSeverity is null ? new List<DetectedDrift>() : await DetectDriftAsync(policy, since, tenantId, ct);

        var detected = new List<AutonomyDetectionDto>();
        var proposed = new List<AutonomyProposalDto>();

        // Inflight cap: how many pending proposals already sit unactioned.
        var inflight = (await _store.GetPendingChangesAsync("pending", 500, ct))
            .Count(p => p.TenantId is null || string.Equals(p.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));

        foreach (var d in driftObjects)
        {
            if (!SeverityMeets(d.Severity, minSeverity!)) continue;

            detected.Add(new AutonomyDetectionDto(
                Signal: "drift", ObjectId: d.ObjectId, ObjectName: d.ObjectName, Severity: d.Severity.ToLowerInvariant(),
                BaseSnapshotId: d.BaseSnapshotId, HeadSnapshotId: d.HeadSnapshotId, Changes: d.Changes));

            // ── ③/④/⑤ PLAN → SIMULATE → PROPOSE ──────────────────────────────
            if (proposed.Count >= policy.Throttle.MaxProposalsPerRun) continue;
            if (inflight >= policy.Throttle.MaxInflightPending) continue;
            // Coalesce: don't re-enqueue while a pending proposal for this object exists.
            if (await HasInflightForObjectAsync(d.ObjectId, tenantId, ct)) continue;

            var proposal = await PlanAndEnqueueAsync(d, ct);
            if (proposal is not null) { proposed.Add(proposal); inflight++; }
        }

        // ── ② DETECT (posture + advisory) — both read the current benchmark posture once.
        BenchmarkedPostureDto? posture = null;
        if (policy.Signals.PostureRegression.Enabled || policy.Signals.Advisory.Enabled)
            posture = await FetchPostureAsync(ct);

        // Posture: a benchmark-score regression since the last tick. Detection-only — a score
        // drop has no single provably-safe corrective write (the drift signal restores the
        // config that caused it), so we surface the regression without auto-proposing, within
        // the locked "propose only what's provably safe" guarantee.
        if (policy.Signals.PostureRegression.Enabled)
        {
            var current = posture?.Score;
            var prev = ParseIntOrNull(await _store.GetSyncStateAsync(PostureWatermarkKey(tenantId), ct));
            if (current is not null)
                await _store.SetSyncStateAsync(PostureWatermarkKey(tenantId), current.Value.ToString(), ct);
            if (EvaluatePostureRegression(prev, current, policy.Signals.PostureRegression.MinScoreDrop) is { } pd)
                detected.Add(pd);
        }

        // Advisory: match the curated advisory catalog (embedded fixture) against the tenant's
        // current posture gaps. Each match surfaces a detection; an advisory that carries a
        // REMEDIATION is also simulated + PROPOSED into the M13 inbox (proposer=autonomy:advisory)
        // — still human-approved, never auto-applied, and throttle/coalesce-guarded like drift.
        // Advisories with no remediation (e.g. read-only Conditional Access) stay detection-only.
        if (policy.Signals.Advisory.Enabled && posture is not null)
        {
            foreach (var m in AdvisoryCatalog.Match(posture.Gaps, policy.Signals.Advisory.MinSeverity))
            {
                detected.Add(m.Detection);
                if (m.Advisory.Remediation is { } rem
                    && proposed.Count < policy.Throttle.MaxProposalsPerRun
                    && inflight < policy.Throttle.MaxInflightPending
                    && !await HasInflightAdvisoryAsync(m.Advisory.Id, tenantId, ct))
                {
                    var proposal = await PlanAndEnqueueAdvisoryAsync(m.Advisory, rem, ct);
                    if (proposal is not null) { proposed.Add(proposal); inflight++; }
                }
            }
        }

        // Advance the loop watermark to now: every snapshot captured this tick has been
        // evaluated, so the next tick only looks at what changes after this point.
        await _store.SetSyncStateAsync(LoopWatermarkKey(tenantId), DateTime.UtcNow.ToString("o"), ct);

        var run = new AutonomyRunDto(
            RunId: runId, TenantId: tenantId,
            StartedUtc: startedUtc.ToString("o"), FinishedUtc: DateTime.UtcNow.ToString("o"),
            Watched: watched, Detected: detected, Proposed: proposed,
            Decision: null, Verify: null);

        await _store.UpsertAutonomyRunAsync(
            new AutonomyRunRecord(runId, tenantId, startedUtc, JsonSerializer.Serialize(run, Web)), ct);

        if (detected.Count > 0 || proposed.Count > 0)
            _log.LogInformation(
                "Autonomy tick {RunId}: {Detected} signal(s), {Proposed} proposal(s) enqueued (nothing auto-applied).",
                runId, detected.Count, proposed.Count);
        return run;
    }

    // ── ② DETECT ────────────────────────────────────────────────────────────────

    private sealed record DetectedDrift(
        string ObjectId, string? ObjectName, string Severity,
        string? BaseSnapshotId, string? HeadSnapshotId,
        IReadOnlyList<DriftChangeDto> Changes, string BaseBodyJson, string Path);

    private async Task<List<DetectedDrift>> DetectDriftAsync(
        AutonomyPolicyDto policy, DateTime since, string tenantId, CancellationToken ct)
    {
        var found = new List<DetectedDrift>();
        // Null-safe: a malformed PUT could deserialize ObjectTypes to null (the policy
        // store normalizes, but don't assume). Empty scope ⇒ all watched types pass.
        var scope = (policy.Scope.ObjectTypes ?? (IReadOnlyList<string>)Array.Empty<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // GetSnapshottedObjectsAsync returns each object's two newest bodies, so we can
        // diff in-memory (the SAME path /objects + /drift use) with no per-object query.
        var objects = await _store.GetSnapshottedObjectsAsync(500, ct, tenantId);
        foreach (var o in objects)
        {
            if (scope.Count > 0 && !scope.Contains(o.ObjectType)) continue;
            if (o.SnapshotCount < 2 || o.PreviousBodyJson is null || o.LatestBodyJson is null) continue;
            if (o.LastCapturedUtc <= since) continue; // already evaluated in a prior tick

            // Map the snapshot object type to a writable surface path; drift on a
            // read-only surface (e.g. MobileApp) can't be auto-remediated, so skip it.
            if (PathFor(o.ObjectType) is not { } path) continue;

            // (older, newer) — matches /drift; the corrective restore is the older body.
            var changes = JsonDrift.Diff(o.PreviousBodyJson, o.LatestBodyJson);
            if (changes.Count == 0) continue;

            // Resolve the two snapshot ids for the run's audit chain (best-effort).
            var history = await _store.GetSnapshotsForObjectAsync(o.ObjectId, 2, ct, tenantId);
            var headId = history.Count > 0 ? history[0].SnapshotId : null;
            var baseId = history.Count > 1 ? history[1].SnapshotId : null;

            found.Add(new DetectedDrift(
                ObjectId: o.ObjectId, ObjectName: o.ObjectName,
                Severity: ClassifySeverity(changes),
                BaseSnapshotId: baseId, HeadSnapshotId: headId,
                Changes: changes, BaseBodyJson: o.PreviousBodyJson, Path: path));
        }
        return found;
    }

    // ── ② DETECT (posture) ────────────────────────────────────────────────────────

    // The posture-regression decision (pure, hermetically tested). A detection iff we have a
    // prior score AND the prev→current drop meets the policy floor (default 3). The first
    // observation only establishes the baseline (prev null ⇒ no detection). Detection-only:
    // a score drop has no single safe mechanical remediation, so the loop never fabricates a
    // corrective write for it — the drift signal restores the config that caused the drop.
    internal static AutonomyDetectionDto? EvaluatePostureRegression(int? prev, int? current, int? minScoreDrop)
    {
        if (prev is null || current is null) return null;
        var drop = prev.Value - current.Value;
        var floor = Math.Max(1, minScoreDrop ?? 3);
        if (drop < floor) return null;
        var severity = drop >= 15 ? "critical" : drop >= 10 ? "high" : drop >= 5 ? "medium" : "low";
        return new AutonomyDetectionDto(
            Signal: "posture", ObjectId: "posture:score",
            ObjectName: $"Security posture score dropped {prev} → {current} (−{drop})",
            Severity: severity, BaseSnapshotId: null, HeadSnapshotId: null,
            Changes: new[] { new DriftChangeDto("posture.score", "Modified", prev, current) });
    }

    // Current benchmark-mapped posture score over loopback (the same seam PLAN uses for
    // /simulate). Best-effort: any failure yields null ⇒ the posture signal skips this tick.
    private async Task<BenchmarkedPostureDto?> FetchPostureAsync(CancellationToken ct)
    {
        try
        {
            var resp = await Loopback.Http.GetAsync("/posture/score", ct);
            if (!resp.IsSuccessStatusCode) return null;
            return JsonSerializer.Deserialize<BenchmarkedPostureDto>(await resp.Content.ReadAsStringAsync(ct), Web);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Autonomy posture fetch failed; skipping the posture/advisory signals this tick");
            return null;
        }
    }

    // ── ③/④/⑤ PLAN → SIMULATE → PROPOSE ──────────────────────────────────────────

    private async Task<AutonomyProposalDto?> PlanAndEnqueueAsync(DetectedDrift d, CancellationToken ct)
    {
        // PLAN: the corrective write restores the BASE snapshot body — the inverse of
        // the drift, provably the prior approved state (M18 open-question #1: drift is
        // the safest signal to auto-enqueue precisely because of this).
        var body = d.BaseBodyJson;

        // SIMULATE (M16): attach a blast-radius so the inbox shows impact + diff. The
        // restore re-targets the same object, so the blast radius is typically benign;
        // best-effort — a failed sim still enqueues (the operator just sees no impact card).
        BlastRadiusReportDto? blast = null;
        string? simJson = null;
        try
        {
            var simReq = JsonSerializer.Serialize(new { proposer = "autonomy:drift", verb = "update", path = d.Path, objectId = d.ObjectId, bodyJson = body });
            var simResp = await Loopback.Http.PostAsync("/simulate",
                new StringContent(simReq, System.Text.Encoding.UTF8, "application/json"), ct);
            if (simResp.IsSuccessStatusCode)
            {
                simJson = await simResp.Content.ReadAsStringAsync(ct);
                blast = JsonSerializer.Deserialize<BlastRadiusReportDto>(simJson, Web);
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "Autonomy simulate failed for {ObjectId}; enqueueing without blast-radius", d.ObjectId); }

        // PROPOSE: the EXISTING M13 enqueue. proposer="autonomy:drift" is the only tell
        // that this came from the loop and not a human MCP client — everything else
        // (diff, blast-radius, the approval/replay path) is unchanged.
        var diffJson = JsonSerializer.Serialize(d.Changes, Web);
        var payload = JsonSerializer.Serialize(new
        {
            proposer = "autonomy:drift",
            kind = "update",
            path = d.Path,
            objectId = d.ObjectId,
            objectName = d.ObjectName,
            bodyJson = body,
            diffJson,
            simulationJson = simJson,
        });

        try
        {
            var resp = await Loopback.Http.PostAsync("/pending-changes",
                new StringContent(payload, System.Text.Encoding.UTF8, "application/json"), ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("Autonomy enqueue failed ({Status}) for {ObjectId}", (int)resp.StatusCode, d.ObjectId);
                return null;
            }
            var changeId = ChangeIdFrom(await resp.Content.ReadAsStringAsync(ct));
            if (changeId == "(unknown)")
                _log.LogWarning("Autonomy enqueue for {ObjectId} returned an unparseable change id; verify-tracking will be orphaned for this run", d.ObjectId);
            return new AutonomyProposalDto(changeId, "update", d.Path, blast);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Autonomy enqueue threw for {ObjectId}", d.ObjectId);
            return null;
        }
    }

    // Advisory PLAN → SIMULATE → PROPOSE (M18): enqueue an advisory's bundled corrective write
    // into the M13 inbox (proposer="autonomy:advisory") with a blast-radius from /simulate. The
    // advisory id rides in the objectName so HasInflightAdvisoryAsync can coalesce (an advisory
    // create has no objectId to key on). Human still approves the exact diff — nothing auto-applies.
    private async Task<AutonomyProposalDto?> PlanAndEnqueueAdvisoryAsync(
        AdvisoryCatalog.Advisory advisory, AdvisoryCatalog.AdvisoryRemediation rem, CancellationToken ct)
    {
        var objectName = $"{advisory.Title} [{advisory.Id}]";
        BlastRadiusReportDto? blast = null;
        string? simJson = null;
        try
        {
            var simReq = JsonSerializer.Serialize(new { proposer = "autonomy:advisory", verb = rem.Verb, path = rem.Path, objectId = rem.ObjectId, bodyJson = rem.BodyJson });
            var simResp = await Loopback.Http.PostAsync("/simulate",
                new StringContent(simReq, System.Text.Encoding.UTF8, "application/json"), ct);
            if (simResp.IsSuccessStatusCode)
            {
                simJson = await simResp.Content.ReadAsStringAsync(ct);
                blast = JsonSerializer.Deserialize<BlastRadiusReportDto>(simJson, Web);
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "Autonomy advisory simulate failed for {AdvisoryId}", advisory.Id); }

        var payload = JsonSerializer.Serialize(new
        {
            proposer = "autonomy:advisory",
            kind = rem.Verb,
            path = rem.Path,
            objectId = rem.ObjectId,
            objectName,
            bodyJson = rem.BodyJson,
            diffJson = (string?)null,
            simulationJson = simJson,
        });
        try
        {
            var resp = await Loopback.Http.PostAsync("/pending-changes",
                new StringContent(payload, System.Text.Encoding.UTF8, "application/json"), ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("Autonomy advisory enqueue failed ({Status}) for {AdvisoryId}", (int)resp.StatusCode, advisory.Id);
                return null;
            }
            var changeId = ChangeIdFrom(await resp.Content.ReadAsStringAsync(ct));
            return new AutonomyProposalDto(changeId, rem.Verb, rem.Path, blast);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Autonomy advisory enqueue threw for {AdvisoryId}", advisory.Id);
            return null;
        }
    }

    // Coalesce advisory proposals by advisory id (embedded in the objectName) — don't re-enqueue
    // while a pending proposal for the same advisory already sits in the inbox.
    private async Task<bool> HasInflightAdvisoryAsync(string advisoryId, string tenantId, CancellationToken ct)
    {
        var pending = await _store.GetPendingChangesAsync("pending", 500, ct);
        return pending.Any(p =>
            (p.TenantId is null || string.Equals(p.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(p.Proposer, "autonomy:advisory", StringComparison.OrdinalIgnoreCase) &&
            (p.ObjectName?.Contains(advisoryId, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    // ── ⑧ VERIFY ─────────────────────────────────────────────────────────────────

    // For every run that raised a proposal which has since been APPLIED, re-diff the
    // object's current head against the base the drift was measured from. residual == 0
    // ⇒ converged. The decision + verify are folded back into the SAME run row.
    private async Task VerifyOpenRunsAsync(string tenantId, CancellationToken ct)
    {
        var runs = await _store.GetAutonomyRunsAsync(50, ct);
        foreach (var rec in runs)
        {
            if (rec.TenantId != tenantId) continue;
            AutonomyRunDto? run;
            try { run = JsonSerializer.Deserialize<AutonomyRunDto>(rec.RunJson, Web); }
            catch (Exception ex) { _log.LogWarning(ex, "Autonomy verify: skipping unreadable run record {RunId}", rec.RunId); continue; }
            if (run is null || run.Proposed.Count == 0) continue;
            if (run.Verify is { Converged: true }) continue; // already converged — done

            var proposal = run.Proposed[0];
            var change = await _store.GetPendingChangeAsync(proposal.PendingChangeId, ct);
            if (change is null) continue;
            if (change.State == "pending") continue; // still inert in the inbox — nothing to fold in yet

            // ── ⑥ DECISION — mirrors what /pending-changes/{id}/approve|reject wrote, and
            // links to the audit row that recorded it. M13's approve/reject writes an audit
            // event whose actor is "{proposer} (approved|rejected by operator)"; the human
            // identity isn't captured per-change, so Operator carries that actor string and
            // AuditEventId points at the row so the run joins the audit chain.
            var (op, auditId) = await FindDecisionAuditAsync(change.State, change.ObjectId, change.DecidedUtc, change.Id, ct);
            var decision = new AutonomyDecisionDto(
                PendingChangeId: change.Id,
                Action: change.State switch
                {
                    "applied" => "approved",
                    "rejected" => "rejected",
                    "failed" => "failed",
                    _ => "pending",
                },
                Operator: op,
                DecidedUtc: change.DecidedUtc?.ToString("o"),
                AppliedObjectId: change.State == "applied" ? change.ObjectId : null,
                AuditEventId: auditId);

            // ── ⑧ VERIFY — only meaningful once the corrective write actually applied.
            AutonomyVerifyDto? verify = run.Verify;
            if (change.State == "applied" && change.ObjectId is { } objId && change.BodyJson is { } baseBody)
            {
                // Residual drift: diff the corrective base body against the NEW head
                // snapshot — the SAME compare /drift does. Zero ⇒ re-converged.
                var history = await _store.GetSnapshotsForObjectAsync(objId, 1, ct, tenantId);
                var head = history.Count > 0 ? history[0] : null;
                if (head is not null)
                {
                    var residual = JsonDrift.Diff(baseBody, head.BodyJson);
                    verify = new AutonomyVerifyDto(
                        VerifiedUtc: DateTime.UtcNow.ToString("o"),
                        ResidualDriftChanges: residual.Count,
                        Converged: residual.Count == 0,
                        VerifySnapshotId: head.SnapshotId);
                }
            }

            // Only re-write the row if the decision or verify actually advanced. Use
            // record VALUE equality (verify != run.Verify) — ReferenceEquals would be
            // true for every freshly-constructed AutonomyVerifyDto and rewrite each tick.
            if (DecisionAdvanced(run.Decision, decision) || verify != run.Verify)
            {
                var updated = run with { Decision = decision, Verify = verify };
                await _store.UpsertAutonomyRunAsync(
                    new AutonomyRunRecord(rec.RunId, rec.TenantId, rec.StartedUtc, JsonSerializer.Serialize(updated, Web)), ct);
            }
        }
    }

    private static bool DecisionAdvanced(AutonomyDecisionDto? a, AutonomyDecisionDto b) =>
        a is null || a.Action != b.Action || a.DecidedUtc != b.DecidedUtc;

    // Locate the audit row M13's approve/reject wrote for a decided change, to fill the
    // decision's Operator (the audit actor, e.g. "autonomy:drift (approved by operator)")
    // and AuditEventId. Only applied/rejected changes have an audit row (a failed replay
    // never reached the AppendAuditEvent). Best-effort: (null, null) when nothing matches.
    private async Task<(string? Operator, string? AuditEventId)> FindDecisionAuditAsync(
        string state, string? objectId, DateTime? decidedUtc, string changeId, CancellationToken ct)
    {
        if (state is not ("applied" or "rejected") || objectId is not { } oid) return (null, null);
        try
        {
            var at = decidedUtc ?? DateTime.UtcNow;
            var events = await _store.QueryAuditAsync(at.AddMinutes(-5), at.AddMinutes(5), oid, 50, ct);
            var match = events.FirstOrDefault(e =>
                string.Equals(e.ObjectId, oid, StringComparison.OrdinalIgnoreCase) &&
                (e.Actor?.Contains("by operator", StringComparison.OrdinalIgnoreCase) ?? false));
            return match is null ? (null, null) : (match.Actor, match.Id);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Autonomy verify: audit lookup failed for change {ChangeId}", changeId);
            return (null, null);
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private static int? ParseIntOrNull(string? s) => int.TryParse(s, out var v) ? v : null;

    private async Task<bool> HasInflightForObjectAsync(string objectId, string tenantId, CancellationToken ct)
    {
        var pending = await _store.GetPendingChangesAsync("pending", 500, ct);
        return pending.Any(p =>
            (p.TenantId is null || string.Equals(p.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(p.ObjectId, objectId, StringComparison.OrdinalIgnoreCase));
    }

    // Snapshot object type → writable surface path. Only the surfaces the loop can
    // actually re-converge via an update; everything else returns null (skipped).
    private static string? PathFor(string objectType) => objectType switch
    {
        "DeviceCompliancePolicy" => "/compliance-policies",
        "SettingsCatalogPolicy" => "/settings-catalog",
        _ => null,
    };

    // Severity of a drift change set — mirrors DriftDetectionService.ClassifyFieldChange
    // (password/mfa/encryption/bitlocker → Critical; assignment → High;
    // displayName/description → Low; else Medium), rolled up to the max.
    private static string ClassifySeverity(IReadOnlyList<DriftChangeDto> changes)
    {
        var max = 0; // 0=Low 1=Medium 2=High 3=Critical
        foreach (var c in changes)
        {
            var p = c.Path.ToLowerInvariant();
            int s;
            if (p.Contains("password") || p.Contains("mfa") || p.Contains("encryption") || p.Contains("bitlocker")) s = 3;
            else if (p.Contains("assignment")) s = 2;
            else if (p.Contains("displayname") || p.Contains("description")) s = 0;
            else s = 1;
            if (s > max) max = s;
        }
        return max switch { 3 => "Critical", 2 => "High", 1 => "Medium", _ => "Low" };
    }

    private static readonly string[] SeverityOrder = ["info", "low", "medium", "high", "critical"];

    // Does an observed severity meet the policy floor? (case-insensitive rank compare.)
    private static bool SeverityMeets(string observed, string floor)
    {
        int Rank(string s) => Math.Max(0, Array.IndexOf(SeverityOrder, s.ToLowerInvariant()));
        return Rank(observed) >= Rank(floor);
    }

    private static DateTime ParseUtc(string? iso) =>
        DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
            ? dt.ToUniversalTime() : DateTime.MinValue;

    private static string Short(string id) => id.Length <= 4 ? id : id[..4];

    private static string ChangeIdFrom(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            if (d.RootElement.TryGetProperty("id", out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? "(unknown)";
        }
        catch { /* fall through */ }
        return "(unknown)";
    }
}
