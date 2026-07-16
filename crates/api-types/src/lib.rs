//! DTOs mirroring `contract/openapi.yaml`.
//!
//! Hand-written for now — keep in sync with the contract, or replace this crate
//! with code generated from the OpenAPI spec (see contract/README.md).

use std::collections::BTreeMap;
use std::collections::HashMap;

use serde::{Deserialize, Serialize};

/// Uniform error envelope — the ONE shape every non-2xx sidecar response carries
/// (mirrors `ErrorDto` in service/Api/Contracts.cs and schema `Error` in
/// contract/openapi.yaml). The thin client already reads the `error` field verbatim
/// (see api_client.rs `body_error`); this struct lets a caller deserialize the full
/// envelope — `status` echoes the HTTP status, `detail` is optional context, and
/// `trace_id` correlates the response with the server-side trace when telemetry is on.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ApiError {
    pub error: String,
    #[serde(default)]
    pub detail: Option<String>,
    pub status: i32,
    #[serde(default)]
    pub trace_id: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SyncStatus {
    pub healthy: bool,
    pub auth_state: AuthState,
    pub tenant_id: Option<String>,
    pub profile_name: Option<String>,
    pub last_sync_utc: Option<String>,
    /// M12.1 — when the blob read-through cache last completed a full warm.
    #[serde(default)]
    pub last_warmed_utc: Option<String>,
    pub cloud: Option<Cloud>,
    pub device_code: Option<DeviceCodePrompt>,
    pub error: Option<String>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum AuthState {
    SignedOut,
    AwaitingDeviceCode,
    /// The sidecar opened the system browser for an interactive (delegated) sign-in
    /// and is waiting for the user to finish — distinct from the generic `SigningIn`.
    AwaitingInteractive,
    SigningIn,
    SignedIn,
    Failed,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DeviceCodePrompt {
    pub user_code: String,
    pub verification_uri: String,
    pub message: String,
    pub expires_utc: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TenantProfileSummary {
    pub id: String,
    pub name: String,
    pub tenant_id: String,
    pub cloud: Option<Cloud>,
    pub auth_method: Option<AuthMethod>,
    pub is_active: bool,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum AuthMethod {
    Interactive,
    ClientSecret,
    DeviceCode,
}

/// Payload to create (POST /profiles) or edit (PATCH /profiles/{id}) a tenant
/// profile (M11 profile lifecycle). On edit, blank string fields keep their current
/// server value; `client_secret` of "" clears the saved secret.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct NewProfile {
    pub name: String,
    pub tenant_id: String,
    pub client_id: String,
    pub cloud: Option<Cloud>,
    pub auth_method: Option<AuthMethod>,
    pub client_secret: Option<String>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum Cloud {
    Commercial,
    #[serde(rename = "GCC")]
    Gcc,
    #[serde(rename = "GCCHigh")]
    GccHigh,
    DoD,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AuditEvent {
    pub id: String,
    pub timestamp: String,
    pub actor: Option<String>,
    pub action: String,
    pub object_type: String,
    pub object_id: String,
    pub object_name: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DriftRecord {
    pub object_id: String,
    pub base_snapshot_id: Option<String>,
    pub head_snapshot_id: Option<String>,
    pub changes: Vec<DriftChange>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DriftChange {
    pub path: String,
    pub kind: DriftKind,
    pub before: Option<serde_json::Value>,
    pub after: Option<serde_json::Value>,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub enum DriftKind {
    Added,
    Removed,
    Modified,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SearchResult {
    pub kind: SearchKind,
    pub id: String,
    pub score: Option<f64>,
    pub summary: String,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub enum SearchKind {
    AuditEvent,
    ConfigSnapshot,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DriftObject {
    pub object_id: String,
    pub object_type: String,
    pub object_name: Option<String>,
    // Signed to mirror the service's `int` wire type exactly: a stray negative
    // would otherwise fail the whole Vec<DriftObject> decode. Values are always
    // >= 0 in practice (see `minimum: 0` in contract/openapi.yaml).
    pub snapshot_count: i32,
    pub last_captured_utc: String,
    pub change_count: i32,
}

/// One Intune application (MobileApp) row for the Applications list. Mirrors the
/// service `AppListItemDto`; the projection from the Graph `MobileApp` union type
/// (app type / platform) is done server-side.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AppListItem {
    pub id: String,
    pub display_name: String,
    pub description: Option<String>,
    pub publisher: Option<String>,
    pub app_type: String,
    pub platform: String,
    pub created_date_time: String,
    pub last_modified_date_time: String,
    pub is_assigned: bool,
    pub publishing_state: String,
    pub is_featured: bool,
}

/// A normalized list row used by every generic LIVE list screen (title + subtitle
/// + optional badge). Per-feature endpoints project their Graph types into this
/// shape server-side so the client renders them uniformly in `list_workspace`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ListItem {
    pub id: String,
    pub title: String,
    pub subtitle: String,
    pub badge: Option<String>,
    /// Optional rich-list columns — present only on surfaces that project them; the
    /// client shows a Platform column + filter chips when any row carries one.
    #[serde(default)]
    pub platform: Option<String>,
    #[serde(default)]
    pub modified: Option<String>,
    /// The backing MDM provider ("jamf", …) when the row comes from a non-Intune
    /// provider (M22 cross-MDM); null/absent = Intune (the implicit default).
    #[serde(default)]
    pub source: Option<String>,
}

/// GET /providers — a registered MDM provider (M22 cross-MDM) + the catalog surfaces
/// it backs. `intune` is the implicit default; others (e.g. `jamf`) back a subset.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MdmProviderDescriptor {
    pub id: String,
    pub supported_surfaces: Vec<String>,
}

/// One row in the M14 device-action catalog (GET /managed-devices/actions): the
/// managedDevice action verbs the sidecar dispatches. `destructive` flags
/// wipe/retire/fresh-start (typed-confirm + opt-in gated); `needs_confirm` drives the
/// client's confirm dialog. Mirrors the server `DeviceActionInfoDto`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DeviceActionInfo {
    pub id: String,
    pub display_name: String,
    pub destructive: bool,
    pub needs_confirm: bool,
}

/// One per-device outcome from a bulk device action
/// (POST /managed-devices/actions/{action}). Mirrors `BulkDeviceActionResultDto`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BulkDeviceActionResult {
    pub device_id: String,
    pub ok: bool,
    pub error: Option<String>,
}

/// One row of a device's action HISTORY (GET /managed-devices/{id}/actions). Projects a
/// Graph `deviceActionResult`: `action` ← actionName, `state` ← actionState, `requested_utc`
/// ← startDateTime, `completed_utc` ← lastUpdatedDateTime. `id` is synthesized
/// (actionName#index); `actor` is null from Graph (reserved for a future audit join).
/// Mirrors the server `DeviceActionRecord`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DeviceActionRecord {
    pub id: String,
    pub action: String,
    pub state: Option<String>,
    pub requested_utc: Option<String>,
    pub completed_utc: Option<String>,
    pub actor: Option<String>,
}

/// One LIST-key blob-cache status row for the active tenant (GET /cache, M12.1).
/// DETAIL keys ({key}/{id}) are lazy-only and not enumerable, so this reflects
/// LIST-key coverage only. `cached_at_utc` is null when the key is missing/expired;
/// `warm_ahead` flags keys PrefetchAllToCacheAsync warms before first use. Mirrors
/// the server `CacheEntryStatusDto`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CacheEntryStatus {
    pub key: String,
    pub display_name: String,
    pub cached_at_utc: Option<String>,
    pub item_count: i32,
    pub warm_ahead: bool,
}

/// Header summary for the cache-dev screen (M12.1). `available` is false on the
/// NullCacheService (cache disabled); `entry_count`/`total_items` count live
/// (non-expired) LIST keys only. Mirrors the server `CacheSummaryDto`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct CacheSummary {
    pub available: bool,
    pub last_warmed_utc: Option<String>,
    pub entry_count: i32,
    pub total_items: i32,
}

/// One row in an object's snapshot history (newest first), for the restore/undo
/// picker. Carries the full body so "restore" can re-PATCH a past version.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SnapshotSummary {
    pub snapshot_id: String,
    pub captured_utc: String,
    pub object_type: String,
    pub object_name: Option<String>,
    pub body_json: String,
}

/// One restored surface (POST /import?dryRun=false): how many objects were created
/// in the live tenant vs failed, with the first few error messages.
#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ImportRestoreGroup {
    #[serde(rename = "type")]
    pub kind: String,
    pub created: i32,
    pub failed: i32,
    #[serde(default)]
    pub errors: Vec<String>,
}

/// Result of a live restore from a backup .zip — per-type created/failed totals.
#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ImportRestoreResult {
    pub applied: bool,
    pub total_created: i32,
    pub total_failed: i32,
    pub groups: Vec<ImportRestoreGroup>,
}

