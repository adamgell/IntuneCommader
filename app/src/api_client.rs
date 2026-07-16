//! Thin HTTP client to the local .NET service (contract/openapi.yaml).
//! Replace with a generated client (progenitor) once codegen is wired.
//!
//! Blocking on purpose: the Reactor UI drives these from use_resource /
//! use_mutation fetchers, which run on a background thread and want a plain
//! synchronous call — no tokio runtime to juggle on the UI thread.

use std::sync::OnceLock;
use std::time::Duration;

use api_types::{
    AutonomyPolicy, AutonomyRun,
    Assignment, AuditEvent, BaselineComparison, BaselineCompareRequest, BenchmarkedPosture,
    BlastRadiusReport, BulkAssignRequest, BulkAssignResult, BulkDeviceActionResult, CacheEntryStatus, CacheSummary,
    CaDetail, CaPolicyListItem, CampaignRequest, CampaignResult, CaSummary, DeviceActionInfo, DeviceActionRecord,
    DeviceRunState, DriftChange, DriftObject, DriftRecord, EvidencePackManifest, EvidencePackRequest,
    FleetDriftResponse, FleetListResponse, FleetPostureResponse,
    FleetTenant, GitOpsApplyRequest, GitOpsApplyResult, GitOpsManifest, GitOpsPlan, GitOpsPlanRequest,
    GitOpsPullRequest, GitOpsStatus, GroupDetail, ImportRestoreResult, ListItem, MaesterRunResult,
    MaesterRunState, MaesterStatus, MarketplaceEntry, MarketplaceInstallRequest,
    MarketplaceInstallResult, NewProfile, PackAdoptRequest, PackAdoptResult, PackManifest, PendingChange,
    Playbook, PlaybookRunRequest, PlaybookRunResult, Poam, PolicyCompareRequest, PostureTrend, SearchResult, SecurityPosture,
    RemediationScriptContent, SimulateRequest, SnapshotSummary, SyncStatus, TenantGroup, TenantProfileSummary,
    TwinAnalyticsResult, TwinNeighborhood, TwinQueryRequest, TwinQueryResult, TwinStats,
};

const SERVICE_URL: &str = "http://127.0.0.1:5099";

// Bound every request so a down or hung sidecar surfaces as an error in the UI
// (the resource's `.error(..)` branch) instead of an eternal loading spinner.
const REQUEST_TIMEOUT: Duration = Duration::from_secs(15);

/// Turn a transport error from a sidecar call into a user-facing message that
/// names the right failure class — "slow" vs "unreachable" vs "bad response" —
/// instead of blaming the network for everything. Use as `.map_err(service_err)`.
pub fn service_err(e: reqwest::Error) -> String {
    if e.is_timeout() {
        "The service is taking too long to respond — it may be busy. Try again.".to_string()
    } else if e.is_connect() {
        "Couldn't reach the service — is the sidecar running?".to_string()
    } else if e.is_decode() {
        "The service returned an unexpected response.".to_string()
    } else {
        format!("Service error: {e}")
    }
}

/// Extract a non-2xx sidecar body's `{ "error": ... }` message verbatim (falling back
/// to the status line). Used by the write actions that surface the server's reason
/// (bad-param 400, signed-out 409) rather than a generic failure.
fn body_error(resp: reqwest::blocking::Response) -> String {
    let status = resp.status();
    let body = resp.text().unwrap_or_default();
    serde_json::from_str::<serde_json::Value>(&body)
        .ok()
        .and_then(|v| v.get("error").and_then(|e| e.as_str()).map(str::to_string))
        .unwrap_or_else(|| {
            format!("{} {}", status.as_u16(), status.canonical_reason().unwrap_or("error"))
        })
}

/// Process-wide client to the local sidecar. Lazily constructed on first use
/// (off the UI thread), so the internal blocking runtime is never created
/// inside an async context.
pub fn api() -> &'static ApiClient {
    static CLIENT: OnceLock<ApiClient> = OnceLock::new();
    CLIENT.get_or_init(|| ApiClient::new(SERVICE_URL))
}

pub struct ApiClient {
    base: String,
    http: reqwest::blocking::Client,
}

impl ApiClient {
    pub fn new(base: impl Into<String>) -> Self {
        let http = reqwest::blocking::Client::builder()
            .timeout(REQUEST_TIMEOUT)
            .build()
            .expect("build blocking HTTP client");
        Self { base: base.into(), http }
    }

    pub fn health(&self) -> reqwest::Result<SyncStatus> {
        self.http.get(format!("{}/health", self.base)).send()?.json()
    }

    pub fn profiles(&self) -> reqwest::Result<Vec<TenantProfileSummary>> {
        self.http.get(format!("{}/profiles", self.base)).send()?.json()
    }

    pub fn activate_profile(&self, id: &str) -> reqwest::Result<()> {
        self.http
            .post(format!("{}/profiles/{}/activate", self.base, id))
            .send()?
            .error_for_status()?;
        Ok(())
    }

    // ── Profile lifecycle (M11): add / edit / delete saved tenant profiles ──
    pub fn create_profile(&self, p: &NewProfile) -> reqwest::Result<()> {
        self.http
            .post(format!("{}/profiles", self.base))
            .json(p)
            .send()?
            .error_for_status()?;
        Ok(())
    }

    pub fn update_profile(&self, id: &str, p: &NewProfile) -> reqwest::Result<()> {
        self.http
            .patch(format!("{}/profiles/{}", self.base, id))
            .json(p)
            .send()?
            .error_for_status()?;
        Ok(())
    }

