# M19 — Posture: continuous compliance as a product

> The posture score, the embedded baselines, and the report engines all already ship as one-shot tools — M19 wires them into a continuously-scored, benchmark-mapped, trended, evidence-exporting compliance product that feeds remediation candidates back into the M18 autonomy loop.

## What already exists

M19 is an **extension**, not a new subsystem. The pieces are in the tree today:

- **`SecurityPosture` DTO** — `crates/api-types/src/lib.rs:206`. A 0–100 `score`, a `breakdown: Vec<ScoreCategory>`, `gaps: Vec<SecurityGap>`, a `stats: PostureStats`, plus `ca_policies` / `compliance_policies` lists. Mirrored on the server as `SecurityPostureDto` (`service/Api/Contracts.cs:159`).
  - `ScoreCategory` (`lib.rs:217`): `category`, `score`, `max_score`, `items: Vec<String>`.
  - `SecurityGap` (`lib.rs:226`): `severity` (`high|medium|low`), `category`, `description`.
  - `PostureStats` (`lib.rs:234`): `ca_total`, `ca_enabled`, `ca_report_only`, `ca_disabled`, `compliance_policies`, `compliance_platforms`, `endpoint_security_intents`, `app_protection_policies`, `auth_strength_policies`, `named_locations`.
- **`GET /security-posture/summary`** — `service/Api/Endpoints/SecurityPostureEndpoints.cs:18`. Fans out across six Core services in parallel (`ConditionalAccessPolicyService`, `CompliancePolicyService`, `EndpointSecurityService`, `AppProtectionPolicyService`, `AuthenticationStrengthService`, `NamedLocationService`), then `ComputeScore` (line 62) produces five weighted categories: **Conditional Access** (max 30), **Compliance** (max 25), **Endpoint Security** (max 20), **App Protection** (max 15), **Auth & Locations** (max 10). Total = 100. This is a near-verbatim port of IntuneCommander's `SecurityPostureBridgeService.ComputeSecurityScore`.
- **`BaselineService`** — `service/Core/Services/BaselineService.cs`. Loads embedded OIB baselines (`oib-sc-baselines.json.gz`, `oib-es-baselines.json.gz`, `oib-compliance-baselines.json.gz`) as `BaselinePolicy` records (`PolicyType` = `SettingsCatalog|EndpointSecurity|Compliance`, `Category`, `Name`, `RawJson`). `CompareSettingsCatalog` (line 84) diffs a tenant Settings Catalog policy against a baseline → `BaselineComparisonResult { Matching, Missing, Drifted, Extra }`. Category parsing in `ParseCategory` (line 135).
- **Three export engines** to generalize:
  - `IConditionalAccessPptExportService` (`service/Core/Services/IConditionalAccessPptExportService.cs`) + the `CaPptExport/` folder (`PowerPointHelper`, `ControlGrantBlock`, `Conditions`, …) — produces PPTX from CA policies.
  - `AssignmentReportExporter` (`service/Core/Services/AssignmentReportExporter.cs`) — `GenerateHtml` / `GenerateCsv` over `AssignmentReportRow`, dynamic columns, embedded SVG bar charts, CSV-injection-safe.
- **The time-machine** — `service/Store/SnapshotStore.cs`. `ConfigSnapshotRecord(SnapshotId, ObjectId, ObjectType, ObjectName, CapturedUtc, BodyJson, ContentHash)` (line 76), append-only, deduped by content hash. `GetSnapshotsForObjectAsync` (line 368) returns every historical body for an object, newest-first — the raw material for trend lines.

M19 does **not** recompute Graph state, redefine the score categories, or write a new export framework. It maps, persists, trends, and packages what these already emit.

## From snapshot to product

Four extensions turn the point-in-time `SecurityPosture` into a continuous product:

**(a) Benchmark mapping.** The five `ScoreCategory.category` strings are mapped to external control IDs (CIS / OIB / Essential 8 / NIST 800-53). A static mapping table (sourced from `BaselineService` category metadata, embedded as `Intune.Commander.Core.Assets.benchmark-map.json.gz`) attaches a `controls[]` array to each category and each `SecurityGap`. `GET /posture/score?benchmark=cis` returns the same `SecurityPostureDto` shape with control IDs grafted on, plus per-benchmark coverage %. The score weights are unchanged; only the *labeling* and the framework rollup are new.