/// Scored security posture (GET /security-posture/summary): a 0-100 score across
/// weighted categories, severity-ranked gaps, and headline stats.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct SecurityPosture {
    pub score: i32,
    pub breakdown: Vec<ScoreCategory>,
    pub gaps: Vec<SecurityGap>,
    pub stats: PostureStats,
    pub ca_policies: Vec<CaListRow>,
    pub compliance_policies: Vec<ComplianceRow>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ScoreCategory {
    pub category: String,
    pub score: i32,
    pub max_score: i32,
    pub items: Vec<String>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SecurityGap {
    pub severity: String, // high | medium | low
    pub category: String,
    pub description: String,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct PostureStats {
    pub ca_total: i32,
    pub ca_enabled: i32,
    pub ca_report_only: i32,
    pub ca_disabled: i32,
    pub compliance_policies: i32,
    pub compliance_platforms: Vec<String>,
    pub endpoint_security_intents: i32,
    pub app_protection_policies: i32,
    pub auth_strength_policies: i32,
    pub named_locations: i32,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CaListRow {
    pub id: String,
    pub name: String,
    pub state: String,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ComplianceRow {
    pub id: String,
    pub name: String,
    pub platform: String,
}

/// Readable Conditional Access summary (GET /conditional-access/{id}/summary):
/// nested conditions/controls reduced to strings, GUIDs resolved to names.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CaSummary {
    pub state: String,
    pub conditions: CaCond,
    pub grant_operator: String,
    pub grant_controls: Vec<String>,
    pub session_controls: Vec<String>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CaCond {
    pub users: String,
    pub applications: String,
    pub platforms: String,
    pub locations: String,
    pub client_apps: String,
    pub sign_in_risk: String,
    pub user_risk: String,
}

/// One row in the rich Conditional Access grid (GET /conditional-access/list).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CaPolicyListItem {
    pub id: String,
    pub display_name: String,
    pub description: Option<String>,
    pub state: String,
    pub users: String,
    pub applications: String,
    pub platforms: String,
    pub grant_controls: Vec<String>,
    pub created: String,
    pub modified: String,
}

/// Full CA policy detail (GET /conditional-access/{id}/detail): include/exclude
/// conditions with GUIDs resolved to display names, grant + session controls.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CaDetail {
    pub id: String,
    pub display_name: String,
    pub description: Option<String>,
    pub state: String,
    pub created: String,
    pub modified: String,
    pub conditions: CaCondDetail,
    pub grant: CaGrant,
    pub session: CaSession,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CaCondDetail {
    pub include_users: Vec<String>,
    pub exclude_users: Vec<String>,
    pub include_groups: Vec<String>,
    pub exclude_groups: Vec<String>,
    pub include_applications: Vec<String>,
    pub exclude_applications: Vec<String>,
    pub include_platforms: Vec<String>,
    pub exclude_platforms: Vec<String>,
    pub include_locations: Vec<String>,
    pub exclude_locations: Vec<String>,
    pub client_app_types: Vec<String>,
    pub sign_in_risk_levels: Vec<String>,
    pub user_risk_levels: Vec<String>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CaGrant {
    pub operator: String,
    pub built_in_controls: Vec<String>,
    pub auth_strength: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CaSession {
    pub sign_in_frequency: Option<String>,
    pub persistent_browser: Option<String>,
    pub app_enforced: bool,
    pub cloud_app_security: bool,
}

/// One AI-proposed write awaiting operator approval (M13.2), for the "Pending AI
/// changes" inbox. `kind` is create|update|delete|assign; `changes` is the field-level
/// diff the AI saw (rendered with the same panel as a human edit); `state` is
/// pending|applied|rejected|failed.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PendingChange {
    pub id: String,
    pub proposer: String,
    pub kind: String,
    pub path: String,
    pub object_id: Option<String>,
    pub object_name: Option<String>,
    pub changes: Vec<DriftChange>,
    pub state: String,
    pub created_utc: String,
    /// M16 blast-radius report attached at propose time (None if not simulated).
    #[serde(default)]
    pub blast_radius: Option<BlastRadiusReport>,
}

// ── M16 Foresight: blast-radius simulator (simulate-before-write) ───────────────

/// POST /simulate request — pre-flight a proposed write. Reuses the PendingChange
/// vocabulary (verb/path/objectId/bodyJson). `verb` is create|update|delete|assign;
/// `body_json` is the object JSON (or an Assignment[] for verb=assign).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct SimulateRequest {
    pub proposer: Option<String>,
    pub verb: String,
    pub path: String,
    pub object_id: Option<String>,
    pub body_json: Option<String>,
}

/// Who a proposed change hits — pure compute over resolved assignments, no write.
/// Counts are the proposed-minus-live delta (newly affected); `severity` rolls
/// counts + conflicts into info|low|medium|high|critical.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct BlastRadiusReport {
    pub severity: String,
    pub summary: String,
    pub affected_user_count: i32,
    pub affected_device_count: i32,
    pub no_longer_affected_count: i32,
    pub sample_affected_principals: Vec<SamplePrincipal>,
    pub conflicts: Vec<SimConflict>,
    pub redundancies: Vec<SimRedundancy>,
    pub cross_policy_impacts: Vec<CrossPolicyImpact>,
}

/// One affected user/device/group, for the report's preview slice.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SamplePrincipal {
    pub r#type: String, // user | device | group
    pub display_name: String,
    pub upn: Option<String>,
    pub id: String,
    pub reason: String,
}

/// A proposed include overlapping an exclude (include All Devices vs exclude Kiosks).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SimConflict {
    pub kind: String,
    pub detail: String,
}

