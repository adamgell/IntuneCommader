//! Tier-1 contract-parity tests — the Rust half of the Rust↔.NET seam guarantee.
//!
//! Each canonical wire file in `contract/examples/` is parsed, deserialized into
//! its mirrored `api-types` struct, then re-serialized; the result must equal the
//! original JSON value (object key order is ignored by `serde_json::Value`). The
//! .NET side (`service/Api.Tests.Unit/ContractParityTests.cs`) asserts the SAME
//! files against `service/Api/Contracts.cs`, so a divergence on either side — a
//! renamed field, a changed enum spelling like `GCCHigh` — fails the test.

use std::fs;
use std::path::PathBuf;

use api_types::*;
use serde::Serialize;
use serde::de::DeserializeOwned;

fn corpus(name: &str) -> String {
    // CARGO_MANIFEST_DIR = crates/api-types, regardless of the test's CWD.
    let path = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("../../contract/examples")
        .join(name);
    fs::read_to_string(&path).unwrap_or_else(|e| panic!("read {}: {e}", path.display()))
}

fn assert_round_trips<T: Serialize + DeserializeOwned>(name: &str) {
    let text = corpus(name);
    let canonical: serde_json::Value =
        serde_json::from_str(&text).unwrap_or_else(|e| panic!("{name}: canonical parse: {e}"));
    let dto: T = serde_json::from_value(canonical.clone())
        .unwrap_or_else(|e| panic!("{name}: deserialize into {}: {e}", std::any::type_name::<T>()));
    let reemitted = serde_json::to_value(&dto).expect("serialize");
    assert_eq!(
        canonical, reemitted,
        "{name}: api-types {} did not round-trip the contract wire form",
        std::any::type_name::<T>()
    );
}

#[test]
fn list_item_round_trips() {
    assert_round_trips::<ListItem>("list-item.json");
}

#[test]
fn mdm_provider_descriptor_round_trips() {
    assert_round_trips::<MdmProviderDescriptor>("mdm-provider-descriptor.json");
}

#[test]
fn sync_status_round_trips() {
    assert_round_trips::<SyncStatus>("sync-status.json");
}

#[test]
fn audit_event_round_trips() {
    assert_round_trips::<AuditEvent>("audit-event.json");
}

#[test]
fn drift_object_round_trips() {
    assert_round_trips::<DriftObject>("drift-object.json");
}

#[test]
fn tenant_profile_round_trips() {
    assert_round_trips::<TenantProfileSummary>("tenant-profile.json");
}

#[test]
fn device_code_prompt_round_trips() {
    assert_round_trips::<DeviceCodePrompt>("device-code-prompt.json");
}

// M15 GitOps — pull manifest, plan change set (incl. nested DriftChange), apply
// result, and status summary all round-trip the contract wire form.
#[test]
fn gitops_manifest_round_trips() {
    assert_round_trips::<GitOpsManifest>("gitops-manifest.json");
}

#[test]
fn gitops_plan_round_trips() {
    assert_round_trips::<GitOpsPlan>("gitops-plan.json");
}

#[test]
fn gitops_apply_result_round_trips() {
    assert_round_trips::<GitOpsApplyResult>("gitops-apply-result.json");
}

#[test]
fn gitops_status_round_trips() {
    assert_round_trips::<GitOpsStatus>("gitops-status.json");
}

// M16 Foresight — simulate request + blast-radius report (incl. nested types).
#[test]
fn simulate_request_round_trips() {
    assert_round_trips::<SimulateRequest>("simulate-request.json");
}

#[test]
fn blast_radius_report_round_trips() {
    assert_round_trips::<BlastRadiusReport>("blast-radius-report.json");
}

// M18 Autonomy — policy (nested scope/signals/throttle) + end-to-end run record
// (nested watched/detection/proposal+blastRadius/decision/verify).
#[test]
fn autonomy_policy_round_trips() {
    assert_round_trips::<AutonomyPolicy>("autonomy-policy.json");
}

#[test]
fn autonomy_run_round_trips() {
    assert_round_trips::<AutonomyRun>("autonomy-run.json");
}

// M17 tenant digital twin — stats, analytics result (nested findings/refs), neighborhood.
#[test]
fn twin_stats_round_trips() {
    assert_round_trips::<TwinStats>("twin-stats.json");
}

#[test]
fn twin_analytics_result_round_trips() {
    assert_round_trips::<TwinAnalyticsResult>("twin-analytics-result.json");
}

#[test]
fn twin_neighborhood_round_trips() {
    assert_round_trips::<TwinNeighborhood>("twin-neighborhood.json");
}

// M19 — continuous posture DTOs (benchmark-mapped score, trend, POA&M, evidence pack).
#[test]
fn benchmarked_posture_round_trips() {
    assert_round_trips::<BenchmarkedPosture>("benchmarked-posture.json");
}

#[test]
fn posture_trend_round_trips() {
    assert_round_trips::<PostureTrend>("posture-trend.json");
}

#[test]
fn poam_round_trips() {
    assert_round_trips::<Poam>("poam.json");
}

#[test]
fn evidence_pack_manifest_round_trips() {
    assert_round_trips::<EvidencePackManifest>("evidence-pack-manifest.json");
}

// M20 fleet — multi-tenant fan-out response DTOs.
#[test]
fn tenant_group_round_trips() {
    assert_round_trips::<TenantGroup>("tenant-group.json");
}

#[test]
fn fleet_list_response_round_trips() {
    assert_round_trips::<FleetListResponse>("fleet-list-response.json");
}

#[test]
fn campaign_result_round_trips() {
    assert_round_trips::<CampaignResult>("campaign-result.json");
}

#[test]
fn fleet_drift_response_round_trips() {
    assert_round_trips::<FleetDriftResponse>("fleet-drift-response.json");
}

#[test]
fn fleet_posture_response_round_trips() {
    assert_round_trips::<FleetPostureResponse>("fleet-posture-response.json");
}

#[test]
fn golden_template_round_trips() {
    assert_round_trips::<GoldenTemplate>("golden-template.json");
}

// M21 ecosystem response DTOs — pack manifest, playbook, marketplace entry.
#[test]
fn pack_manifest_round_trips() {
    assert_round_trips::<PackManifest>("pack-manifest.json");
}

#[test]
fn playbook_round_trips() {
    assert_round_trips::<Playbook>("playbook.json");
}

#[test]
fn marketplace_entry_round_trips() {
    assert_round_trips::<MarketplaceEntry>("marketplace-entry.json");
}

// Classic build-out slices — the three former nav stubs that added a new endpoint:
// policy comparison (baseline vs tenant), bulk app assign result, remediation run state.
#[test]
fn baseline_comparison_round_trips() {
    assert_round_trips::<BaselineComparison>("baseline-comparison.json");
}

#[test]
fn bulk_assign_result_round_trips() {
    assert_round_trips::<BulkAssignResult>("bulk-assign-result.json");
}

#[test]
fn device_run_state_round_trips() {
    assert_round_trips::<DeviceRunState>("device-run-state.json");
}

// M14 Hands — a device action-history row (Graph deviceActionResult projection).
#[test]
fn device_action_record_round_trips() {
    assert_round_trips::<DeviceActionRecord>("device-action-record.json");
}