    pub fn delete_profile(&self, id: &str) -> reqwest::Result<()> {
        self.http
            .delete(format!("{}/profiles/{}", self.base, id))
            .send()?
            .error_for_status()?;
        Ok(())
    }

    pub fn sign_in(&self) -> reqwest::Result<()> {
        self.http
            .post(format!("{}/auth/signin", self.base))
            .send()?
            .error_for_status()?;
        Ok(())
    }

    pub fn sign_out(&self) -> reqwest::Result<()> {
        self.http
            .post(format!("{}/auth/signout", self.base))
            .send()?
            .error_for_status()?;
        Ok(())
    }

    pub fn sync(&self) -> reqwest::Result<()> {
        self.http
            .post(format!("{}/sync", self.base))
            .send()?
            .error_for_status()?;
        Ok(())
    }

    pub fn audit(&self, q: Option<&str>) -> reqwest::Result<Vec<AuditEvent>> {
        let mut req = self.http.get(format!("{}/audit", self.base));
        if let Some(q) = q {
            req = req.query(&[("q", q)]);
        }
        req.send()?.json()
    }

    pub fn objects(&self) -> reqwest::Result<Vec<DriftObject>> {
        self.http.get(format!("{}/objects", self.base)).send()?.json()
    }

    // ── Generic CRUD over a resource path (e.g. "/scope-tags") ──────────────
    // Every management surface is driven through these by path (the feature
    // registry carries the path), so adding a surface is a registry line only.

    // List → normalized `Vec<ListItem>`. 409 (signed out) → empty (sign-in state).
    pub fn get_list(&self, path: &str) -> reqwest::Result<Vec<ListItem>> {
        let resp = self.http.get(format!("{}{}", self.base, path)).send()?;
        if resp.status() == reqwest::StatusCode::CONFLICT {
            return Ok(Vec::new());
        }
        resp.error_for_status()?.json()
    }

    // Full object JSON for one item — the editor's view/edit source.
    pub fn get_detail(&self, path: &str, id: &str) -> reqwest::Result<String> {
        self.http
            .get(format!("{}{}/{}", self.base, path, id))
            .send()?
            .error_for_status()?
            .text()
    }

    // The deep settingInstance tree for a Settings Catalog policy, fetched apart
    // from the policy metadata. 409 (signed out) → empty array.
    pub fn get_settings(&self, id: &str) -> reqwest::Result<String> {
        let resp = self
            .http
            .get(format!("{}/settings-catalog/{}/settings", self.base, id))
            .send()?;
        if resp.status() == reqwest::StatusCode::CONFLICT {
            return Ok("[]".to_string());
        }
        resp.error_for_status()?.text()
    }

    // Scored security posture (0-100 + breakdown + gaps). 409 (signed out) → empty.
    pub fn security_posture(&self) -> reqwest::Result<SecurityPosture> {
        let resp = self
            .http
            .get(format!("{}/security-posture/summary", self.base))
            .send()?;
        if resp.status() == reqwest::StatusCode::CONFLICT {
            return Ok(SecurityPosture::default());
        }
        resp.error_for_status()?.json()
    }

    // ── Maester security-test integration ──────────────────────────────────
    // Prerequisite probe (PowerShell 7 + Maester module). Never 409s.
    pub fn maester_status(&self) -> reqwest::Result<MaesterStatus> {
        self.http
            .get(format!("{}/maester/status", self.base))
            .send()?
            .error_for_status()?
            .json()
    }

    // Latest stored run for the active tenant. 409 (signed out) → empty.
    pub fn maester_results(&self) -> reqwest::Result<MaesterRunResult> {
        let resp = self.http.get(format!("{}/maester/results", self.base)).send()?;
        if resp.status() == reqwest::StatusCode::CONFLICT {
            return Ok(MaesterRunResult::default());
        }
        resp.error_for_status()?.json()
    }

    // Start a run (optionally tag-filtered). Fire-and-forget: the sidecar returns 202
    // immediately with the run state and executes in the background, so this no longer
    // needs the old 20-min timeout override. Poll maester_run_status() until it leaves
    // Running, then maester_results().
    pub fn maester_run(&self, tags: Option<Vec<String>>) -> reqwest::Result<MaesterRunState> {
        // Omit `tags` entirely when not provided — the contract's field is optional but
        // not nullable, so sending {"tags": null} would drift from the schema.
        let body = match &tags {
            Some(t) => serde_json::json!({ "tags": t }),
            None => serde_json::json!({}),
        };
        let resp = self.http.post(format!("{}/maester/run", self.base)).json(&body).send()?;
        if resp.status() == reqwest::StatusCode::CONFLICT {
            // The server returns 409 for two distinct cases: a run is already in progress,
            // or we're signed out. Disambiguate via the status endpoint — a genuinely
            // Running run → report it so the caller just polls; otherwise fall through and
            // surface the 409 as an error so the UI isn't a silent no-op.
            if let Ok(state) = self.maester_run_status() {
                if state.status == "Running" {
                    return Ok(state);
                }
            }
        }
        resp.error_for_status()?.json()
    }

    // Cheap in-memory status of the current/last run (no pwsh probe). Poll while Running.
    pub fn maester_run_status(&self) -> reqwest::Result<MaesterRunState> {
        self.http
            .get(format!("{}/maester/run/status", self.base))
            .send()?
            .error_for_status()?
            .json()
    }