/// A proposed target already covered by a live target (a group subset of All Users).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SimRedundancy {
    pub kind: String,
    pub detail: String,
}

/// A second-order impact on another policy (a compliance flip cascading into a CA lockout).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CrossPolicyImpact {
    pub policy_id: String,
    pub policy_name: String,
    pub detail: String,
}

/// A normalized Intune assignment row (shared by every assignable surface). `intent`
/// is apps-only. Drives the assignment editors; the sidecar translates to/from the
/// per-resource typed Graph assignment.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Assignment {
    pub kind: String, // group | exclusionGroup | allDevices | allUsers
    pub group_id: Option<String>,
    pub group_name: Option<String>,
    pub filter_id: Option<String>,
    pub filter_mode: Option<String>, // include | exclude
    pub intent: Option<String>,      // apps: required | available | uninstall | availableWithoutEnrollment
}

// ── Maester security-test integration (maester365/maester) ─────────────────────
// Mirror the sidecar's Maester DTOs. The sidecar shells out to PowerShell + the
// Maester module, shares the active session token, and categorizes results.

/// Whether the sidecar can run Maester (PowerShell 7 + Maester module present).
#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MaesterStatus {
    pub available: bool,
    pub pwsh_version: Option<String>,
    pub maester_version: Option<String>,
    pub message: Option<String>,
}

/// In-memory status of the current/last Maester run (POST /maester/run returns this;
/// poll GET /maester/run/status until `status` leaves "Running", then GET /maester/results).
#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MaesterRunState {
    pub status: String, // Idle | Running | Completed | Failed
    pub started_at: Option<String>,
    pub finished_at: Option<String>,
    pub error: Option<String>,
    /// Live phase while Running (e.g. "Connecting to services", "Running checks").
    #[serde(default)]
    pub phase: Option<String>,
    /// Checks completed so far in the in-flight run (0 when not running).
    #[serde(default)]
    pub completed: i32,
    /// Expected total checks (from the prior run; 0 when unknown).
    #[serde(default)]
    pub total: i32,
}

/// One Maester test (control), categorized by the service it covers.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MaesterControl {
    pub id: String,
    pub title: String,
    pub category: String, // Entra | Intune | Exchange | Defender | Teams | SharePoint | Other
    pub severity: Option<String>,
    pub result: String, // Passed | Failed | Skipped | NotRun | Error
    pub help_url: Option<String>,
}

/// Per-category roll-up (one per covered service).
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MaesterCategory {
    pub name: String,
    pub total: i32,
    pub passed: i32,
    pub failed: i32,
    pub skipped: i32,
    pub score: i32, // 0-100: passed / (passed+failed)
}

