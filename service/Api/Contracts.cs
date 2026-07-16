using System.Text.Json;

namespace CmProjectX.Api;

// API DTOs mirroring contract/openapi.yaml. Minimal API serializes these with
// System.Text.Json Web defaults (camelCase), matching the Rust api-types crate.

// ─── Uniform error envelope (contract: Error) ─────────────────────────────────
// The ONE shape EVERY non-2xx response carries: unhandled exceptions (500, via
// UseExceptionHandler), bare framework status codes (via UseStatusCodePages), and
// endpoint-level validation/conflict errors (via ApiResults). `Error` is the short
// human message the thin Rust client reads verbatim (app/src/api_client.rs
// body_error); `Detail` is optional extra context; `Status` echoes the HTTP status
// code; `TraceId` ties the body to the server-side Activity when telemetry is on.
// Mirrored byte-for-byte in crates/api-types/src/lib.rs (ApiError) and
// contract/openapi.yaml (Error). See ErrorEnvelope.cs for the producers.
public sealed record ErrorDto(
    string Error,
    string? Detail,
    int Status,
    string? TraceId = null);

// AwaitingInteractive: the sidecar opened the system browser for an interactive
// (delegated) sign-in and is waiting for the user to finish — distinct from the
// generic SigningIn so the client can say "continue in your browser".
public enum AuthState { SignedOut, AwaitingDeviceCode, AwaitingInteractive, SigningIn, SignedIn, Failed }

public sealed record SyncStatusDto(
    bool Healthy,
    string AuthState,
    string? TenantId,
    string? ProfileName,
    string? LastSyncUtc,
    string? Cloud,
    DeviceCodePromptDto? DeviceCode,
    string? Error,
    string? LastWarmedUtc = null);

public sealed record DeviceCodePromptDto(
    string UserCode,
    string VerificationUri,
    string Message,
    string? ExpiresUtc);

public sealed record TenantProfileSummaryDto(
    string Id,
    string Name,
    string TenantId,
    string Cloud,
    string AuthMethod,
    bool IsActive);

// M11 profile lifecycle — create (POST /profiles) or edit (PATCH /profiles/{id}) a
// saved tenant profile. Cloud/AuthMethod are the enum names ("Commercial"/"GCC"/
// "GCCHigh"/"DoD"; "Interactive"/"ClientSecret"/"DeviceCode"). On PATCH, null
// fields keep their existing value; an empty-string secret clears it.
public sealed record CreateProfileRequest(
    string? Name,
    string? TenantId,
    string? ClientId,
    string? Cloud,
    string? AuthMethod,
    string? ClientSecret);

public sealed record AuditEventDto(
    string Id,
    string Timestamp,
    string? Actor,
    string Action,
    string ObjectType,
    string ObjectId,
    string? ObjectName);

public sealed record DriftRecordDto(
    string ObjectId,
    string? BaseSnapshotId,
    string? HeadSnapshotId,
    IReadOnlyList<DriftChangeDto> Changes);

public sealed record DriftChangeDto(
    string Path,
    string Kind,
    object? Before,
    object? After);

public sealed record SearchResultDto(
    string Kind,
    string Id,
    double? Score,
    string Summary);

public sealed record DriftObjectDto(
    string ObjectId,
    string ObjectType,
    string? ObjectName,
    int SnapshotCount,
    string LastCapturedUtc,
    int ChangeCount);

// The headless drift-gate CLI's report (stdout --format json / --output). `Objects` is the
// DriftObjectDto set filtered to ChangeCount >= MinChanges. `Mode` is "store-only" today.
public sealed record DriftReportDto(
    string GeneratedUtc,
    string Mode,
    string? TenantId,
    int TrackedObjectCount,
    int DriftedObjectCount,
    int MinChanges,
    bool DriftDetected,
    IReadOnlyList<DriftObjectDto> Objects);

// One Intune application row for the Applications list. Shape mirrors
// IntuneCommander's AppListItemDto; AppType/Platform are projected from the Graph
// MobileApp union type by ApplicationMapper.
public sealed record AppListItemDto(
    string Id,
    string DisplayName,
    string? Description,
    string? Publisher,
    string AppType,
    string Platform,
    string CreatedDateTime,
    string LastModifiedDateTime,
    bool IsAssigned,
    string PublishingState,
    bool IsFeatured);

// Normalized list row for generic LIVE list screens (Devices, Identity, …).
// Per-feature endpoints project their Graph element types into this shape so the
// client renders every list uniformly via `list_workspace`.
// `Platform`/`Modified` are optional rich-list columns (null on surfaces that don't
// project them); the client renders a multi-column grid + platform filter chips when
// present, else falls back to the title/subtitle/badge row.
public sealed record ListItemDto(
    string Id, string Title, string Subtitle, string? Badge,
    string? Platform = null, string? Modified = null, string? Source = null);

// GET /providers — the registered MDM providers (M22 cross-MDM) and the catalog
// surfaces each one honestly backs. `intune` is the implicit default provider; others
// (e.g. `jamf`) serve a subset behind the same (verb, path, body) contract.
public sealed record MdmProviderDescriptorDto(string Id, IReadOnlyList<string> SupportedSurfaces);