    // Readable, GUID-resolved Conditional Access summary for one policy.
    pub fn ca_summary(&self, id: &str) -> reqwest::Result<CaSummary> {
        self.http
            .get(format!("{}/conditional-access/{}/summary", self.base, id))
            .send()?
            .error_for_status()?
            .json()
    }

    // Rich CA grid rows. 409 (signed out) → empty.
    pub fn ca_list(&self) -> reqwest::Result<Vec<CaPolicyListItem>> {
        let resp = self
            .http
            .get(format!("{}/conditional-access/list", self.base))
            .send()?;
        if resp.status() == reqwest::StatusCode::CONFLICT {
            return Ok(Vec::new());
        }
        resp.error_for_status()?.json()
    }

    // Full CA policy detail (include/exclude conditions, GUIDs resolved).
    pub fn ca_detail(&self, id: &str) -> reqwest::Result<CaDetail> {
        self.http
            .get(format!("{}/conditional-access/{}/detail", self.base, id))
            .send()?
            .error_for_status()?
            .json()
    }

    // GET /groups/{id}/detail — properties + member counts + member list for the
    // bespoke Groups workspace. 409 (signed out) → empty detail.
    pub fn group_detail(&self, id: &str) -> reqwest::Result<GroupDetail> {
        let resp = self
            .http
            .get(format!("{}/groups/{}/detail", self.base, id))
            .send()?;
        if resp.status() == reqwest::StatusCode::CONFLICT {
            return Ok(GroupDetail::default());
        }
        resp.error_for_status()?.json()
    }

    pub fn create_item(&self, path: &str, body: String) -> reqwest::Result<()> {
        self.http
            .post(format!("{}{}", self.base, path))
            .header("content-type", "application/json")
            .body(body)
            .send()?
            .error_for_status()?;
        Ok(())
    }

    pub fn update_item(&self, path: &str, id: &str, body: String) -> reqwest::Result<()> {
        self.http
            .patch(format!("{}{}/{}", self.base, path, id))
            .header("content-type", "application/json")
            .body(body)
            .send()?
            .error_for_status()?;
        Ok(())
    }

    pub fn delete_item(&self, path: &str, id: &str) -> reqwest::Result<()> {
        self.http
            .delete(format!("{}{}/{}", self.base, path, id))
            .send()?
            .error_for_status()?;
        Ok(())
    }

    // ── M6 safe-write rails: diff-preview, snapshot-on-write, restore ───────

    // Field-level diff of the current object body vs the pending edit, computed by
    // the sidecar's drift engine (`JsonDrift.Diff`) so the confirm panel shows the
    // exact change set a save will apply. `before` is "{}" for a brand-new object.
    pub fn preview_diff(&self, before: &str, after: &str) -> reqwest::Result<Vec<DriftChange>> {
        self.http
            .post(format!("{}/preview-diff", self.base))
            .json(&serde_json::json!({ "before": before, "after": after }))
            .send()?
            .error_for_status()?
            .json()
    }

    // Capture the body we just wrote into the append-only time-machine, so the edit
    // is itself restorable. Dedups server-side on content hash (a no-op write adds
    // nothing). `object_type` is the surface path (e.g. "/scope-tags").
    pub fn capture_snapshot(
        &self,
        object_type: &str,
        object_id: &str,
        object_name: Option<&str>,
        body: &str,
    ) -> reqwest::Result<()> {
        self.http
            .post(format!("{}/snapshots", self.base))
            .json(&serde_json::json!({
                "objectType": object_type,
                "objectId": object_id,
                "objectName": object_name,
                "bodyJson": body,
            }))
            .send()?
            .error_for_status()?;
        Ok(())
    }

    // An object's snapshot history (newest first) for the restore/undo picker.
    pub fn list_snapshots(&self, object_id: &str) -> reqwest::Result<Vec<SnapshotSummary>> {
        self.http
            .get(format!("{}/snapshots", self.base))
            .query(&[("objectId", object_id)])
            .send()?
            .error_for_status()?
            .json()
    }

    // ── Assignments (normalized) ───────────────────────────────────────────
    pub fn get_assignments(&self, path: &str, id: &str) -> reqwest::Result<Vec<Assignment>> {
        let resp = self
            .http
            .get(format!("{}{}/{}/assignments", self.base, path, id))
            .send()?;
        if resp.status() == reqwest::StatusCode::CONFLICT {
            return Ok(Vec::new());
        }
        resp.error_for_status()?.json()
    }

    pub fn set_assignments(&self, path: &str, id: &str, items: Vec<Assignment>) -> reqwest::Result<()> {
        self.http
            .post(format!("{}{}/{}/assignments", self.base, path, id))
            .json(&items)
            .send()?
            .error_for_status()?;
        Ok(())
    }

    // Generic POST to an "action" endpoint (export, generate, cleanup, preview,
    // apply…) returning the response body as text — a result message or a JSON
    // payload the caller renders verbatim. Optional JSON body. 409 (signed out)
    // surfaces as an error via the caller's `service_err` mapping.
    pub fn post_text(&self, path: &str, body: Option<String>) -> reqwest::Result<String> {
        let mut req = self.http.post(format!("{}{}", self.base, path));
        if let Some(b) = body {
            req = req.header("content-type", "application/json").body(b);
        }
        req.send()?.error_for_status()?.text()
    }

    // ── M8 bulk & lifecycle: backup export + import dry-run preview ─────────
    // GET /export → a backup .zip (bytes) of the signed-in tenant's core surfaces.
    pub fn export_backup(&self) -> reqwest::Result<Vec<u8>> {
        let resp = self.http.get(format!("{}/export", self.base)).send()?;
        Ok(resp.error_for_status()?.bytes()?.to_vec())
    }