/// A categorized Maester run (POST /maester/run, GET /maester/results). `executed_at`
/// is null on the stored time-machine body so identical posture dedups.
#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MaesterRunResult {
    pub executed_at: Option<String>,
    #[serde(default)]
    pub overall_result: String,
    pub total: i32,
    pub passed: i32,
    pub failed: i32,
    pub skipped: i32,
    pub error: i32,
    pub categories: Vec<MaesterCategory>,
    pub controls: Vec<MaesterControl>,
}
// ── M15 Policy-as-Code / GitOps (pull → plan → apply over Export/Import/Drift) ──

/// POST /gitops/pull body — destination dir + surfaces to include ("*" = all).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct GitOpsPullRequest {
    pub output_path: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub surfaces: Option<Vec<String>>,
}

/// manifest.json — provenance for a pull. `surfaces` is folder-name → object count;
/// `content_hash` is the SHA-256 roll-up of every normalized file body.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GitOpsManifest {
    pub schema_version: i32,
    pub tenant_id: Option<String>,
    pub tenant_domain: Option<String>,
    pub pulled_utc: String,
    pub pulled_by: Option<String>,
    pub cmpx_version: String,
    pub surfaces: BTreeMap<String, i32>,
    pub object_count: i32,
    pub content_hash: String,
}

/// POST /gitops/plan body.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct GitOpsPlanRequest {
    pub repo_path: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub surfaces: Option<Vec<String>>,
}

/// A Terraform-style change set — per-object `DriftChange[]` grouped by verdict.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GitOpsPlan {
    pub plan_id: String,
    pub repo_path: String,
    pub generated_utc: String,
    pub summary: GitOpsPlanSummary,
    pub objects: Vec<GitOpsPlanObject>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct GitOpsPlanSummary {
    pub add: i32,
    pub change: i32,
    pub destroy: i32,
    pub noop: i32,
}

/// One object's verdict + field-level `DriftChange[]` (same shape as the M6 diff
/// panel). `verdict` is Added | Removed | Modified.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GitOpsPlanObject {
    pub surface: String,
    pub object_type: String,
    pub object_name: String,
    pub verdict: String,
    pub changes: Vec<DriftChange>,
}

/// POST /gitops/apply body — confirm:true + the reviewed planId (M6 gate).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct GitOpsApplyRequest {
    pub repo_path: String,
    pub plan_id: String,
    pub confirm: bool,
    #[serde(default)]
    pub confirm_destroy: bool,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GitOpsApplyResult {
    pub plan_id: String,
    pub applied_utc: String,
    pub objects: Vec<GitOpsApplyObject>,
    pub summary: GitOpsApplySummary,
}

/// `outcome` is applied | skipped | conflict.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GitOpsApplyObject {
    pub object_name: String,
    pub outcome: String,
    pub snapshot_id: Option<String>,
    pub new_id: Option<String>,
    pub reason: Option<String>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct GitOpsApplySummary {
    pub applied: i32,
    pub skipped: i32,
    pub conflict: i32,
}

/// GET /gitops/status — is the tenant in sync with the repo tree?
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GitOpsStatus {
    pub repo_path: String,
    pub manifest_tenant_id: Option<String>,
    pub in_sync: bool,
    pub summary: GitOpsPlanSummary,
}