// ── M14 device actions (managedDevice action verbs) ──────────────────────────
// One row in the device-action catalog (GET /managed-devices/actions): the verbs the
// sidecar will dispatch. `Destructive` flags wipe/retire/fresh-start (typed-confirm +
// opt-in gated); `NeedsConfirm` mirrors it for the client's confirm dialog.
public sealed record DeviceActionInfoDto(
    string Id, string DisplayName, bool Destructive, bool NeedsConfirm);

// One per-device outcome from a bulk action (POST /managed-devices/actions/{action}).
public sealed record BulkDeviceActionResultDto(string DeviceId, bool Ok, string? Error);

// One row of a device's action HISTORY (GET /managed-devices/{id}/actions). Projects a
// Graph `deviceActionResult`: `Action` ← actionName, `State` ← actionState (done/pending/
// failed/…), `RequestedUtc` ← startDateTime, `CompletedUtc` ← lastUpdatedDateTime. `Id` is
// synthesized (actionName#index — Graph gives no per-record id). `Actor` is null: Graph's
// deviceActionResults carry no initiator; reserved for a future audit-log join.
public sealed record DeviceActionRecord(
    string Id, string Action, string? State, string? RequestedUtc, string? CompletedUtc, string? Actor);

// ── M12.1 cache-dev (Cache Sync) ─────────────────────────────────────────────
// One LIST-key blob-cache status row for the active tenant (GET /cache). DETAIL
// keys ({key}/{id}) are lazy-only and NOT enumerable, so this reflects LIST-key
// coverage only. CachedAtUtc is null when the key is missing or expired (GetMetadata
// returns null). WarmAhead = the key is in PrefetchAllToCacheAsync's warm-ahead set.
public sealed record CacheEntryStatusDto(
    string Key, string DisplayName, string? CachedAtUtc, int ItemCount, bool WarmAhead);

// Header summary for the cache-dev screen. Available is false on NullCacheService
// (cache disabled); EntryCount/TotalItems count live (non-expired) LIST keys only.
public sealed record CacheSummaryDto(
    bool Available, string? LastWarmedUtc, int EntryCount, int TotalItems);

// ── Groups detail (mirrors IntuneCommander's GroupDetailPanel) ───────────────
public sealed record GroupMemberCountsDto(int Users, int Devices, int NestedGroups, int Total);

public sealed record GroupMemberDto(
    string MemberType,      // User | Device | Group
    string DisplayName,
    string SecondaryInfo,
    string TertiaryInfo,
    string Status,
    string Id);

public sealed record GroupDetailDto(
    string Id,
    string DisplayName,
    string? Description,
    string GroupType,
    bool SecurityEnabled,
    bool MailEnabled,
    string? Mail,
    string? MembershipRule,
    string? MembershipRuleProcessingState,
    string? CreatedDateTime,
    GroupMemberCountsDto Counts,
    List<GroupMemberDto> Members);

// ─── M6 safe-write rails (diff-preview, snapshot-on-write, restore/undo) ──────

// POST /preview-diff: client posts the before/after object JSON (as strings) and
// gets back the same DriftChange list the time-machine drift timeline uses, so a
// write can be gated behind a visible change set.
public sealed record PreviewDiffRequest(string? Before, string? After);

// POST /snapshots: the client captures the object body it just wrote so each edit
// is immediately time-travelable (today snapshots also land on /sync).
public sealed record SnapshotCaptureRequest(
    string ObjectType, string ObjectId, string? ObjectName, string BodyJson);

// One row in an object's snapshot history (newest first), for the restore/undo
// picker. Carries the full body so "restore" can re-PATCH a past version.
public sealed record SnapshotSummaryDto(
    string SnapshotId, string CapturedUtc, string ObjectType, string? ObjectName, string BodyJson);

// ─── M8 bulk & lifecycle: import dry-run preview (what a backup .zip contains) ──
public sealed record ImportItemDto(string Type, string Name);
public sealed record ImportPreviewGroupDto(string Type, int Count, IReadOnlyList<string> Names);
public sealed record ImportPreviewDto(int Total, IReadOnlyList<ImportPreviewGroupDto> Groups);

// ─── M8 live restore (POST /import?dryRun=false): per-type created / failed ────
public sealed record ImportRestoreGroupDto(string Type, int Created, int Failed, IReadOnlyList<string> Errors);
public sealed record ImportRestoreResultDto(
    bool Applied, int TotalCreated, int TotalFailed, IReadOnlyList<ImportRestoreGroupDto> Groups);

// ─── Security posture score (GET /security-posture/summary) ───────────────────
// 0-100 score across five weighted categories + severity-ranked gaps + headline
// stats. Ported from IntuneCommander's SecurityPostureSummary.
public sealed record SecurityPostureDto(
    int Score,
    IReadOnlyList<ScoreCategoryDto> Breakdown,
    IReadOnlyList<SecurityGapDto> Gaps,
    PostureStatsDto Stats,
    IReadOnlyList<CaListRowDto> CaPolicies,
    IReadOnlyList<ComplianceRowDto> CompliancePolicies);

public sealed record ScoreCategoryDto(string Category, int Score, int MaxScore, IReadOnlyList<string> Items);
public sealed record SecurityGapDto(string Severity, string Category, string Description);
public sealed record PostureStatsDto(
    int CaTotal, int CaEnabled, int CaReportOnly, int CaDisabled,
    int CompliancePolicies, IReadOnlyList<string> CompliancePlatforms,
    int EndpointSecurityIntents, int AppProtectionPolicies,
    int AuthStrengthPolicies, int NamedLocations);