    // GET /conditional-access/pptx → a .pptx (bytes) documenting CA policies,
    // one slide per policy (Core's ConditionalAccessPptExportService).
    pub fn export_ca_pptx(&self) -> reqwest::Result<Vec<u8>> {
        let resp = self.http.get(format!("{}/conditional-access/pptx", self.base)).send()?;
        Ok(resp.error_for_status()?.bytes()?.to_vec())
    }

    // POST /import (dry-run) → JSON preview of what a backup .zip contains.
    pub fn import_preview(&self, zip: Vec<u8>) -> reqwest::Result<String> {
        self.http
            .post(format!("{}/import", self.base))
            .header("content-type", "application/zip")
            .body(zip)
            .send()?
            .error_for_status()?
            .text()
    }

    // POST /import?dryRun=false → LIVE restore of a backup .zip into the tenant
    // (every object created fresh). Long-running — overrides the 15s default timeout.
    pub fn import_apply(&self, zip: Vec<u8>) -> reqwest::Result<ImportRestoreResult> {
        self.http
            .post(format!("{}/import?dryRun=false", self.base))
            .timeout(Duration::from_secs(10 * 60))
            .header("content-type", "application/zip")
            .body(zip)
            .send()?
            .error_for_status()?
            .json()
    }

    // GET /assignment-explorer?mode=&group= → assignment report rows for one mode
    // (all / all-users / all-devices / unassigned / empty-groups / failed / group).
    // A full scan can take a while — overrides the default timeout. 409 → empty.
    pub fn assignment_explorer(&self, mode: &str, group: &str) -> reqwest::Result<Vec<ListItem>> {
        let resp = self
            .http
            .get(format!("{}/assignment-explorer", self.base))
            .query(&[("mode", mode), ("group", group)])
            .timeout(Duration::from_secs(5 * 60))
            .send()?;
        if resp.status() == reqwest::StatusCode::CONFLICT {
            return Ok(Vec::new());
        }
        resp.error_for_status()?.json()
    }

    // GET /assignment-explorer/report?mode=&group=&format=html|csv → report bytes.
    pub fn assignment_report(&self, mode: &str, group: &str, format: &str) -> reqwest::Result<Vec<u8>> {
        let resp = self
            .http
            .get(format!("{}/assignment-explorer/report", self.base))
            .query(&[("mode", mode), ("group", group), ("format", format)])
            .timeout(Duration::from_secs(5 * 60))
            .send()?;
        Ok(resp.error_for_status()?.bytes()?.to_vec())
    }

    // ── M14 managed-device actions ─────────────────────────────────────────
    // GET /managed-devices/actions → the verb catalog (drives the action buttons +
    // confirm gating). 409 (signed out) → empty.
    pub fn list_device_actions(&self) -> reqwest::Result<Vec<DeviceActionInfo>> {
        let resp = self
            .http
            .get(format!("{}/managed-devices/actions", self.base))
            .send()?;
        if resp.status() == reqwest::StatusCode::CONFLICT {
            return Ok(Vec::new());
        }
        resp.error_for_status()?.json()
    }

    // POST /managed-devices/{id}/actions/{action} — run one action against one
    // device. `confirm` carries the typed-confirm token (the device name, for
    // destructive verbs); `params` overrides the server's safe DefaultBody (send
    // None to keep it). On a non-2xx the sidecar's `{ "error": ... }` body is
    // surfaced VERBATIM (bad-confirm 400, destructive-gated 403, signed-out 409) so
    // the typed-confirm panel shows the real reason — hence `Result<(), String>`.
    pub fn run_device_action(
        &self,
        id: &str,
        action: &str,
        confirm: Option<&str>,
        params: Option<serde_json::Value>,
    ) -> Result<(), String> {
        let resp = self
            .http
            .post(format!("{}/managed-devices/{}/actions/{}", self.base, id, action))
            .json(&serde_json::json!({ "confirm": confirm, "parameters": params }))
            .send()
            .map_err(service_err)?;
        if resp.status().is_success() {
            return Ok(());
        }
        let status = resp.status();
        let body = resp.text().unwrap_or_default();
        Err(serde_json::from_str::<serde_json::Value>(&body)
            .ok()
            .and_then(|v| v.get("error").and_then(|e| e.as_str()).map(str::to_string))
            .unwrap_or_else(|| {
                format!("{} {}", status.as_u16(), status.canonical_reason().unwrap_or("error"))
            }))
    }

    // POST /managed-devices/actions/{action} — run one action against MANY devices.
    // `confirm` carries the case-SENSITIVE sweep token "{action} {count}" for destructive
    // verbs (None for reversible — distinct from the single-action device-name token).
    // On 200 the sidecar returns per-device outcomes; on a non-2xx its `{ "error": ... }`
    // body surfaces VERBATIM (over-cap / bad-token 400, destructive-gated 403, signed-out
    // 409) so the bulk bar shows the real reason — hence `Result<_, String>`.
    pub fn post_bulk_device_action(
        &self,
        action: &str,
        ids: &[String],
        confirm: Option<&str>,
    ) -> Result<Vec<BulkDeviceActionResult>, String> {
        let resp = self
            .http
            .post(format!("{}/managed-devices/actions/{}", self.base, action))
            .json(&serde_json::json!({ "deviceIds": ids, "confirm": confirm }))
            .send()
            .map_err(service_err)?;
        if resp.status().is_success() {
            return resp.json().map_err(service_err);
        }
        let status = resp.status();
        let body = resp.text().unwrap_or_default();
        Err(serde_json::from_str::<serde_json::Value>(&body)
            .ok()
            .and_then(|v| v.get("error").and_then(|e| e.as_str()).map(str::to_string))
            .unwrap_or_else(|| {
                format!("{} {}", status.as_u16(), status.canonical_reason().unwrap_or("error"))
            }))
    }