// ── Groups detail (mirrors the sidecar's GroupDetailDto / IC GroupDetailPanel) ──

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GroupMemberCounts {
    pub users: i64,
    pub devices: i64,
    pub nested_groups: i64,
    pub total: i64,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GroupMember {
    pub member_type: String, // User | Device | Group
    pub display_name: String,
    pub secondary_info: String,
    pub tertiary_info: String,
    pub status: String,
    pub id: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GroupDetail {
    pub id: String,
    pub display_name: String,
    pub description: Option<String>,
    pub group_type: String,
    pub security_enabled: bool,
    pub mail_enabled: bool,
    pub mail: Option<String>,
    pub membership_rule: Option<String>,
    pub membership_rule_processing_state: Option<String>,
    pub created_date_time: Option<String>,
    pub counts: GroupMemberCounts,
    pub members: Vec<GroupMember>,
}

// ── M18 Autonomy: the closed-loop AI SRE (watch → detect → plan → simulate →
// propose → approve → apply → verify → audit) ───────────────────────────────────
// LOCKED: autonomy ends at the inbox. The policy toggles only which signals may
// auto-ENQUEUE a PendingChange; no value here auto-applies a write. The loop's
// terminal action is POST /pending-changes (state=pending) — byte-identical to an
// MCP propose_* tool — and a human still approves the exact diff.

/// GET/PUT /autonomy/policy — per-tenant autonomy policy. `enabled` gates the whole
/// loop; each signal is toggled independently with its own severity/score floor.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AutonomyPolicy {
    pub tenant_id: Option<String>,
    pub enabled: bool,
    pub cadence_minutes: i32,
    pub scope: AutonomyScope,
    pub signals: AutonomySignals,
    pub throttle: AutonomyThrottle,
    pub note: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AutonomyScope {
    pub object_types: Vec<String>,
    pub assignment_group_allowlist: Vec<String>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AutonomySignals {
    pub drift: AutonomySignalConfig,
    pub posture_regression: AutonomySignalConfig,
    pub advisory: AutonomySignalConfig,
}

/// One signal's enqueue toggle. `min_severity` gates drift/advisory; `min_score_drop`
/// gates postureRegression. Both optional so a signal can toggle without the unused
/// floor. Emitted as explicit null (matching the .NET Web-defaults serializer).
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AutonomySignalConfig {
    pub enabled: bool,
    pub min_severity: Option<String>,
    pub min_score_drop: Option<i32>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AutonomyThrottle {
    pub max_proposals_per_run: i32,
    pub max_inflight_pending: i32,
}

/// GET /autonomy/runs(/{id}) — one scheduled loop tick, end-to-end. `watched` is the
/// delta-sync result; `detected` the signals found; `proposed` the pending-changes
/// raised (with blast-radius); `decision`/`verify` fill in as the operator acts and
/// the next tick re-checks.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AutonomyRun {
    pub run_id: String,
    pub tenant_id: Option<String>,
    pub started_utc: String,
    pub finished_utc: Option<String>,
    pub watched: AutonomyWatched,
    pub detected: Vec<AutonomyDetection>,
    pub proposed: Vec<AutonomyProposal>,
    pub decision: Option<AutonomyDecision>,
    pub verify: Option<AutonomyVerify>,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AutonomyWatched {
    pub audit_events_pulled: i32,
    pub snapshots_changed: i32,
    pub object_types: Vec<String>,
}

/// One detected signal on one object, with the field-level diff (`DriftChange[]`,
/// same shape as the inbox + drift timeline) and the base/head snapshot ids it spans.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AutonomyDetection {
    pub signal: String,
    pub object_id: String,
    pub object_name: Option<String>,
    pub severity: String,
    pub base_snapshot_id: Option<String>,
    pub head_snapshot_id: Option<String>,
    pub changes: Vec<DriftChange>,
}

/// A pending-change the loop raised, with the blast-radius it carries into the inbox.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AutonomyProposal {
    pub pending_change_id: String,
    pub kind: String,
    pub path: String,
    pub blast_radius: Option<BlastRadiusReport>,
}

/// The operator's verdict — mirrors what /pending-changes/{id}/approve|reject writes.
/// `action` is approved|rejected|failed|pending.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AutonomyDecision {
    pub pending_change_id: String,
    pub action: String,
    pub operator: Option<String>,
    pub decided_utc: Option<String>,
    pub applied_object_id: Option<String>,
    pub audit_event_id: Option<String>,
}

/// The follow-up convergence check — re-diff the new head vs baseline.
/// `residual_drift_changes == 0` ⇒ converged.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AutonomyVerify {
    pub verified_utc: String,
    pub residual_drift_changes: i32,
    pub converged: bool,
    pub verify_snapshot_id: Option<String>,
}

// ── M17 Tenant digital twin — offline graph analytics ───────────────────────────

/// Node/edge counts + freshness of the materialized twin graph.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct TwinStats {
    pub node_count: i32,
    pub edge_count: i32,
    pub built_utc: Option<String>,
    pub stale: bool,
    pub source: String,
}

/// One typed graph node, carrying the normalized props it was projected from.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TwinNode {
    pub id: String,
    pub r#type: String, // Device | User | Group | Policy | CAPolicy | Filter
    pub name: String,
    pub props: BTreeMap<String, String>,
}

/// One directed edge (memberOf | includes | excludes) with an optional qualifier.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TwinEdge {
    pub from: String,
    pub to: String,
    pub r#type: String,
    pub filter_id: Option<String>,
    pub intent: Option<String>,
}

/// A related node referenced by a finding (a group, a contending policy).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TwinRef {
    pub id: String,
    pub name: String,
    pub role: String, // includeGroup | excludeGroup | contendsWith | target
    pub count: Option<i32>,
}

/// One normalized analytics finding (uniform shape across all queries).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TwinFinding {
    pub id: Option<String>,
    pub name: Option<String>,
    pub kind: String,
    pub detail: String,
    pub refs: Vec<TwinRef>,
    pub metrics: BTreeMap<String, i32>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TwinAnalyticsResult {
    pub query: String,
    pub built_utc: Option<String>,
    pub source: String,
    pub findings: Vec<TwinFinding>,
}

/// An edge plus the node on the other end (for the neighborhood view).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TwinEdgeView {
    pub from: String,
    pub to: String,
    pub r#type: String,
    pub other_node: TwinNode,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TwinNeighborhood {
    pub node: TwinNode,
    pub inbound: Vec<TwinEdgeView>,
    pub outbound: Vec<TwinEdgeView>,
    pub members: Vec<TwinNode>,
    pub warnings: Vec<String>,
}

/// POST /twin/query — a structural typed traversal (canned vocabulary).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct TwinQueryRequest {
    pub from_type: String,
    pub edge_path: Vec<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub predicates: Option<BTreeMap<String, String>>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TwinQueryResult {
    pub nodes: Vec<TwinNode>,
    pub edges: Vec<TwinEdge>,
}

// ─── M19 continuous posture (docs/part-ii/M19-posture.md) ─────────────────────
// Extends the point-in-time `SecurityPosture` into a benchmark-mapped, trended,
// evidence-exporting compliance product. Score weights are unchanged; these graft
// control IDs on (`/posture/score`), trend the score (`/posture/trend`), package
// the evidence (`/posture/evidence-pack`), and roll gaps into a POA&M (`/posture/poam`).

/// One external control reference (CIS / OIB / Essential 8 / NIST 800-53).
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BenchmarkControlRef {
    pub framework: String, // cis | oib | e8 | nist
    pub id: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub title: Option<String>,
}

/// Per-benchmark coverage rollup: satisfied controls over controls in scope.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BenchmarkCoverage {
    pub controls_covered: i32,
    pub controls_total: i32,
    pub percent: i32,
}

/// A `ScoreCategory` with control references grafted on.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BenchmarkedScoreCategory {
    pub category: String,
    pub score: i32,
    pub max_score: i32,
    pub items: Vec<String>,
    pub controls: Vec<BenchmarkControlRef>,
}