public sealed record CaListRowDto(string Id, string Name, string State);
public sealed record ComplianceRowDto(string Id, string Name, string Platform);

// ─── M19 continuous posture (docs/part-ii/M19-posture.md) ─────────────────────
// Extends the point-in-time posture into a benchmark-mapped, trended,
// evidence-exporting compliance product. The score weights are unchanged; these
// DTOs only graft control IDs on, trend the score, and package the evidence.

// One external control reference (CIS / OIB / Essential 8 / NIST 800-53).
public sealed record BenchmarkControlRefDto(string Framework, string Id, string? Title = null);

// Per-benchmark coverage rollup: satisfied controls over controls in scope.
public sealed record BenchmarkCoverageDto(int ControlsCovered, int ControlsTotal, int Percent);

// A ScoreCategory / SecurityGap with control references grafted on.
public sealed record BenchmarkedScoreCategoryDto(
    string Category, int Score, int MaxScore, IReadOnlyList<string> Items,
    IReadOnlyList<BenchmarkControlRefDto> Controls);
public sealed record BenchmarkedGapDto(
    string Severity, string Category, string Description,
    IReadOnlyList<BenchmarkControlRefDto> Controls);

// GET /posture/score?benchmark= — the summary shape with control IDs + coverage.
public sealed record BenchmarkedPostureDto(
    string Benchmark, string BenchmarkVersion, int Score, BenchmarkCoverageDto Coverage,
    IReadOnlyList<BenchmarkedScoreCategoryDto> Breakdown,
    IReadOnlyList<BenchmarkedGapDto> Gaps, PostureStatsDto Stats);

// GET /posture/trend — score + per-category scores over time + delta vs. earliest.
public sealed record PostureTrendPointDto(
    string CapturedUtc, int Score, IReadOnlyDictionary<string, int> CategoryScores);
public sealed record PostureTrendDeltaDto(
    string? Since, int ScoreChange, IReadOnlyList<string> Regressions,
    IReadOnlyList<PostureRemediationCandidateDto> RemediationCandidates);
// M18 feed (read-only): a regressed category surfaced as a remediation candidate for
// the autonomy loop's candidate list. Never auto-enqueued — M18 owns that decision.
public sealed record PostureRemediationCandidateDto(
    string Category, int ScoreChange, string? RemediationCandidateRef = null);
public sealed record PostureTrendDto(
    string Benchmark, IReadOnlyList<PostureTrendPointDto> Points, PostureTrendDeltaDto Delta);

// GET /posture/poam — gap → control(s) → remediation → due date.
public sealed record PoamItemDto(
    string Id, string Severity, string Category, string Finding,
    IReadOnlyList<BenchmarkControlRefDto> Controls, string Remediation, string Source,
    string? Owner, string OpenedUtc, string DueUtc, string State,
    string? RemediationCandidateRef = null);
public sealed record PoamDto(string Benchmark, string GeneratedUtc, IReadOnlyList<PoamItemDto> Items);

// POST /posture/evidence-pack — request body + the resulting signed manifest.
public sealed record EvidencePackRequest(
    string? AsOf, string? Benchmark, IReadOnlyList<string>? Surfaces, IReadOnlyList<string>? Formats);
public sealed record EvidenceArtifactRefDto(string Kind, string Format, string Path, string SourceEngine);
public sealed record EvidencePackManifestDto(
    string PackId, string AsOf, string Benchmark, string TenantName, int Score, string ZipPath,
    IReadOnlyList<EvidenceArtifactRefDto> Artifacts, IReadOnlyList<string> SnapshotIds, string ManifestHash);

// ─── Conditional Access readable summary (GET /conditional-access/{id}/summary) ─
// Deeply-nested CA conditions/controls reduced to readable strings, with user/app
// GUIDs resolved to names. Rendered as typed sections in the detail pane.
public sealed record CaSummaryDto(
    string State,
    CaCondDto Conditions,
    string GrantOperator,
    IReadOnlyList<string> GrantControls,
    IReadOnlyList<string> SessionControls);

public sealed record CaCondDto(
    string Users, string Applications, string Platforms, string Locations,
    string ClientApps, string SignInRisk, string UserRisk);

// ─── Conditional Access rich list + full detail (dedicated CA workspace) ──────
public sealed record CaPolicyListItemDto(
    string Id, string DisplayName, string? Description, string State,
    string Users, string Applications, string Platforms,
    IReadOnlyList<string> GrantControls, string Created, string Modified);

public sealed record CaDetailDto(
    string Id, string DisplayName, string? Description, string State,
    string Created, string Modified,
    CaCondDetailDto Conditions, CaGrantDto Grant, CaSessionDto Session);