    // GET /managed-devices/{id}/actions → the device's action history (Graph
    // deviceActionResults, projected to DeviceActionRecord rows). Volatile — the sidecar
    // reads it straight from Graph (no blob cache). 409 (signed out) → empty.
    pub fn get_device_action_history(&self, id: &str) -> reqwest::Result<Vec<DeviceActionRecord>> {
        let resp = self
            .http
            .get(format!("{}/managed-devices/{}/actions", self.base, id))
            .send()?;
        if resp.status() == reqwest::StatusCode::CONFLICT {
            return Ok(Vec::new());
        }
        resp.error_for_status()?.json()
    }

    // ── M12.1 cache-dev (Cache Sync) — inspect / warm / evict the blob cache ────
    // GET /cache → per-key LIST status for the active tenant. 409 (signed out) → empty.
    pub fn cache_status(&self) -> reqwest::Result<Vec<CacheEntryStatus>> {
        let resp = self.http.get(format!("{}/cache", self.base)).send()?;
        if resp.status() == reqwest::StatusCode::CONFLICT {
            return Ok(Vec::new());
        }
        resp.error_for_status()?.json()
    }

    // GET /cache/summary → header summary (availability + last warm + counts).
    // 409 (signed out) → default (available=false, zero counts).
    pub fn cache_summary(&self) -> reqwest::Result<CacheSummary> {
        let resp = self.http.get(format!("{}/cache/summary", self.base)).send()?;
        if resp.status() == reqwest::StatusCode::CONFLICT {
            // Signed out is NOT "cache disabled" — return an available sentinel so the
            // screen doesn't show the wrong "cache unavailable" banner; the signed-out
            // empty state is driven by auth, not this flag.
            return Ok(CacheSummary { available: true, ..Default::default() });
        }
        resp.error_for_status()?.json()
    }

    // POST /cache/warm?force= → fire-and-forget warm (202). 409 (signed out) → error.
    pub fn cache_warm(&self, force: bool) -> reqwest::Result<()> {
        self.http
            .post(format!("{}/cache/warm", self.base))
            .query(&[("force", force)])
            .send()?
            .error_for_status()?;
        Ok(())
    }

    // POST /cache/evict?key= → evict one key, or all tenant keys when None (204).
    pub fn cache_evict(&self, key: Option<&str>) -> reqwest::Result<()> {
        let mut req = self.http.post(format!("{}/cache/evict", self.base));
        if let Some(k) = key {
            req = req.query(&[("key", k)]);
        }
        req.send()?.error_for_status()?;
        Ok(())
    }

    // ── M15 Policy-as-Code / GitOps (pull → plan → apply) ──────────────────
    // pull writes a normalized repo tree + manifest; plan is a read-only change
    // set; apply is gated on confirm + the reviewed planId (the same M6 rail,
    // lifted to tree scope). Signed-out (409) surfaces via the caller's
    // `service_err` mapping. The plan renders in the existing M6 diff panel
    // because each object carries the same DriftChange[] shape.
    pub fn gitops_pull(&self, req: &GitOpsPullRequest) -> reqwest::Result<GitOpsManifest> {
        self.http
            .post(format!("{}/gitops/pull", self.base))
            .json(req)
            .send()?
            .error_for_status()?
            .json()
    }

    pub fn gitops_plan(&self, req: &GitOpsPlanRequest) -> reqwest::Result<GitOpsPlan> {
        self.http
            .post(format!("{}/gitops/plan", self.base))
            .json(req)
            .send()?
            .error_for_status()?
            .json()
    }

    pub fn gitops_apply(&self, req: &GitOpsApplyRequest) -> reqwest::Result<GitOpsApplyResult> {
        self.http
            .post(format!("{}/gitops/apply", self.base))
            .json(req)
            .send()?
            .error_for_status()?
            .json()
    }

    pub fn gitops_status(&self, repo_path: &str) -> reqwest::Result<GitOpsStatus> {
        self.http
            .get(format!("{}/gitops/status", self.base))
            .query(&[("repoPath", repo_path)])
            .send()?
            .error_for_status()?
            .json()
    }

    // ── M16 Foresight: blast-radius simulation ─────────────────────────────
    // POST /simulate — pre-flight a proposed write (who does this hit?) without
    // touching Graph. Drives the M6 confirm gate; the same report rides on the
    // M13 inbox row (PendingChange::blast_radius). Signed-out (409) → service error.
    pub fn simulate(&self, req: &SimulateRequest) -> reqwest::Result<BlastRadiusReport> {
        self.http
            .post(format!("{}/simulate", self.base))
            .json(req)
            .send()?
            .error_for_status()?
            .json()
    }

    // ── M17 Tenant digital twin — offline graph analytics ──────────────────
    // GET /twin/stats → node/edge counts + freshness. The offline path never
    // 409s (returns source="empty" / zero counts when signed out or cold).
    pub fn twin_stats(&self) -> reqwest::Result<TwinStats> {
        self.http.get(format!("{}/twin/stats", self.base)).send()?.error_for_status()?.json()
    }