/// A `SecurityGap` with control references grafted on.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BenchmarkedGap {
    pub severity: String, // high | medium | low
    pub category: String,
    pub description: String,
    pub controls: Vec<BenchmarkControlRef>,
}

/// GET /posture/score?benchmark= — the summary shape with control IDs + coverage.
// No `Eq`: it embeds `PostureStats`, which (matching the existing posture DTOs)
// derives only `PartialEq`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BenchmarkedPosture {
    pub benchmark: String,
    pub benchmark_version: String,
    pub score: i32,
    pub coverage: BenchmarkCoverage,
    pub breakdown: Vec<BenchmarkedScoreCategory>,
    pub gaps: Vec<BenchmarkedGap>,
    pub stats: PostureStats,
}

/// GET /posture/trend — one historical posture snapshot projected to its score.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PostureTrendPoint {
    pub captured_utc: String,
    pub score: i32,
    // BTreeMap → deterministic key order, mirroring the server's ordered emit.
    pub category_scores: std::collections::BTreeMap<String, i32>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PostureTrendDelta {
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub since: Option<String>,
    pub score_change: i32,
    pub regressions: Vec<String>,
    // M18 feed (read-only): each regressed category as a remediation candidate for the
    // autonomy loop's candidate list. Never auto-enqueued — M18 owns that decision.
    pub remediation_candidates: Vec<PostureRemediationCandidate>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PostureRemediationCandidate {
    pub category: String,
    pub score_change: i32,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub remediation_candidate_ref: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PostureTrend {
    pub benchmark: String,
    pub points: Vec<PostureTrendPoint>,
    pub delta: PostureTrendDelta,
}

/// GET /posture/poam — gap → control(s) → remediation → due date.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PoamItem {
    pub id: String,
    pub severity: String,
    pub category: String,
    pub finding: String,
    pub controls: Vec<BenchmarkControlRef>,
    pub remediation: String,
    pub source: String, // security-gap | baseline-drift
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub owner: Option<String>,
    pub opened_utc: String,
    pub due_utc: String,
    pub state: String, // open | closed
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub remediation_candidate_ref: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Poam {
    pub benchmark: String,
    pub generated_utc: String,
    pub items: Vec<PoamItem>,
}

/// POST /posture/evidence-pack — request body picking what to bundle.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct EvidencePackRequest {
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub as_of: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub benchmark: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub surfaces: Option<Vec<String>>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub formats: Option<Vec<String>>,
}

/// One artifact inside the evidence-pack zip.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct EvidenceArtifactRef {
    pub kind: String,
    pub format: String,
    pub path: String,
    pub source_engine: String,
}

/// The signed manifest of a point-in-time evidence pack. Reproducible: the same
/// `asOf` produces the same `manifestHash` (modulo render timestamp).
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct EvidencePackManifest {
    pub pack_id: String,
    pub as_of: String,
    pub benchmark: String,
    pub tenant_name: String,
    pub score: i32,
    pub zip_path: String,
    pub artifacts: Vec<EvidenceArtifactRef>,
    pub snapshot_ids: Vec<String>,
    pub manifest_hash: String,
}



// ── M20 Fleet — MSP-scale multi-tenant fan-out (docs/part-ii/M20-fleet.md) ──────
// Pattern G: the same (verb, path, body) replayed across a tenant SET; reads merged
// tenant-tagged, each write a gated M13 replay. Mirrors service/Api/Contracts.cs.

/// A known tenant profile + its live fleet-session sign-in state (GET /fleet/tenants).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct FleetTenant {
    pub tenant_id: String,
    pub profile_id: String,
    pub tenant_name: String,
    pub auth_state: AuthState,
    pub signed_in: bool,
}

/// A named, persisted set of tenantIds — a saved fan-out target. Doubles as the
/// POST /fleet/groups request body.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TenantGroup {
    pub id: String,
    pub name: String,
    pub tenant_ids: Vec<String>,
    pub golden_tenant_id: Option<String>,
    pub created_utc: String,
}

/// Per-tenant status header in a fan-out response (ok | throttled | unsupported | error | signedOut).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct FleetTenantStatus {
    pub tenant_id: String,
    pub tenant_name: String,
    pub status: String,
}

/// A `ListItem` stamped with its source tenant (fleet-tagged list row).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct FleetListItem {
    pub id: String,
    pub title: String,
    pub subtitle: String,
    pub badge: Option<String>,
    pub platform: Option<String>,
    pub modified: Option<String>,
    pub tenant_id: String,
    pub tenant_name: String,
}

/// A per-tenant error in a fan-out response — that tenant's rows are omitted, fleet returns.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct FleetError {
    pub tenant_id: String,
    pub code: String,
    pub message: String,
}

/// GET /fleet/list/{surface} — merged tenant-tagged rows + per-tenant status/errors.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct FleetListResponse {
    pub surface: String,
    pub group: String,
    pub tenants: Vec<FleetTenantStatus>,
    pub items: Vec<FleetListItem>,
    pub errors: Vec<FleetError>,
}

/// Where a campaign's baseline comes from. kind=goldenTenant reads a live tenant;
/// goldenRepo (M15 GitOps) is deferred.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GoldenRef {
    pub kind: String,
    pub tenant_id: Option<String>,
    pub repo_ref: Option<String>,
}

/// POST /fleet/campaign — broadcast a baseline to a group. dryRun previews per-tenant
/// diffs and applies nothing; otherwise each target is enqueued as a gated M13 replay.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CampaignRequest {
    pub campaign_id: Option<String>,
    pub group: String,
    pub golden: GoldenRef,
    pub surface: String,
    pub object_name: String,
    pub verb: Option<String>,
    pub dry_run: Option<bool>,
    pub gate: Option<String>,
    pub overrides: Option<std::collections::HashMap<String, serde_json::Value>>,
}