public sealed record CaCondDetailDto(
    IReadOnlyList<string> IncludeUsers, IReadOnlyList<string> ExcludeUsers,
    IReadOnlyList<string> IncludeGroups, IReadOnlyList<string> ExcludeGroups,
    IReadOnlyList<string> IncludeApplications, IReadOnlyList<string> ExcludeApplications,
    IReadOnlyList<string> IncludePlatforms, IReadOnlyList<string> ExcludePlatforms,
    IReadOnlyList<string> IncludeLocations, IReadOnlyList<string> ExcludeLocations,
    IReadOnlyList<string> ClientAppTypes,
    IReadOnlyList<string> SignInRiskLevels, IReadOnlyList<string> UserRiskLevels);

public sealed record CaGrantDto(string Operator, IReadOnlyList<string> BuiltInControls, string? AuthStrength);
public sealed record CaSessionDto(string? SignInFrequency, string? PersistentBrowser, bool AppEnforced, bool CloudAppSecurity);

// ─── M13.2 MCP human-in-the-loop writes (the "Pending AI changes" inbox) ──────

// POST /pending-changes — an MCP propose_* tool enqueues a write here. `kind` is
// create|update|delete|assign; `diffJson` is the DriftChange[] the AI computed.
public sealed record CreatePendingChangeRequest(
    string Proposer, string Kind, string Path,
    string? ObjectId, string? ObjectName, string? BodyJson, string? DiffJson,
    string? SimulationJson = null);

// GET /pending-changes — one queued AI-proposed write for the approval inbox.
// `Changes` is the parsed diff (rendered with the same panel as a human edit);
// `BlastRadius` is the parsed M16 simulation (null if not simulated).
public sealed record PendingChangeDto(
    string Id, string Proposer, string Kind, string Path,
    string? ObjectId, string? ObjectName,
    IReadOnlyList<DriftChangeDto> Changes,
    string State, string CreatedUtc,
    BlastRadiusReportDto? BlastRadius = null);

// POST /pending-changes/{id}/reject — optional operator note.
public sealed record RejectPendingChangeRequest(string? Note);

// ─── Maester security-test integration (maester365/maester) ───────────────────
// The sidecar shells out to PowerShell 7 + the Maester module, sharing the active
// AuthSession token, and projects Invoke-Maester's JSON into these normalized DTOs.

// GET /maester/status — prerequisite probe (no auth).
public sealed record MaesterStatusDto(
    bool Available, string? PwshVersion, string? MaesterVersion, string? Message);

// GET /maester/run/status — cheap in-memory status of the current/last run (poll while Running).
// Phase/Completed/Total give live per-check progress while Running.
public sealed record MaesterRunStateDto(
    string Status, string? StartedAt, string? FinishedAt, string? Error,
    string? Phase = null, int Completed = 0, int Total = 0);

// POST /maester/run — optional Pester tag filter; omitted runs every check.
public sealed record MaesterRunRequest(IReadOnlyList<string>? Tags);

// One Maester test (control), categorized by the service it covers.
public sealed record MaesterControlDto(
    string Id, string Title, string Category, string? Severity, string Result, string? HelpUrl);

// Per-category roll-up (one per covered service).
public sealed record MaesterCategoryDto(
    string Name, int Total, int Passed, int Failed, int Skipped, int Score);

// A categorized Maester run. The body stored in the time-machine drops ExecutedAt
// (set null) so identical posture dedups by content hash and drift stays meaningful.
public sealed record MaesterRunResultDto(
    string? ExecutedAt, string OverallResult,
    int Total, int Passed, int Failed, int Skipped, int Error,
    IReadOnlyList<MaesterCategoryDto> Categories,
    IReadOnlyList<MaesterControlDto> Controls);
// ─── M16 Foresight: blast-radius simulator ────────────────────────────────────
// POST /simulate (+ /simulate/assignment) — pre-flight a proposed write. Reuses the
// PendingChange vocabulary (verb/path/objectId/bodyJson) so the same payload flows
// inbox → simulator. Verb is create|update|delete|assign; BodyJson is the object JSON
// (or an Assignment[] for verb=assign).
public sealed record SimulateRequest(
    string? Proposer, string Verb, string Path, string? ObjectId, string? BodyJson);

// Who a proposed change hits — pure compute over resolved assignments, no write.
// Counts are the proposed-minus-live delta (newly affected); severity rolls counts +
// conflicts into info|low|medium|high|critical.
public sealed record BlastRadiusReportDto(
    string Severity,
    string Summary,
    int AffectedUserCount,
    int AffectedDeviceCount,
    int NoLongerAffectedCount,
    IReadOnlyList<SamplePrincipalDto> SampleAffectedPrincipals,
    IReadOnlyList<SimConflictDto> Conflicts,
    IReadOnlyList<SimRedundancyDto> Redundancies,
    IReadOnlyList<CrossPolicyImpactDto> CrossPolicyImpacts);

// One affected user/device/group, for the report's preview slice.
public sealed record SamplePrincipalDto(
    string Type, string DisplayName, string? Upn, string Id, string Reason);

// A proposed include overlapping an exclude (include All Devices vs exclude Kiosks).
public sealed record SimConflictDto(string Kind, string Detail);

// A proposed target already covered by a live target (a group subset of All Users).
public sealed record SimRedundancyDto(string Kind, string Detail);

// A second-order impact on another policy (a compliance flip cascading into a CA lockout).
public sealed record CrossPolicyImpactDto(string PolicyId, string PolicyName, string Detail);