**(b) Trend over the time-machine.** Each `/posture/score` computation is itself snapshotted into `SnapshotStore` as a synthetic object (`ObjectType = "security-posture"`, `ObjectId = "{benchmark}"`, `BodyJson` = the serialized `SecurityPostureDto`). `GET /posture/trend` then replays `GetSnapshotsForObjectAsync` and projects each historical body down to `{ capturedUtc, score, categoryScores }` — a sparkline with no extra storage layer. Dedup-by-hash means a flat posture costs zero rows; only real movement is recorded.

**(c) Evidence-pack export.** `POST /posture/evidence-pack` generalizes the three engines into an **all-surface, point-in-time assessor pack**: it pins an `asOf` timestamp, pulls the matching `ConfigSnapshotRecord` bodies from the time-machine, runs `AssignmentReportExporter.GenerateHtml/Csv` per surface, `ConditionalAccessPptExportService.ExportAsync` for the CA deck, renders the posture score + baseline-comparison results to PDF/Markdown, and zips them with a signed manifest. The pack is reproducible: same `asOf` ⇒ byte-identical artifacts (modulo render timestamp).

**(d) POA&M from drift.** Every open `SecurityGap` (and every `Drifted`/`Missing` row from `BaselineService.CompareSettingsCatalog`) becomes a **Plan of Action & Milestones** item: gap → mapped control(s) → recommended remediation → owner → due date (derived from `severity`: high = 30d, medium = 60d, low = 90d). `GET /posture/poam` returns the live POA&M; closed items are inferred when the originating gap disappears from a later score.

## Contract additions

Add to `contract/openapi.yaml` first, then mirror in `crates/api-types/src/lib.rs` and `service/Api/Contracts.cs`. New endpoints live in a `PostureEndpoints.cs` module wired in `Program.cs`.

| Method | Path | Returns | Notes |
|---|---|---|---|
| GET | `/security-posture/summary` | `SecurityPostureDto` | **Already exists** (`SecurityPostureEndpoints.cs:18`). Unchanged. |
| GET | `/posture/score?benchmark=cis\|oib\|e8\|nist` | `BenchmarkedPostureDto` | `SecurityPostureDto` + per-category `controls[]` + framework `coverage`. Also snapshots itself. |
| GET | `/posture/trend?benchmark=cis&from=…&to=…` | `PostureTrendDto` | Score + category scores over time from `SnapshotStore`. |
| POST | `/posture/evidence-pack` | `EvidencePackManifestDto` (+ zip on disk) | Body picks `asOf`, `benchmark`, `surfaces[]`, `formats[]`. |
| GET | `/posture/poam?benchmark=cis` | `PoamDto` (`items: PoamItemDto[]`) | Open gaps → controls → remediation → due. |

New DTOs (camelCase over the wire): `BenchmarkedPostureDto`, `BenchmarkControlRef`, `PostureTrendDto`, `PostureTrendPoint`, `PoamDto`, `PoamItemDto`, `EvidencePackManifestDto`, `EvidenceArtifactRef`. All `409 Conflict` when signed out, matching the existing posture endpoint.

## Sample data

**`GET /posture/score?benchmark=cis`** — the existing `SecurityPostureDto` shape with control IDs grafted onto each category (real `ScoreCategory` field names: `category`, `score`, `maxScore`, `items`):