/// One target tenant's campaign outcome (success | skip | conflict | error | pending).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CampaignTenantResult {
    pub tenant_id: String,
    pub tenant_name: String,
    pub outcome: String,
    pub reason: Option<String>,
    pub pending_id: Option<String>,
    pub new_id: Option<String>,
    pub applied_fields: Vec<String>,
    pub diff: Vec<DriftChange>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CampaignSummary {
    pub success: i32,
    pub skip: i32,
    pub conflict: i32,
    pub error: i32,
    pub pending: i32,
    pub total: i32,
}

/// GET /fleet/campaign/{id} — a campaign's aggregated per-tenant outcomes.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CampaignResult {
    pub campaign_id: String,
    pub surface: String,
    pub object_name: String,
    pub dry_run: bool,
    pub results: Vec<CampaignTenantResult>,
    pub summary: CampaignSummary,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct FleetDriftField {
    pub field: String,
    pub golden: Option<serde_json::Value>,
    pub actual: Option<serde_json::Value>,
}

/// One tenant's divergence from the resolved effective golden.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct FleetDriftTenant {
    pub tenant_id: String,
    pub tenant_name: String,
    pub status: String,
    pub drift_count: i32,
    pub note: Option<String>,
    pub drifts: Vec<FleetDriftField>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct FleetDriftSummary {
    pub golden: i32,
    pub in_sync: i32,
    pub diverged: i32,
    pub error: i32,
}

/// GET /fleet/drift — per-tenant field-level divergence from the resolved golden.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct FleetDriftResponse {
    pub group: String,
    pub golden: Option<String>,
    pub surface: String,
    pub object_name: String,
    pub tenants: Vec<FleetDriftTenant>,
    pub summary: FleetDriftSummary,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct FleetPostureTenant {
    pub tenant_id: String,
    pub tenant_name: String,
    pub status: String,
    pub score: Option<i32>,
}

/// GET /fleet/posture — per-tenant posture score + fleet roll-up.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct FleetPostureResponse {
    pub group: String,
    pub average_score: i32,
    pub scored_tenants: i32,
    pub tenants: Vec<FleetPostureTenant>,
}

/// A golden template + override layers (effective = golden ⊕ groupOverride ⊕ tenantOverride).
/// Doubles as the POST /fleet/templates request body.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GoldenTemplate {
    pub id: String,
    pub name: String,
    pub surface: String,
    pub object_name: String,
    pub golden: GoldenRef,
    pub baseline_json: Option<String>,
    pub group_overrides: Option<std::collections::HashMap<String, serde_json::Value>>,
    pub tenant_overrides: Option<std::collections::HashMap<String, serde_json::Value>>,
    pub created_utc: String,
}

// ── M21 Ecosystem — shareable packs, playbooks, marketplace ───────────────────
// Mirrors service/Api/Contracts.cs. A pack adopt drives the M15 gitops plan/apply
// (over loopback), a playbook run lands native steps in the M13 inbox, and a
// marketplace install appends an M13.3 plugins.json row. No new write path.

/// A versioned M15 desired-state repo + its top-level pack.json manifest.
/// `target_surfaces` mirror Surfaces.cs keys; `repo_path` is the local working tree.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PackManifest {
    pub schema_version: String,
    pub id: String,
    pub name: String,
    pub version: String,
    pub publisher: Option<String>,
    pub description: Option<String>,
    pub target_surfaces: Vec<String>,
    pub min_sidecar: Option<String>,
    pub parameters: Vec<PackParameter>,
    pub repo_path: Option<String>,
    pub object_count: i32,
    pub signature: Option<String>,
}

/// One adoption parameter the operator binds before plan (e.g. a target group id).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PackParameter {
    pub name: String,
    #[serde(rename = "type")]
    pub r#type: String,
    pub prompt: Option<String>,
    pub required: bool,
    pub default: Option<String>,
    pub applies_to: Option<String>,
}

/// POST /packs/{id}/adopt — bind params, then plan (confirm:false) or apply.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct PackAdoptRequest {
    pub parameters: Option<HashMap<String, String>>,
    pub confirm: bool,
    pub plan_id: Option<String>,
    pub confirm_destroy: bool,
}

/// The outcome of an adopt. `phase` = plan | apply | error; `requires_m15` is true
/// when the GitOps endpoints are absent in this build. `plan`/`apply_result` are the
/// raw M15 bodies (passthrough) for whichever phase ran.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PackAdoptResult {
    pub pack_id: String,
    pub phase: String,
    pub requires_m15: bool,
    pub message: Option<String>,
    pub plan_id: Option<String>,
    pub summary: Option<String>,
    pub plan: Option<serde_json::Value>,
    pub apply_result: Option<serde_json::Value>,
}

/// A parameterized, ordered set of proposed writes. Each native step lands as a
/// PendingChange; plugin steps are pass-through. `proposer` carries playbook:{id}.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Playbook {
    pub schema_version: String,
    pub id: String,
    pub name: String,
    pub version: String,
    pub description: Option<String>,
    pub parameters: Vec<PackParameter>,
    pub steps: Vec<PlaybookStep>,
}

/// One ordered step. `tool` is a native propose_* or a plugin tool
/// (plugin_<name>_<tool>). `{{param}}` tokens in path/object_id/body are substituted.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PlaybookStep {
    pub id: String,
    pub tool: String,
    pub path: Option<String>,
    pub object_id: Option<String>,
    pub body: Option<HashMap<String, serde_json::Value>>,
    pub args: Option<HashMap<String, serde_json::Value>>,
    pub gated: Option<bool>,
    pub depends_on: Option<Vec<String>>,
}