// ─── M15 Policy-as-Code / GitOps ──────────────────────────────────────────────
// pull → plan → (M6 gate) → apply over the existing Export / Import / Drift
// engines. The repo tree is a normalized on-disk mirror of tenant config; plan is
// a read-only DriftChange[] change set; apply is the M6 safe-write rail lifted to
// tree scope. Conditional Access is pull/plan only — never written.

// POST /gitops/pull — where to write the tree, and which surfaces ("*" = all).
public sealed record GitOpsPullRequest(string? OutputPath, IReadOnlyList<string>? Surfaces);

// manifest.json — provenance for a pull. `Surfaces` is folder-name → object count;
// `ContentHash` is the SHA-256 roll-up of every normalized file body.
public sealed record GitOpsManifest(
    int SchemaVersion,
    string? TenantId,
    string? TenantDomain,
    string PulledUtc,
    string? PulledBy,
    string CmpxVersion,
    IReadOnlyDictionary<string, int> Surfaces,
    int ObjectCount,
    string ContentHash);

// POST /gitops/plan — diff a repo tree against the live tenant.
public sealed record GitOpsPlanRequest(string? RepoPath, IReadOnlyList<string>? Surfaces);

public sealed record GitOpsPlan(
    string PlanId, string RepoPath, string GeneratedUtc,
    GitOpsPlanSummary Summary, IReadOnlyList<GitOpsPlanObject> Objects);

public sealed record GitOpsPlanSummary(int Add, int Change, int Destroy, int Noop);

// One object's verdict + its field-level DriftChange[] (same shape as the M6 diff
// panel and the drift timeline). Verdict is Added | Removed | Modified; Noop
// objects are counted in the summary but omitted from the list.
public sealed record GitOpsPlanObject(
    string Surface, string ObjectType, string ObjectName, string Verdict,
    IReadOnlyList<DriftChangeDto> Changes);

// POST /gitops/apply — confirm:true + the reviewed planId (M6 gate). ConfirmDestroy
// gates deletion of tenant objects absent from the repo (default off → skipped).
public sealed record GitOpsApplyRequest(string? RepoPath, string? PlanId, bool Confirm, bool ConfirmDestroy = false);

public sealed record GitOpsApplyResult(
    string PlanId, string AppliedUtc,
    IReadOnlyList<GitOpsApplyObject> Objects, GitOpsApplySummary Summary);

// Outcome is applied | skipped | conflict. `SnapshotId`/`NewId` set on apply;
// `Reason` explains a skip/conflict.
public sealed record GitOpsApplyObject(
    string ObjectName, string Outcome, string? SnapshotId = null, string? NewId = null, string? Reason = null);

public sealed record GitOpsApplySummary(int Applied, int Skipped, int Conflict);

// GET /gitops/status — is the tenant in sync with the repo tree?
public sealed record GitOpsStatus(
    string RepoPath, string? ManifestTenantId, bool InSync, GitOpsPlanSummary Summary);

// ─── M18 Autonomy: the closed-loop AI SRE (watch → detect → plan → simulate →
// propose → approve → apply → verify → audit) ─────────────────────────────────
// LOCKED: autonomy ends at the inbox. The policy toggles only which signals may
// auto-ENQUEUE a PendingChange; no value here auto-applies a write. The loop's
// terminal action is POST /pending-changes (state=pending) — byte-identical to an
// MCP propose_* tool — and a human still approves the exact diff.

// GET/PUT /autonomy/policy — per-tenant autonomy policy. `Enabled` gates the whole
// loop; each signal is toggled independently with its own severity/score floor.
public sealed record AutonomyPolicyDto(
    string? TenantId,
    bool Enabled,
    int CadenceMinutes,
    AutonomyScopeDto Scope,
    AutonomySignalsDto Signals,
    AutonomyThrottleDto Throttle,
    string Note);

public sealed record AutonomyScopeDto(
    IReadOnlyList<string> ObjectTypes,
    IReadOnlyList<string> AssignmentGroupAllowlist);

public sealed record AutonomySignalsDto(
    AutonomySignalConfigDto Drift,
    AutonomySignalConfigDto PostureRegression,
    AutonomySignalConfigDto Advisory);

// One signal's enqueue toggle. `MinSeverity` gates drift/advisory; `MinScoreDrop`
// gates postureRegression. Both nullable so a signal can toggle without the unused floor.
public sealed record AutonomySignalConfigDto(
    bool Enabled,
    string? MinSeverity,
    int? MinScoreDrop);

public sealed record AutonomyThrottleDto(
    int MaxProposalsPerRun,
    int MaxInflightPending);

// GET /autonomy/runs(/{id}) — one scheduled loop tick, end-to-end. `Watched` is the
// delta-sync result; `Detected` the signals found; `Proposed` the pending-changes
// raised (with blast-radius); `Decision`/`Verify` fill in as the operator acts and
// the next tick re-checks.
public sealed record AutonomyRunDto(
    string RunId,
    string? TenantId,
    string StartedUtc,
    string? FinishedUtc,
    AutonomyWatchedDto Watched,
    IReadOnlyList<AutonomyDetectionDto> Detected,
    IReadOnlyList<AutonomyProposalDto> Proposed,
    AutonomyDecisionDto? Decision,
    AutonomyVerifyDto? Verify);