```json
{
  "benchmark": "cis",
  "benchmarkVersion": "CIS Microsoft Intune for Windows 11 v3.0.1",
  "score": 71,
  "coverage": { "controlsCovered": 38, "controlsTotal": 54, "percent": 70 },
  "breakdown": [
    {
      "category": "Conditional Access",
      "score": 25,
      "maxScore": 30,
      "items": ["4 enabled", "1 report-only"],
      "controls": [
        { "framework": "cis", "id": "1.1.1", "title": "Ensure MFA via CA is enforced" },
        { "framework": "e8", "id": "MFA", "title": "Multi-factor Authentication" },
        { "framework": "nist", "id": "IA-2", "title": "Identification and Authentication" }
      ]
    },
    {
      "category": "Compliance",
      "score": 18,
      "maxScore": 25,
      "items": ["3 policies", "2 platforms covered"],
      "controls": [
        { "framework": "cis", "id": "2.3.1", "title": "Device compliance policy assigned" },
        { "framework": "oib", "id": "OIB-Compliance-Win", "title": "Windows compliance baseline" }
      ]
    }
  ],
  "gaps": [
    { "severity": "high", "category": "Conditional Access", "description": "No CA policy targets all users" }
  ],
  "stats": {
    "caTotal": 6, "caEnabled": 4, "caReportOnly": 1, "caDisabled": 1,
    "compliancePolicies": 3, "compliancePlatforms": ["Windows", "iOS"],
    "endpointSecurityIntents": 2, "appProtectionPolicies": 1,
    "authStrengthPolicies": 1, "namedLocations": 2
  }
}
```

**`GET /posture/trend?benchmark=cis`** — score over time, replayed from `SnapshotStore` (each point is one historical `security-posture` snapshot body):

```json
{
  "benchmark": "cis",
  "points": [
    { "capturedUtc": "2026-05-01T09:02:00Z", "score": 58, "categoryScores": { "Conditional Access": 15, "Compliance": 14, "Endpoint Security": 10, "App Protection": 10, "Auth & Locations": 9 } },
    { "capturedUtc": "2026-05-20T09:01:00Z", "score": 66, "categoryScores": { "Conditional Access": 20, "Compliance": 18, "Endpoint Security": 10, "App Protection": 10, "Auth & Locations": 8 } },
    { "capturedUtc": "2026-06-24T09:00:00Z", "score": 71, "categoryScores": { "Conditional Access": 25, "Compliance": 18, "Endpoint Security": 15, "App Protection": 5, "Auth & Locations": 8 } }
  ],
  "delta": { "since": "2026-05-01T09:02:00Z", "scoreChange": 13, "regressions": ["App Protection -5"] }
}
```

**`GET /posture/poam`** — one POA&M item (gap → control → remediation → due):

```json
{
  "benchmark": "cis",
  "generatedUtc": "2026-06-24T09:00:00Z",
  "items": [
    {
      "id": "poam-ca-allusers-2026q2",
      "severity": "high",
      "category": "Conditional Access",
      "finding": "No CA policy targets all users",
      "controls": [{ "framework": "cis", "id": "1.1.1" }, { "framework": "e8", "id": "MFA" }],
      "remediation": "Create an enabled CA policy with conditions.users.includeUsers = \"All\" requiring MFA.",
      "source": "security-gap",
      "owner": "security@contoso.com",
      "openedUtc": "2026-06-24T09:00:00Z",
      "dueUtc": "2026-07-24T09:00:00Z",
      "state": "open",
      "remediationCandidateRef": "m18:ca-require-mfa-all-users"
    }
  ]
}
```

**`POST /posture/evidence-pack`** → `EvidencePackManifestDto` — what point-in-time artifacts it bundles:

```json
{
  "packId": "evp-2026-06-24-cis-soc2",
  "asOf": "2026-06-24T00:00:00Z",
  "benchmark": "cis",
  "tenantName": "Contoso",
  "score": 71,
  "zipPath": "%LocalAppData%\\cmProjectX\\evidence\\evp-2026-06-24-cis-soc2.zip",
  "artifacts": [
    { "kind": "posture-score", "format": "pdf", "path": "score-summary.pdf", "sourceEngine": "PostureRenderer" },
    { "kind": "conditional-access", "format": "pptx", "path": "ca-policies.pptx", "sourceEngine": "ConditionalAccessPptExportService" },
    { "kind": "assignments", "format": "html", "path": "assignment-report.html", "sourceEngine": "AssignmentReportExporter" },
    { "kind": "baseline-comparison", "format": "md", "path": "oib-drift.md", "sourceEngine": "BaselineService.CompareSettingsCatalog" },
    { "kind": "poam", "format": "csv", "path": "poam.csv", "sourceEngine": "AssignmentReportExporter.GenerateCsv" }
  ],
  "snapshotIds": ["snap-ca-8821", "snap-comp-4410", "snap-endp-2207"],
  "manifestHash": "sha256:9f1c…e3"
}
```

## Benchmark mapping