    // POST /twin/rebuild?warm= — re-materialize the twin. warm=false rebuilds
    // offline from cache+store; warm=true refreshes from Graph first (sign-in
    // gated → 409, surfaced via service_err).
    pub fn twin_rebuild(&self, warm: bool) -> reqwest::Result<TwinStats> {
        self.http
            .post(format!("{}/twin/rebuild", self.base))
            .query(&[("warm", if warm { "true" } else { "false" })])
            .send()?
            .error_for_status()?
            .json()
    }

    // GET /twin/analytics/{query} — one of the seven canned analytics. Signed
    // out / cold cache → 200 with empty findings (source="empty").
    pub fn twin_analytics(&self, query: &str) -> reqwest::Result<TwinAnalyticsResult> {
        self.http
            .get(format!("{}/twin/analytics/{}", self.base, query))
            .send()?
            .error_for_status()?
            .json()
    }

    // GET /twin/node/{id} — the neighborhood of a finding node. 404 (unknown /
    // not in the current twin) → None so the detail pane shows an empty state.
    pub fn twin_node(&self, id: &str) -> reqwest::Result<Option<TwinNeighborhood>> {
        let resp = self.http.get(format!("{}/twin/node/{}", self.base, id)).send()?;
        if resp.status() == reqwest::StatusCode::NOT_FOUND {
            return Ok(None);
        }
        resp.error_for_status()?.json().map(Some)
    }

    // POST /twin/query — a structural typed traversal (from-type + edge-path). Offline
    // like the rest of the twin: signed out / cold cache → 200 with empty nodes+edges.
    pub fn twin_query(&self, req: &TwinQueryRequest) -> reqwest::Result<TwinQueryResult> {
        self.http
            .post(format!("{}/twin/query", self.base))
            .json(req)
            .send()?
            .error_for_status()?
            .json()
    }

    // ── M18 Autonomy — the per-tenant policy + the closed-loop run log ─────
    // GET /autonomy/policy → the effective policy (safe Defaults when unset).
    // 409 (signed out) surfaces via service_err.
    pub fn autonomy_policy(&self) -> reqwest::Result<AutonomyPolicy> {
        self.http.get(format!("{}/autonomy/policy", self.base)).send()?.error_for_status()?.json()
    }

    // PUT /autonomy/policy — returns the server-NORMALIZED policy (cadence/throttle
    // clamped, tenantId stamped); the caller must adopt the returned value.
    pub fn set_autonomy_policy(&self, policy: &AutonomyPolicy) -> reqwest::Result<AutonomyPolicy> {
        self.http
            .put(format!("{}/autonomy/policy", self.base))
            .json(policy)
            .send()?
            .error_for_status()?
            .json()
    }

    // GET /autonomy/runs — the loop run-log (newest first). Not auth-gated
    // (reads stored rows); empty when none.
    pub fn autonomy_runs(&self) -> reqwest::Result<Vec<AutonomyRun>> {
        self.http.get(format!("{}/autonomy/runs", self.base)).send()?.error_for_status()?.json()
    }

    // GET /autonomy/runs/{id} — one run's full watch→verify chain. 404 unknown.
    pub fn autonomy_run(&self, id: &str) -> reqwest::Result<AutonomyRun> {
        self.http
            .get(format!("{}/autonomy/runs/{}", self.base, id))
            .send()?
            .error_for_status()?
            .json()
    }

    // ── M19 continuous posture — benchmark score / trend / POA&M / evidence ─
    // GET /posture/score?benchmark= — benchmark-mapped score + coverage. Also
    // self-snapshots a trend point server-side. 409 (signed out) → service_err.
    pub fn posture_score(&self, benchmark: &str) -> reqwest::Result<BenchmarkedPosture> {
        self.http
            .get(format!("{}/posture/score", self.base))
            .query(&[("benchmark", benchmark)])
            .send()?
            .error_for_status()?
            .json()
    }

    // GET /posture/trend?benchmark= — the scored trend from the snapshot store.
    // Not auth-gated (reads local snapshots); empty points until ≥1 score call.
    pub fn posture_trend(&self, benchmark: &str) -> reqwest::Result<PostureTrend> {
        self.http
            .get(format!("{}/posture/trend", self.base))
            .query(&[("benchmark", benchmark)])
            .send()?
            .error_for_status()?
            .json()
    }

    // GET /posture/poam?benchmark= — open gaps → controls → remediation → due.
    // 409 (signed out) → service_err.
    pub fn posture_poam(&self, benchmark: &str) -> reqwest::Result<Poam> {
        self.http
            .get(format!("{}/posture/poam", self.base))
            .query(&[("benchmark", benchmark)])
            .send()?
            .error_for_status()?
            .json()
    }

    // POST /posture/evidence-pack — package a point-in-time evidence zip and
    // return its manifest. Read/report action (no Graph write, no M13 inbox).
    pub fn posture_evidence_pack(
        &self,
        req: &EvidencePackRequest,
    ) -> reqwest::Result<EvidencePackManifest> {
        self.http
            .post(format!("{}/posture/evidence-pack", self.base))
            .json(req)
            .send()?
            .error_for_status()?
            .json()
    }

    // ── M20 Fleet — MSP-scale multi-tenant fan-out ─────────────────────────
    // GET /fleet/tenants — known profiles + fleet-session sign-in state (always 200).
    pub fn fleet_tenants(&self) -> reqwest::Result<Vec<FleetTenant>> {
        self.http.get(format!("{}/fleet/tenants", self.base)).send()?.error_for_status()?.json()
    }