public sealed record AutonomyWatchedDto(
    int AuditEventsPulled,
    int SnapshotsChanged,
    IReadOnlyList<string> ObjectTypes);

// One detected signal on one object, with the field-level diff (DriftChange[], same
// shape as the inbox + drift timeline) and the base/head snapshot ids it spans.
public sealed record AutonomyDetectionDto(
    string Signal,
    string ObjectId,
    string? ObjectName,
    string Severity,
    string? BaseSnapshotId,
    string? HeadSnapshotId,
    IReadOnlyList<DriftChangeDto> Changes);

// A pending-change the loop raised, with the blast-radius it carries into the inbox.
public sealed record AutonomyProposalDto(
    string PendingChangeId,
    string Kind,
    string Path,
    BlastRadiusReportDto? BlastRadius);

// The operator's verdict — mirrors what /pending-changes/{id}/approve|reject writes.
// `Action` is approved|rejected|failed|pending.
public sealed record AutonomyDecisionDto(
    string PendingChangeId,
    string Action,
    string? Operator,
    string? DecidedUtc,
    string? AppliedObjectId,
    string? AuditEventId);

// The follow-up convergence check — re-diff the new head vs baseline.
// residualDriftChanges == 0 ⇒ converged.
public sealed record AutonomyVerifyDto(
    string VerifiedUtc,
    int ResidualDriftChanges,
    bool Converged,
    string? VerifySnapshotId);
// ─── M17 Tenant digital twin — offline graph analytics ────────────────────────
// One local graph of the tenant, materialized from the cache + snapshot store; the
// analytics run as traversals over it and never call Graph.
public sealed record TwinStats(
    int NodeCount, int EdgeCount, string? BuiltUtc, bool Stale, string Source);

public sealed record TwinNode(
    string Id, string Type, string Name, IReadOnlyDictionary<string, string> Props);

public sealed record TwinEdge(
    string From, string To, string Type, string? FilterId = null, string? Intent = null);

// A related node referenced by a finding (a group, a contending policy).
public sealed record TwinRef(string Id, string Name, string Role, int? Count = null);

// One normalized analytics finding — uniform shape across all seven queries.
public sealed record TwinFinding(
    string? Id, string? Name, string Kind, string Detail,
    IReadOnlyList<TwinRef> Refs, IReadOnlyDictionary<string, int> Metrics);

public sealed record TwinAnalyticsResult(
    string Query, string? BuiltUtc, string Source, IReadOnlyList<TwinFinding> Findings);

public sealed record TwinEdgeView(string From, string To, string Type, TwinNode OtherNode);

public sealed record TwinNeighborhood(
    TwinNode Node, IReadOnlyList<TwinEdgeView> Inbound, IReadOnlyList<TwinEdgeView> Outbound,
    IReadOnlyList<TwinNode> Members, IReadOnlyList<string> Warnings);

public sealed record TwinQueryRequest(
    string FromType, IReadOnlyList<string> EdgePath, IReadOnlyDictionary<string, string>? Predicates);

public sealed record TwinQueryResult(IReadOnlyList<TwinNode> Nodes, IReadOnlyList<TwinEdge> Edges);
// ─── M20 Fleet — MSP-scale multi-tenant fan-out (docs/part-ii/M20-fleet.md) ───
// Pattern G: the same (verb, path, body) replayed across a tenant SET; reads merged
// tenant-tagged, each write a gated M13 replay (POST /pending-changes per target).
// All camelCase over the wire, mirrored byte-for-byte in crates/api-types/src/lib.rs.

// GET /fleet/tenants — a known tenant profile + its live fleet-session sign-in state.
public sealed record FleetTenantDto(
    string TenantId, string ProfileId, string TenantName, string AuthState, bool SignedIn);

// A named, persisted set of tenantIds — a saved fan-out target. Doubles as the
// POST /fleet/groups request body (CreatedUtc/Id are server-assigned when absent).
public sealed record TenantGroupDto(
    string Id, string Name, IReadOnlyList<string> TenantIds,
    string? GoldenTenantId, string CreatedUtc);

// Per-tenant status header in a fan-out response.
public sealed record FleetTenantStatusDto(string TenantId, string TenantName, string Status);

// A ListItemDto stamped with its source tenant (fleet-tagged list row).
public sealed record FleetListItemDto(
    string Id, string Title, string Subtitle, string? Badge,
    string? Platform, string? Modified, string TenantId, string TenantName);

// A per-tenant error in a fan-out response — that tenant's rows are omitted, fleet returns.
public sealed record FleetErrorDto(string TenantId, string Code, string Message);

// GET /fleet/list/{surface} — merged tenant-tagged rows + per-tenant status/errors.
public sealed record FleetListResponseDto(
    string Surface, string Group,
    IReadOnlyList<FleetTenantStatusDto> Tenants,
    IReadOnlyList<FleetListItemDto> Items,
    IReadOnlyList<FleetErrorDto> Errors);

// Where a campaign's baseline comes from. kind=goldenTenant reads the surface from a
// live tenant; goldenRepo (M15 GitOps) is deferred.
public sealed record GoldenRefDto(string Kind, string? TenantId, string? RepoRef);