The five score categories from `ComputeScore` (`SecurityPostureEndpoints.cs:62`) are the join keys. A static map attaches control references per `(category, benchmark)`:

| `ScoreCategory.category` | CIS | OIB | Essential 8 | NIST 800-53 |
|---|---|---|---|---|
| Conditional Access | 1.1.x | OIB-Identity | MFA, Restrict Admin | IA-2, AC-3 |
| Compliance | 2.3.x | OIB-Compliance-{platform} | Patch OS | CM-6, SI-2 |
| Endpoint Security | 3.x | OIB-ES-{cat} (`oib-es-baselines`) | App Control, Macro | SC-7, SI-3 |
| App Protection | 4.x | OIB-AppProtect | Restrict Macros | AC-19, MP-7 |
| Auth & Locations | 1.2.x | OIB-Identity | MFA | IA-2(11), AC-17 |

**Where baselines come from.** OIB control IDs reuse `BaselineService` directly: `GetCategories()` and `BaselinePolicy.Category` (parsed by `ParseCategory`, `BaselineService.cs:135`) are the OIB category labels; the `oib-es-baselines.json.gz` / `oib-compliance-baselines.json.gz` resources are the authority for the OIB column. CIS / Essential 8 / NIST mappings are a curated overlay shipped as `benchmark-map.json.gz` (same embedded-resource pattern as the OIB baselines), keyed by category, versioned by benchmark release. Coverage % = (categories with at least one satisfied control) / (controls in scope for that benchmark).

## Feeds M18

Every POA&M item carries a `remediationCandidateRef` (see sample above). When `/posture/trend` reports a **regression** — a category score that dropped between two snapshots, e.g. `"App Protection -5"` — M19 emits the originating gap as a remediation candidate keyed into the M18 autonomy loop. M18 already gates writes through the M6 safe-write rails (`/preview-diff`, snapshot-on-write, audit) and the M13 pending-changes inbox (`PendingChangeRecord`, `SnapshotStore.cs:93`); M19 supplies the *what* (the gap and its target state), M18 supplies the *how* (a proposed, operator-approved write). Closing the loop: after M18 applies a remediation, the next scheduled `/posture/score` snapshots a higher category score, `/posture/trend` shows the recovery, and the POA&M item flips to `state: "closed"`.

## Definition of done

- `GET /posture/score?benchmark=cis` returns the existing `SecurityPostureDto` shape with `controls[]` grafted onto each category and a benchmark `coverage` rollup, and self-snapshots into `SnapshotStore` under `ObjectType = "security-posture"`.
- Make a real change in the tenant (enable one CA policy), re-run `/posture/score`: the new score lands as a fresh snapshot.
- `GET /posture/trend` then shows **two points with a non-zero delta** — proving the time-machine wiring, not just a single read.
- `POST /posture/evidence-pack` with the same `asOf` twice produces the **same manifest hash** (reproducible point-in-time pack); regenerating after the change shows the score delta inside the pack.
- `GET /posture/poam` lists the closed item once the gap it came from disappears from the latest score.

## Open questions

- **Benchmark sources & licensing.** CIS Benchmarks are downloadable but their redistribution terms restrict shipping verbatim control text. Do we embed only control *IDs + our own one-line titles* (likely safe) and link out, or license the full text? Essential 8 / NIST 800-53 are public-domain; CIS is the constraint.
- **Evidence-pack format & signing.** Is the assessor deliverable a single signed zip (manifest + `manifestHash`) sufficient, or do SOC2/ISO assessors want per-artifact attestation (e.g. a detached signature per PDF)? Should the manifest hash chain into the append-only audit log for tamper-evidence?
- **Scoring weights vs. benchmark weights.** `ComputeScore` weights are cmProjectX's own opinion (CA=30, Compliance=25, …). A CIS-faithful score would weight by CIS control count, not our five buckets. Do we keep one canonical score and only *relabel* per benchmark (current plan), or compute a genuinely benchmark-native score per framework? The latter forks the number the dashboard shows.
- **Trend cadence.** Who triggers periodic `/posture/score` snapshots — a sidecar timer, the M18 loop, or only on-demand from the UI? Cadence sets trend resolution and storage growth (mitigated by dedup-by-hash).