/// POST /playbooks/{id}/run — bind params before expanding the ordered steps.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct PlaybookRunRequest {
    pub parameters: Option<HashMap<String, String>>,
}

/// The outcome of a run. `pending_change_ids` is the flat list of created change ids
/// for polling via get_change_status.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PlaybookRunResult {
    pub playbook_id: String,
    pub proposer: String,
    pub steps: Vec<PlaybookStepResult>,
    pub pending_change_ids: Vec<String>,
}

/// One step's outcome — enqueued (native), passthrough (plugin), skipped, or error.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PlaybookStepResult {
    pub step_id: String,
    pub tool: String,
    pub outcome: String, // enqueued | passthrough | skipped | error
    pub pending_change_id: Option<String>,
    pub message: Option<String>,
}

/// A curated, installable downstream MCP plugin. Installing binds `config_fields` and
/// appends a plugins.json row. `trust` flags whether the plugin's own side-effects are
/// read-only or writes-side-effecting. `installed` is true when a row already exists.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MarketplaceEntry {
    pub id: String,
    pub name: String,
    pub category: Option<String>,
    pub publisher: Option<String>,
    pub verified: bool,
    pub description: Option<String>,
    pub transport: String,
    pub url_template: Option<String>,
    pub command: Option<String>,
    pub args: Option<Vec<String>>,
    pub config_fields: Vec<MarketplaceConfigField>,
    pub exposes_tools: Option<Vec<String>>,
    pub trust: Option<String>,
    pub installed: bool,
}

/// One value the operator supplies at install. When `header` is set the bound value
/// becomes that HTTP header (applying `format`, e.g. "Bearer {{token}}").
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MarketplaceConfigField {
    pub name: String,
    #[serde(rename = "type")]
    pub r#type: String,
    pub required: bool,
    pub header: Option<String>,
    pub format: Option<String>,
}

/// POST /marketplace/{id}/install — bind the entry's configFields before writing the row.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct MarketplaceInstallRequest {
    pub config: Option<HashMap<String, String>>,
}

/// The plugins.json row written (secrets redacted in the echo).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MarketplaceInstallResult {
    pub entry_id: String,
    pub plugin_name: String,
    pub transport: String,
    pub installed: bool,
    pub message: Option<String>,
}

// ── Policy Comparison — a SettingsCatalog baseline vs a tenant policy ──────────
// Mirrors service/Api/Contracts.cs. The engine is BaselineService.CompareSettingsCatalog
// (baseline OIB/CIS catalog vs the tenant policy's live settings), grouped by verdict.

/// POST /baselines/compare body — a baseline FileName + a tenant SettingsCatalog policy id.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct BaselineCompareRequest {
    pub baseline_id: String,
    pub policy_id: String,
}

/// POST /policies/compare body — two live SettingsCatalog policy ids (A vs B).
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct PolicyCompareRequest {
    pub a_id: String,
    pub b_id: String,
}

/// One setting's A-vs-B values (both optional: missing on the B side, or extra beyond A).
/// `setting_name` is the human-readable label from the embedded Settings Catalog registry
/// (None when the id isn't in the catalog).
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BaselineSettingComparison {
    pub setting_definition_id: String,
    #[serde(default)]
    pub setting_name: Option<String>,
    pub baseline_value: Option<String>,
    pub tenant_value: Option<String>,
}

/// GET-shaped comparison result — settings grouped by verdict (matching / missing /
/// drifted / extra), same buckets as BaselineComparisonResult.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BaselineComparison {
    pub baseline_name: String,
    pub tenant_policy_id: Option<String>,
    pub tenant_policy_name: Option<String>,
    pub matching: Vec<BaselineSettingComparison>,
    pub missing: Vec<BaselineSettingComparison>,
    pub drifted: Vec<BaselineSettingComparison>,
    pub extra: Vec<BaselineSettingComparison>,
}

// ── Bulk App Assignment — assign one Assignment[] to many apps at once ─────────
// Mirrors service/Api/Contracts.cs. Loops ApplicationService.AssignApplicationAsync
// (replace-all per app); dryRun previews without writing.

/// POST /apps/assign body — the same Assignment[] applied to every app in `app_ids`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct BulkAssignRequest {
    pub app_ids: Vec<String>,
    pub assignments: Vec<Assignment>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub dry_run: Option<bool>,
}

/// One app's outcome in a bulk assign (replace-all per app is all-or-nothing).
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BulkAssignItemResult {
    pub app_id: String,
    pub ok: bool,
    pub error: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BulkAssignResult {
    pub results: Vec<BulkAssignItemResult>,
}

// ── Detection & Remediation — proactive-remediation run state ──────────────────
// Mirrors service/Api/Contracts.cs. Per-device detection/remediation state for a
// deployed remediation (DeviceHealthScript). Volatile — bypasses the M12.1 cache.

/// One device's detection + remediation state for a remediation script
/// (GET /remediation-scripts/{id}/device-states).
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DeviceRunState {
    pub device_id: String,
    pub device_name: Option<String>,
    pub detection_state: Option<String>,
    pub remediation_state: Option<String>,
    pub last_state_update_utc: Option<String>,
}

/// A remediation script's decoded detection + remediation PowerShell, for the code
/// viewer (GET /remediation-scripts/{id}/scripts). Either body may be None (detection-only,
/// or a global/Microsoft-managed script whose bytes Graph doesn't return).
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct RemediationScriptContent {
    pub script_id: String,
    pub script_name: Option<String>,
    pub detection: Option<String>,
    pub remediation: Option<String>,
}