// POST /fleet/campaign — broadcast a baseline to a group. dryRun previews per-tenant
// diffs and applies nothing; otherwise each target is enqueued as a gated M13 replay.
public sealed record CampaignRequestDto(
    string? CampaignId, string Group, GoldenRefDto Golden,
    string Surface, string ObjectName,
    string? Verb, bool? DryRun, string? Gate,
    Dictionary<string, JsonElement>? Overrides);

// One target tenant's campaign outcome (never collapsed into a single fleet result).
public sealed record CampaignTenantResultDto(
    string TenantId, string TenantName, string Outcome,
    string? Reason, string? PendingId, string? NewId,
    IReadOnlyList<string> AppliedFields,
    IReadOnlyList<DriftChangeDto> Diff);

public sealed record CampaignSummaryDto(
    int Success, int Skip, int Conflict, int Error, int Pending, int Total);

// GET /fleet/campaign/{id} — a campaign's aggregated per-tenant outcomes.
public sealed record CampaignResultDto(
    string CampaignId, string Surface, string ObjectName, bool DryRun,
    IReadOnlyList<CampaignTenantResultDto> Results, CampaignSummaryDto Summary);

public sealed record FleetDriftFieldDto(string Field, object? Golden, object? Actual);

// One tenant's divergence from the resolved effective golden.
public sealed record FleetDriftTenantDto(
    string TenantId, string TenantName, string Status, int DriftCount,
    string? Note, IReadOnlyList<FleetDriftFieldDto> Drifts);

public sealed record FleetDriftSummaryDto(int Golden, int InSync, int Diverged, int Error);

// GET /fleet/drift — per-tenant field-level divergence from the resolved golden.
public sealed record FleetDriftResponseDto(
    string Group, string? Golden, string Surface, string ObjectName,
    IReadOnlyList<FleetDriftTenantDto> Tenants, FleetDriftSummaryDto Summary);

public sealed record FleetPostureTenantDto(
    string TenantId, string TenantName, string Status, int? Score);

// GET /fleet/posture — per-tenant posture score + fleet roll-up.
public sealed record FleetPostureResponseDto(
    string Group, int AverageScore, int ScoredTenants,
    IReadOnlyList<FleetPostureTenantDto> Tenants);

// A golden template + override layers. effective = golden ⊕ groupOverride ⊕ tenantOverride.
// Doubles as the POST /fleet/templates request body.
public sealed record GoldenTemplateDto(
    string Id, string Name, string Surface, string ObjectName, GoldenRefDto Golden,
    string? BaselineJson,
    Dictionary<string, JsonElement>? GroupOverrides,
    Dictionary<string, JsonElement>? TenantOverrides,
    string CreatedUtc);
// ─── M21 Ecosystem — shareable packs, playbooks, marketplace ──────────────────
// All three converge on existing chokepoints: a pack adopt → M15 gitops plan/apply
// (over loopback; graceful when M15 isn't in the build), a playbook run → M13
// /pending-changes, a marketplace install → an M13.3 plugins.json row. No new write
// path is introduced — the design (docs/part-ii/M21-ecosystem.md) is wiring.

// A pack is a versioned M15 desired-state repo + this top-level pack.json manifest.
// `TargetSurfaces` mirror Surfaces.cs path keys; `RepoPath` is the local working tree
// the manifest was loaded from (used to run the M15 plan/apply). `Signature` is reserved.
public sealed record PackManifest(
    string SchemaVersion,
    string Id,
    string Name,
    string Version,
    string? Publisher,
    string? Description,
    IReadOnlyList<string> TargetSurfaces,
    string? MinSidecar,
    IReadOnlyList<PackParameter> Parameters,
    string? RepoPath,
    int ObjectCount,
    string? Signature);

// One adoption parameter the operator binds before plan (e.g. a target group id).
public sealed record PackParameter(
    string Name,
    string Type,
    string? Prompt,
    bool Required,
    string? Default,
    string? AppliesTo);

// POST /packs/{id}/adopt — bind params, then plan (confirm:false, default) or apply
// (confirm:true + a reviewed PlanId). Plan-only is the Terraform-style preview.
public sealed record PackAdoptRequest(
    Dictionary<string, string>? Parameters,
    bool Confirm,
    string? PlanId,
    bool ConfirmDestroy);

// The outcome of an adopt. `Phase` = plan | apply | error. `RequiresM15` is true when
// the GitOps endpoints are absent in this build (this branch bases off main). `Plan` /
// `ApplyResult` carry the raw M15 body (passthrough) for whichever phase ran.
public sealed record PackAdoptResult(
    string PackId,
    string Phase,
    bool RequiresM15,
    string? Message,
    string? PlanId = null,
    string? Summary = null,
    object? Plan = null,
    object? ApplyResult = null);

// A parameterized, ordered set of proposed writes. Each native step expands to a
// propose_* that lands as a PendingChange; plugin steps are pass-through.
public sealed record Playbook(
    string SchemaVersion,
    string Id,
    string Name,
    string Version,
    string? Description,
    IReadOnlyList<PackParameter> Parameters,
    IReadOnlyList<PlaybookStep> Steps);

// One ordered step. `Tool` is a native propose_* or an aggregated plugin tool
// (plugin_<name>_<tool>). `{{param}}` tokens in Path/ObjectId/Body are substituted.
public sealed record PlaybookStep(
    string Id,
    string Tool,
    string? Path,
    string? ObjectId,
    Dictionary<string, object>? Body,
    Dictionary<string, object>? Args,
    bool? Gated,
    IReadOnlyList<string>? DependsOn);

