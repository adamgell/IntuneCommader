using System.Text.Json;
using System.Text.Json.Nodes;
using CmProjectX.Api;
using Xunit;

namespace CmProjectX.Tests.Unit;

// Tier-1 contract-parity tests: the .NET half of the Rust↔.NET seam guarantee.
// Each canonical wire file in contract/examples/ is deserialized into its
// service/Api/Contracts.cs DTO and re-serialized with the same System.Text.Json
// "Web" defaults the minimal API uses (camelCase). The re-emitted JSON must
// structurally equal the file — so a renamed field, wrong casing, or a changed
// enum spelling breaks the test. The Rust api-types crate round-trips the SAME
// files; together they pin the two DTO sets to one contract.
public class ContractParityTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // Uniform error envelope — the one non-2xx shape shared by both sides of the seam.
    [Fact] public void Error_RoundTripsContract() => AssertRoundTrips<ErrorDto>("error.json");

    [Fact] public void ListItem_RoundTripsContract() => AssertRoundTrips<ListItemDto>("list-item.json");
    [Fact] public void MdmProviderDescriptor_RoundTripsContract() => AssertRoundTrips<MdmProviderDescriptorDto>("mdm-provider-descriptor.json");
    [Fact] public void SyncStatus_RoundTripsContract() => AssertRoundTrips<SyncStatusDto>("sync-status.json");
    [Fact] public void AuditEvent_RoundTripsContract() => AssertRoundTrips<AuditEventDto>("audit-event.json");
    [Fact] public void DriftObject_RoundTripsContract() => AssertRoundTrips<DriftObjectDto>("drift-object.json");
    [Fact] public void TenantProfile_RoundTripsContract() => AssertRoundTrips<TenantProfileSummaryDto>("tenant-profile.json");
    [Fact] public void DeviceCodePrompt_RoundTripsContract() => AssertRoundTrips<DeviceCodePromptDto>("device-code-prompt.json");

    // M15 GitOps — the pull manifest, the plan change set (incl. nested DriftChange),
    // the apply result, and the status summary all round-trip the contract wire form.
    [Fact] public void GitOpsManifest_RoundTripsContract() => AssertRoundTrips<GitOpsManifest>("gitops-manifest.json");
    [Fact] public void GitOpsPlan_RoundTripsContract() => AssertRoundTrips<GitOpsPlan>("gitops-plan.json");
    [Fact] public void GitOpsApplyResult_RoundTripsContract() => AssertRoundTrips<GitOpsApplyResult>("gitops-apply-result.json");
    [Fact] public void GitOpsStatus_RoundTripsContract() => AssertRoundTrips<GitOpsStatus>("gitops-status.json");

    // M16 Foresight — the simulate request + the blast-radius report (incl. nested
    // SamplePrincipal/SimConflict/SimRedundancy/CrossPolicyImpact) round-trip the contract.
    [Fact] public void SimulateRequest_RoundTripsContract() => AssertRoundTrips<SimulateRequest>("simulate-request.json");
    [Fact] public void BlastRadiusReport_RoundTripsContract() => AssertRoundTrips<BlastRadiusReportDto>("blast-radius-report.json");

    // M18 Autonomy — the policy (incl. nested scope/signals/throttle) and the end-to-end
    // run record (incl. nested watched/detection/proposal+blastRadius/decision/verify)
    // round-trip the contract wire form.
    [Fact] public void AutonomyPolicy_RoundTripsContract() => AssertRoundTrips<AutonomyPolicyDto>("autonomy-policy.json");
    [Fact] public void AutonomyRun_RoundTripsContract() => AssertRoundTrips<AutonomyRunDto>("autonomy-run.json");
    // M17 tenant digital twin — stats, analytics result (incl. nested findings/refs),
    // and node neighborhood round-trip the contract.
    [Fact] public void TwinStats_RoundTripsContract() => AssertRoundTrips<TwinStats>("twin-stats.json");
    [Fact] public void TwinAnalyticsResult_RoundTripsContract() => AssertRoundTrips<TwinAnalyticsResult>("twin-analytics-result.json");
    [Fact] public void TwinNeighborhood_RoundTripsContract() => AssertRoundTrips<TwinNeighborhood>("twin-neighborhood.json");
    // M19 — continuous posture DTOs (benchmark-mapped score, trend, POA&M, evidence pack).
    [Fact] public void BenchmarkedPosture_RoundTripsContract() => AssertRoundTrips<BenchmarkedPostureDto>("benchmarked-posture.json");
    [Fact] public void PostureTrend_RoundTripsContract() => AssertRoundTrips<PostureTrendDto>("posture-trend.json");
    [Fact] public void Poam_RoundTripsContract() => AssertRoundTrips<PoamDto>("poam.json");
    [Fact] public void EvidencePackManifest_RoundTripsContract() => AssertRoundTrips<EvidencePackManifestDto>("evidence-pack-manifest.json");
    // M20 fleet — multi-tenant fan-out response DTOs.
    [Fact] public void TenantGroup_RoundTripsContract() => AssertRoundTrips<TenantGroupDto>("tenant-group.json");
    [Fact] public void FleetListResponse_RoundTripsContract() => AssertRoundTrips<FleetListResponseDto>("fleet-list-response.json");
    [Fact] public void CampaignResult_RoundTripsContract() => AssertRoundTrips<CampaignResultDto>("campaign-result.json");
    [Fact] public void FleetDriftResponse_RoundTripsContract() => AssertRoundTrips<FleetDriftResponseDto>("fleet-drift-response.json");
    [Fact] public void FleetPostureResponse_RoundTripsContract() => AssertRoundTrips<FleetPostureResponseDto>("fleet-posture-response.json");
    [Fact] public void GoldenTemplate_RoundTripsContract() => AssertRoundTrips<GoldenTemplateDto>("golden-template.json");
    // M21 ecosystem response DTOs — pack manifest, playbook, marketplace entry.
    [Fact] public void PackManifest_RoundTripsContract() => AssertRoundTrips<PackManifest>("pack-manifest.json");
    [Fact] public void Playbook_RoundTripsContract() => AssertRoundTrips<Playbook>("playbook.json");
    [Fact] public void MarketplaceEntry_RoundTripsContract() => AssertRoundTrips<MarketplaceEntry>("marketplace-entry.json");

    // Classic build-out slices — the three former nav stubs that added a new endpoint:
    // policy comparison (baseline vs tenant), bulk app assign result, remediation run state.
    [Fact] public void BaselineComparison_RoundTripsContract() => AssertRoundTrips<BaselineComparisonDto>("baseline-comparison.json");
    [Fact] public void BulkAssignResult_RoundTripsContract() => AssertRoundTrips<BulkAssignResult>("bulk-assign-result.json");
    [Fact] public void DeviceRunState_RoundTripsContract() => AssertRoundTrips<DeviceRunStateDto>("device-run-state.json");

    // M14 Hands — a device action-history row (Graph deviceActionResult projection).
    [Fact] public void DeviceActionRecord_RoundTripsContract() => AssertRoundTrips<DeviceActionRecord>("device-action-record.json");

    [Fact]
    public void CamelCaseKeys_AreEmitted()
    {
        // Guard against an accidental PascalCase regression (e.g. dropping Web
        // defaults). Inspect the raw JSON text — a JsonObject built from Web
        // options does case-INSENSITIVE key lookup, so ContainsKey can't tell
        // "subtitle" from "Subtitle".
        var json = JsonSerializer.Serialize(
            JsonSerializer.Deserialize<ListItemDto>(Load("list-item.json"), Web), Web);
        Assert.Contains("\"subtitle\"", json);
        Assert.DoesNotContain("\"Subtitle\"", json);
        Assert.DoesNotContain("\"Title\"", json);
    }

    [Fact]
    public void EnumSpellings_AreContractExact()
    {
        // These exact strings are what the Rust enums must (de)serialize to — e.g.
        // Cloud::GccHigh is #[serde(rename = "GCCHigh")], AuthMethod::ClientSecret
        // serializes to "ClientSecret". A drift on either side breaks parity.
        var p = JsonSerializer.Deserialize<TenantProfileSummaryDto>(Load("tenant-profile.json"), Web)!;
        Assert.Equal("GCCHigh", p.Cloud);
        Assert.Equal("ClientSecret", p.AuthMethod);

        var s = JsonSerializer.Deserialize<SyncStatusDto>(Load("sync-status.json"), Web)!;
        Assert.Equal("SignedIn", s.AuthState);
        Assert.Equal("Commercial", s.Cloud);
    }

    // --- helpers ------------------------------------------------------------

    private static void AssertRoundTrips<T>(string fileName)
    {
        var text = Load(fileName);
        var dto = JsonSerializer.Deserialize<T>(text, Web);
        Assert.NotNull(dto);

        var reEmitted = JsonSerializer.SerializeToNode(dto, Web);
        var canonical = JsonNode.Parse(text);

        Assert.True(JsonNode.DeepEquals(canonical, reEmitted),
            $"{fileName}: .NET DTO {typeof(T).Name} did not round-trip the contract wire form.\n" +
            $"  canonical:  {canonical?.ToJsonString()}\n" +
            $"  re-emitted: {reEmitted?.ToJsonString()}");
    }

    private static string Load(string name) => File.ReadAllText(Path.Combine(CorpusDir(), name));

    // Walk up from the test bin dir to the repo root (the dir holding
    // contract/openapi.yaml), then into contract/examples/. Mirrors how
    // SidecarFixture locates repo artifacts at runtime.
    private static string CorpusDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "contract", "openapi.yaml")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException(
                $"Could not locate repo root (contract/openapi.yaml) from {AppContext.BaseDirectory}");
        return Path.Combine(dir.FullName, "contract", "examples");
    }
}