    // GET /fleet/groups — saved fan-out target sets (always 200, empty when none).
    pub fn fleet_groups(&self) -> reqwest::Result<Vec<TenantGroup>> {
        self.http.get(format!("{}/fleet/groups", self.base)).send()?.error_for_status()?.json()
    }

    // POST /fleet/groups — upsert a saved tenant group (id/createdUtc server-assigned).
    pub fn fleet_upsert_group(&self, group: &TenantGroup) -> reqwest::Result<TenantGroup> {
        self.http
            .post(format!("{}/fleet/groups", self.base))
            .json(group)
            .send()?
            .error_for_status()?
            .json()
    }

    // GET /fleet/list/{surface}?group= — fan-out LIST across the group, tenant-tagged.
    // Never 409; per-tenant failures ride inside the 200 body as status/error rows.
    pub fn fleet_list(&self, surface: &str, group: &str) -> reqwest::Result<FleetListResponse> {
        self.http
            .get(format!("{}/fleet/list/{}", self.base, surface))
            .query(&[("group", group)])
            .send()?
            .error_for_status()?
            .json()
    }

    // POST /fleet/campaign — broadcast a golden baseline to a group. dryRun previews
    // per-tenant diffs and applies nothing; a live run enqueues one gated M13 replay
    // per target (outcome="pending"), never a direct bulk write.
    pub fn fleet_campaign(&self, req: &CampaignRequest) -> reqwest::Result<CampaignResult> {
        self.http
            .post(format!("{}/fleet/campaign", self.base))
            .json(req)
            .send()?
            .error_for_status()?
            .json()
    }

    // GET /fleet/drift?group=&surface=&objectName=&golden= — per-tenant divergence from
    // the resolved effective golden (golden ⊕ stored template overrides). Accepted
    // overrides are folded into the golden server-side, so they read as inSync, not drift.
    pub fn fleet_drift(
        &self,
        group: &str,
        surface: &str,
        object_name: &str,
        golden: Option<&str>,
    ) -> reqwest::Result<FleetDriftResponse> {
        let mut q: Vec<(&str, &str)> =
            vec![("group", group), ("surface", surface), ("objectName", object_name)];
        if let Some(g) = golden {
            q.push(("golden", g));
        }
        self.http
            .get(format!("{}/fleet/drift", self.base))
            .query(&q)
            .send()?
            .error_for_status()?
            .json()
    }

    // GET /fleet/posture?group= — per-tenant posture score + fleet roll-up (average over
    // scored tenants). Per-tenant failures ride inside the 200 body as non-"ok" status rows.
    pub fn fleet_posture(&self, group: &str) -> reqwest::Result<FleetPostureResponse> {
        self.http
            .get(format!("{}/fleet/posture", self.base))
            .query(&[("group", group)])
            .send()?
            .error_for_status()?
            .json()
    }

    // ── M21 Ecosystem — packs / playbooks / marketplace ────────────────────
    // GET /packs — disk-catalog pack manifests (no auth; [] when absent).
    pub fn packs(&self) -> reqwest::Result<Vec<PackManifest>> {
        self.http.get(format!("{}/packs", self.base)).send()?.error_for_status()?.json()
    }

    // POST /packs/{id}/adopt — plan (confirm:false) or apply (confirm:true + planId)
    // over the M15 gitops routes. Surfaces the sidecar's {error}/409/501 body verbatim
    // (adopt drives a live plan, so signed-out matters) → Result<_, String>.
    pub fn adopt_pack(&self, id: &str, req: &PackAdoptRequest) -> Result<PackAdoptResult, String> {
        let resp = self
            .http
            .post(format!("{}/packs/{}/adopt", self.base, id))
            .json(req)
            .send()
            .map_err(service_err)?;
        if resp.status().is_success() {
            return resp.json().map_err(service_err);
        }
        Err(body_error(resp))
    }

    // GET /playbooks — disk-catalog playbook definitions.
    pub fn playbooks(&self) -> reqwest::Result<Vec<Playbook>> {
        self.http.get(format!("{}/playbooks", self.base)).send()?.error_for_status()?.json()
    }

    // POST /playbooks/{id}/run — expand + enqueue the ordered steps; native steps land
    // as gated PendingChanges in the M13 inbox (per-step outcome in the result).
    pub fn run_playbook(&self, id: &str, req: &PlaybookRunRequest) -> reqwest::Result<PlaybookRunResult> {
        self.http
            .post(format!("{}/playbooks/{}/run", self.base, id))
            .json(req)
            .send()?
            .error_for_status()?
            .json()
    }

    // GET /marketplace — curated installable MCP plugins (built-ins authoritative).
    pub fn marketplace(&self) -> reqwest::Result<Vec<MarketplaceEntry>> {
        self.http.get(format!("{}/marketplace", self.base)).send()?.error_for_status()?.json()
    }

    // ── Bulk App Assignment — the same assignments across many apps ────────
    // POST /apps/assign — dryRun (default) previews per-app; a live run replaces
    // each app's assignment set. Per-app results ride inside the 200 body.
    pub fn bulk_assign(&self, req: &BulkAssignRequest) -> reqwest::Result<BulkAssignResult> {
        self.http
            .post(format!("{}/apps/assign", self.base))
            .json(req)
            .send()?
            .error_for_status()?
            .json()
    }