// POST /playbooks/{id}/run — bind params before expanding the ordered steps.
public sealed record PlaybookRunRequest(Dictionary<string, string>? Parameters);

// The outcome of a run. `PendingChangeIds` is the flat list of created change ids for
// polling via get_change_status; `Proposer` is playbook:{id} (the audit-trail origin).
public sealed record PlaybookRunResult(
    string PlaybookId,
    string Proposer,
    IReadOnlyList<PlaybookStepResult> Steps,
    IReadOnlyList<string> PendingChangeIds);

// One step's outcome — enqueued (native, with a change id), passthrough (plugin),
// skipped, or error.
public sealed record PlaybookStepResult(
    string StepId,
    string Tool,
    string Outcome,
    string? PendingChangeId = null,
    string? Message = null);

// A curated, installable downstream MCP plugin. Installing binds `ConfigFields` and
// appends a plugins.json row (M13.3 shape). `Trust` surfaces whether the plugin's own
// side-effects are read-only or writes-side-effecting. `Installed` is true when a
// plugins.json row already exists for this entry.
public sealed record MarketplaceEntry(
    string Id,
    string Name,
    string? Category,
    string? Publisher,
    bool Verified,
    string? Description,
    string Transport,
    string? UrlTemplate,
    string? Command,
    IReadOnlyList<string>? Args,
    IReadOnlyList<MarketplaceConfigField> ConfigFields,
    IReadOnlyList<string>? ExposesTools,
    string? Trust,
    bool Installed);

// One value the operator supplies at install. When `Header` is set the bound value
// becomes that HTTP header (applying `Format`, e.g. "Bearer {{token}}").
public sealed record MarketplaceConfigField(
    string Name,
    string Type,
    bool Required,
    string? Header,
    string? Format);

// POST /marketplace/{id}/install — bind the entry's configFields before writing the row.
public sealed record MarketplaceInstallRequest(Dictionary<string, string>? Config);

// The plugins.json row written (secrets redacted in the echo).
public sealed record MarketplaceInstallResult(
    string EntryId,
    string PluginName,
    string Transport,
    bool Installed,
    string? Message = null);

// ── Policy Comparison — a SettingsCatalog baseline vs a tenant policy ──────────
// Mirrors crates/api-types/src/lib.rs. Projects Core's BaselineComparisonResult
// (BaselineService.CompareSettingsCatalog) over the wire, grouped by verdict.

// POST /baselines/compare body — a baseline FileName + a tenant SettingsCatalog policy id.
public sealed record BaselineCompareRequest(string? BaselineId, string? PolicyId);

// POST /policies/compare body — two live SettingsCatalog policy ids (A vs B).
public sealed record PolicyCompareRequest(string? AId, string? BId);

// One setting's A-vs-B values (both optional: missing on the tenant/B side, or extra
// beyond the baseline/A side). SettingName is the human-readable label resolved from the
// embedded Settings Catalog definition registry (null when the id isn't in the catalog).
public sealed record BaselineSettingComparisonDto(
    string SettingDefinitionId, string? SettingName, string? BaselineValue, string? TenantValue);

// Settings grouped by verdict (matching / missing / drifted / extra).
public sealed record BaselineComparisonDto(
    string BaselineName,
    string? TenantPolicyId,
    string? TenantPolicyName,
    IReadOnlyList<BaselineSettingComparisonDto> Matching,
    IReadOnlyList<BaselineSettingComparisonDto> Missing,
    IReadOnlyList<BaselineSettingComparisonDto> Drifted,
    IReadOnlyList<BaselineSettingComparisonDto> Extra);

// ── Bulk App Assignment — assign one Assignment[] to many apps at once ─────────
// Mirrors crates/api-types/src/lib.rs. Loops ApplicationService.AssignApplicationAsync.

// POST /apps/assign body — the same assignments applied to every app in AppIds.
public sealed record BulkAssignRequest(
    IReadOnlyList<string> AppIds, IReadOnlyList<AssignmentDto> Assignments, bool? DryRun);

// One app's outcome (replace-all per app is all-or-nothing).
public sealed record BulkAssignItemResult(string AppId, bool Ok, string? Error);

public sealed record BulkAssignResult(IReadOnlyList<BulkAssignItemResult> Results);

// ── Detection & Remediation — proactive-remediation run state ──────────────────
// Mirrors crates/api-types/src/lib.rs. Projects Graph DeviceHealthScriptDeviceState.

// One device's detection + remediation state for a deployed remediation script.
public sealed record DeviceRunStateDto(
    string DeviceId, string? DeviceName, string? DetectionState, string? RemediationState, string? LastStateUpdateUtc);

// POST /remediation-scripts/{id}/run/{deviceId} body — confirm gate for the on-demand run.
public sealed record RunRemediationRequest(bool Confirm);

// GET /remediation-scripts/{id}/scripts — a remediation script's decoded detection +
// remediation PowerShell (either may be null: detection-only, or a global script whose
// bytes Graph doesn't return).
public sealed record RemediationScriptContentDto(
    string ScriptId, string? ScriptName, string? Detection, string? Remediation);