    // ── Detection & Remediation — run reporting + gated on-demand run ──────
    // GET /remediation-scripts/{id}/run-summary → metric tiles. 409 (signed out) → empty.
    pub fn remediation_run_summary(&self, script_id: &str) -> reqwest::Result<Vec<ListItem>> {
        let resp = self
            .http
            .get(format!("{}/remediation-scripts/{}/run-summary", self.base, script_id))
            .send()?;
        if resp.status() == reqwest::StatusCode::CONFLICT {
            return Ok(Vec::new());
        }
        resp.error_for_status()?.json()
    }

    // GET /remediation-scripts/{id}/device-states → per-device state. 409 → empty.
    pub fn remediation_device_states(&self, script_id: &str) -> reqwest::Result<Vec<DeviceRunState>> {
        let resp = self
            .http
            .get(format!("{}/remediation-scripts/{}/device-states", self.base, script_id))
            .send()?;
        if resp.status() == reqwest::StatusCode::CONFLICT {
            return Ok(Vec::new());
        }
        resp.error_for_status()?.json()
    }

    // GET /remediation-scripts/{id}/scripts → decoded detection + remediation text for
    // the code viewer. 409 (signed out) / 404 → None; otherwise the decoded bodies.
    pub fn remediation_script_content(
        &self,
        script_id: &str,
    ) -> reqwest::Result<Option<RemediationScriptContent>> {
        let resp = self
            .http
            .get(format!("{}/remediation-scripts/{}/scripts", self.base, script_id))
            .send()?;
        if matches!(resp.status(), reqwest::StatusCode::CONFLICT | reqwest::StatusCode::NOT_FOUND) {
            return Ok(None);
        }
        resp.error_for_status()?.json().map(Some)
    }

    // POST /remediation-scripts/{id}/run/{deviceId} — gated on-demand remediation.
    // Sends confirm:true; surfaces the sidecar's 403 (disabled / destructive-off) or
    // 400 body VERBATIM so the UI shows the real reason.
    pub fn run_remediation(&self, script_id: &str, device_id: &str) -> Result<(), String> {
        let resp = self
            .http
            .post(format!("{}/remediation-scripts/{}/run/{}", self.base, script_id, device_id))
            .json(&serde_json::json!({ "confirm": true }))
            .send()
            .map_err(service_err)?;
        if resp.status().is_success() {
            return Ok(());
        }
        Err(body_error(resp))
    }

    // ── Policy Comparison — a SettingsCatalog baseline vs a tenant policy ───
    // POST /baselines/compare — settings grouped by verdict. 409 signed out.
    pub fn compare_baseline(
        &self,
        baseline_id: &str,
        policy_id: &str,
    ) -> reqwest::Result<BaselineComparison> {
        self.http
            .post(format!("{}/baselines/compare", self.base))
            .json(&BaselineCompareRequest {
                baseline_id: baseline_id.to_string(),
                policy_id: policy_id.to_string(),
            })
            .send()?
            .error_for_status()?
            .json()
    }

    // POST /policies/compare — diff two live SettingsCatalog policies (A vs B),
    // grouped by verdict with human-readable setting names. 409 signed out.
    pub fn compare_policies(
        &self,
        a_id: &str,
        b_id: &str,
    ) -> reqwest::Result<BaselineComparison> {
        self.http
            .post(format!("{}/policies/compare", self.base))
            .json(&PolicyCompareRequest {
                a_id: a_id.to_string(),
                b_id: b_id.to_string(),
            })
            .send()?
            .error_for_status()?
            .json()
    }

    // POST /marketplace/{id}/install — bind configFields + append a plugins.json row.
    // 400 (missing/invalid field) carries {error}; surface it verbatim.
    pub fn install_plugin(
        &self,
        id: &str,
        req: &MarketplaceInstallRequest,
    ) -> Result<MarketplaceInstallResult, String> {
        let resp = self
            .http
            .post(format!("{}/marketplace/{}/install", self.base, id))
            .json(req)
            .send()
            .map_err(service_err)?;
        if resp.status().is_success() {
            return resp.json().map_err(service_err);
        }
        Err(body_error(resp))
    }

    pub fn drift(&self, object_id: &str) -> reqwest::Result<DriftRecord> {
        self.http
            .get(format!("{}/drift", self.base))
            .query(&[("objectId", object_id)])
            .send()?
            .json()
    }

    pub fn search(&self, q: &str) -> reqwest::Result<Vec<SearchResult>> {
        self.http
            .get(format!("{}/search", self.base))
            .query(&[("q", q)])
            .send()?
            .json()
    }

    // ── M13.2 Pending AI changes — the human-in-the-loop approval inbox ──────
    // Writes an MCP client proposed, awaiting operator approval. Approve replays the
    // write server-side (snapshot-on-write + audit); reject records the decision.
    pub fn list_pending(&self) -> reqwest::Result<Vec<PendingChange>> {
        self.http
            .get(format!("{}/pending-changes", self.base))
            .send()?
            .error_for_status()?
            .json()
    }

    pub fn approve_change(&self, id: &str) -> reqwest::Result<()> {
        self.http
            .post(format!("{}/pending-changes/{}/approve", self.base, id))
            .send()?
            .error_for_status()?;
        Ok(())
    }

    pub fn reject_change(&self, id: &str) -> reqwest::Result<()> {
        self.http
            .post(format!("{}/pending-changes/{}/reject", self.base, id))
            .header("content-type", "application/json")
            .body("{}")
            .send()?
            .error_for_status()?;
        Ok(())
    }
}
