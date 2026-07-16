using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CmProjectX.Store;
using Intune.Commander.Core.Models;
using Intune.Commander.Core.Services;
// Microsoft.Graph.Beta.Models defines a `WebApplication` type that collides with
// the ASP.NET host type used by the extension-method signature (CS0104). Alias the
// host type so `this WebApplication app` resolves (see SecurityPostureEndpoints).
using WebApplication = Microsoft.AspNetCore.Builder.WebApplication;

namespace CmProjectX.Api;

// M19 — continuous posture (docs/part-ii/M19-posture.md). EXTENDS the point-in-time
// /security-posture/summary into a benchmark-mapped, trended, evidence-exporting
// compliance product. It does NOT recompute Graph state or redefine the score: it
// reuses SecurityPostureEndpoints.GatherAsync for the number, BenchmarkMapService
// for the control overlay, BaselineService for OIB IDs, the SnapshotStore for the
// trend, and the existing export engines for the evidence pack.
//
//   GET  /posture/score?benchmark=     benchmark-mapped score; self-snapshots for trend
//   GET  /posture/trend?benchmark=     score over time replayed from the time-machine
//   POST /posture/evidence-pack        all-surface point-in-time assessor pack (zip)
//   GET  /posture/poam?benchmark=      open gaps → controls → remediation → due date
//
// All 409 (Conflict) when signed out, matching /security-posture/summary. These are
// ACTION/aggregate endpoints — NOT added to Surfaces.cs / EndpointInventory.cs.
public static class PostureEndpoints
{
    // Synthetic time-machine object for self-snapshotting the score (per benchmark).
    private const string PostureObjectType = "security-posture";

    // High → 30d, medium → 60d, low → 90d remediation windows for POA&M due dates.
    private static readonly Dictionary<string, int> DueDays = new(StringComparer.OrdinalIgnoreCase)
    {
        ["high"] = 30, ["medium"] = 60, ["low"] = 90,
    };

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static void MapPosture(this WebApplication app)
    {
        // ── GET /posture/score — benchmark-mapped score + coverage; self-snapshots ──
        app.MapGet("/posture/score", async (string? benchmark, AuthSession auth, ISnapshotStore store, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var bm = BenchmarkMapService.Normalize(benchmark);
            var map = new BenchmarkMapService(new BaselineService());

            var posture = await SecurityPostureEndpoints.GatherAsync(g, ct);
            var benchmarked = Decorate(posture, bm, map);

            // Self-snapshot: each score computation lands as a synthetic object so
            // /posture/trend can replay it. Dedup-by-hash means a flat posture costs
            // zero rows — only real movement is recorded.
            var body = JsonSerializer.Serialize(benchmarked, Web);
            var rec = new ConfigSnapshotRecord(
                Guid.NewGuid().ToString("n"), bm, PostureObjectType,
                $"Posture ({bm})", DateTime.UtcNow, body,
                TenantId: auth.ActiveProfile?.TenantId);
            var stored = await store.AppendSnapshotIfChangedAsync(rec, ct);
            if (stored is not null) await store.CommitIndexAsync(ct);

            return Results.Ok(benchmarked);
        });

        // ── GET /posture/trend — score over time, replayed from the time-machine ──
        // Reads the local store, scoped to the active tenant when a profile is selected.
        app.MapGet("/posture/trend", async (string? benchmark, DateTime? from, DateTime? to, ISnapshotStore store, AuthSession auth, CancellationToken ct) =>
        {
            var bm = BenchmarkMapService.Normalize(benchmark);
            var history = await store.GetSnapshotsForObjectAsync(bm, 500, ct, auth.ActiveProfile?.TenantId); // newest first

            // Oldest → newest, windowed by [from,to], projected to {capturedUtc, score, categoryScores}.
            var points = history
                .Where(s => s.ObjectType == PostureObjectType)
                .Where(s => (from is null || s.CapturedUtc >= from.Value.ToUniversalTime())
                         && (to is null || s.CapturedUtc <= to.Value.ToUniversalTime()))
                .OrderBy(s => s.CapturedUtc)
                .Select(ProjectPoint)
                .Where(p => p is not null)
                .Select(p => p!)
                .ToList();

            var delta = ComputeDelta(points);
            return Results.Ok(new PostureTrendDto(bm, points, delta));
        });

        // ── GET /posture/poam — open gaps → controls → remediation → due date ──
        // Also infers CLOSED items: POA&M ids that were open in an earlier score but
        // whose gap is absent from the current one (the remediation-loop closes here).
        app.MapGet("/posture/poam", async (string? benchmark, AuthSession auth, ISnapshotStore store, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var bm = BenchmarkMapService.Normalize(benchmark);
            var map = new BenchmarkMapService(new BaselineService());

            var posture = await SecurityPostureEndpoints.GatherAsync(g, ct);
            var now = DateTime.UtcNow;
            var items = await BuildFullPoamAsync(store, posture, bm, map, now, now, ct);
            return Results.Ok(new PoamDto(bm, now.ToString("o"), items));
        });

        // ── POST /posture/evidence-pack — all-surface point-in-time assessor pack ──
        app.MapPost("/posture/evidence-pack", async (EvidencePackRequest? req, AuthSession auth, ISnapshotStore store, CancellationToken ct) =>
        {
            var g = auth.Graph; if (g is null) return Results.Conflict();
            var bm = BenchmarkMapService.Normalize(req?.Benchmark);
            var map = new BenchmarkMapService(new BaselineService());
            var asOf = ParseAsOf(req?.AsOf);
            var tenantName = auth.ActiveProfile?.Name ?? "tenant";

            var posture = await SecurityPostureEndpoints.GatherAsync(g, ct);
            var benchmarked = Decorate(posture, bm, map);
            // The pack POA&M pins its history at asOf so closed items are reproducible.
            var poam = await BuildFullPoamAsync(store, posture, bm, map, asOf, asOf, ct);

            // Pull the matching config-snapshot bodies pinned at asOf from the
            // time-machine (the reproducible, point-in-time material for the pack).
            var pinned = await CollectPinnedSnapshotsAsync(store, asOf, auth.ActiveProfile?.TenantId, ct);

            var manifest = await WritePackAsync(
                g, benchmarked, posture, poam, tenantName, bm, asOf, pinned, req?.Formats, ct);
            return Results.Ok(manifest);
        });
    }

    // ── Benchmark decoration ────────────────────────────────────────────────

    private static BenchmarkedPostureDto Decorate(SecurityPostureDto p, string bm, BenchmarkMapService map)
    {
        var breakdown = p.Breakdown.Select(c => new BenchmarkedScoreCategoryDto(
            c.Category, c.Score, c.MaxScore, c.Items,
            map.CategoryControls(c.Category, bm).Select(ToRef).ToList())).ToList();

        var gaps = p.Gaps.Select(gap => new BenchmarkedGapDto(
            gap.Severity, gap.Category, gap.Description,
            map.GapControls(gap.Category, bm).Select(ToRef).ToList())).ToList();

        var coverage = ComputeCoverage(p.Breakdown.Where(c => c.Score > 0).Select(c => c.Category), bm, map);
        return new BenchmarkedPostureDto(bm, BenchmarkMapService.VersionFor(bm), p.Score, coverage, breakdown, gaps, p.Stats);
    }

    // Coverage = controls whose category scored > 0, over the controls in scope for the
    // benchmark. This is an INDICATIVE, category-level signal — a category marks its
    // mapped controls covered once it has any score; it does NOT per-control verify that
    // each policy meets the control (that limitation is stated in MethodologyCaveat,
    // surfaced verbatim in the evidence pack so the number can't be read as attestation).
    //
    // Extracted as a pure helper (covered categories in, coverage out) so the
    // broadened benchmark denominators can be unit-tested hermetically.
    internal static BenchmarkCoverageDto ComputeCoverage(IEnumerable<string> coveredCategories, string bm, BenchmarkMapService map)
    {
        var total = map.AllControls(bm).Count;
        var covered = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in coveredCategories)
            foreach (var ctrl in map.CategoryControls(category, bm))
                if (seen.Add($"{ctrl.Framework}|{ctrl.Id}"))
                    covered++;
        var percent = total == 0 ? 0 : (int)Math.Round(100.0 * covered / total);
        return new BenchmarkCoverageDto(covered, total, percent);
    }

    private static BenchmarkControlRefDto ToRef(BenchmarkMapService.ControlRef c) =>
        new(c.Framework, c.Id, c.Title);

    // ── Trend projection ──────────────────────────────────────────────────────

    internal static PostureTrendPointDto? ProjectPoint(ConfigSnapshotRecord snap)
    {
        try
        {
            var b = JsonSerializer.Deserialize<BenchmarkedPostureDto>(snap.BodyJson, Web);
            if (b is null) return null;
            // SortedDictionary → deterministic key order on the wire (mirrors the
            // Rust BTreeMap), so trend bodies are byte-stable for the same input.
            // Breakdown may deserialize null from an old/partial stored body — coalesce.
            var cats = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var c in b.Breakdown ?? []) cats[c.Category] = c.Score;
            return new PostureTrendPointDto(snap.CapturedUtc.ToString("o"), b.Score, cats);
        }
        catch (JsonException) { return null; }
    }

    internal static PostureTrendDeltaDto ComputeDelta(IReadOnlyList<PostureTrendPointDto> points)
    {
        if (points.Count == 0) return new PostureTrendDeltaDto(null, 0, [], []);
        var first = points[0];
        var last = points[^1];
        var regressions = new List<string>();
        var candidates = new List<PostureRemediationCandidateDto>();
        foreach (var (cat, lastScore) in last.CategoryScores)
        {
            if (first.CategoryScores.TryGetValue(cat, out var firstScore) && lastScore < firstScore)
            {
                var change = lastScore - firstScore;
                regressions.Add($"{cat} {change}"); // e.g. "App Protection -5"
                // M18 feed (read-only): each regression carries the originating gap as a
                // real remediation-candidate reference so the autonomy loop's candidate
                // list can pick it up. We do NOT auto-enqueue — M18 owns that decision.
                candidates.Add(new PostureRemediationCandidateDto(cat, change, RemediationCandidateForCategory(cat)));
            }
        }
        return new PostureTrendDeltaDto(first.CapturedUtc, last.Score - first.Score, regressions, candidates);
    }

    // ── POA&M ─────────────────────────────────────────────────────────────────

    // Open + inferred-closed POA&M items. Open items come from the CURRENT gaps; closed
    // items are POA&M ids that were open in an earlier posture snapshot (per benchmark)
    // but are absent from the current gap set — the loop closed. `historyUpperBound`
    // pins which snapshots count (asOf for the evidence pack, now for the live view).
    private static async Task<IReadOnlyList<PoamItemDto>> BuildFullPoamAsync(
        ISnapshotStore store, SecurityPostureDto posture, string bm, BenchmarkMapService map,
        DateTime now, DateTime historyUpperBound, CancellationToken ct)
    {
        var open = BuildPoam(posture, bm, map, now);
        var openIds = open.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);

        var history = await store.GetSnapshotsForObjectAsync(bm, 500, ct); // newest first
        var parsed = new List<(DateTime CapturedUtc, BenchmarkedPostureDto Score)>();
        foreach (var s in history)
        {
            if (s.ObjectType != PostureObjectType || s.CapturedUtc > historyUpperBound) continue;
            try
            {
                var b = JsonSerializer.Deserialize<BenchmarkedPostureDto>(s.BodyJson, Web);
                if (b is not null) parsed.Add((s.CapturedUtc, b));
            }
            catch (JsonException) { /* skip an unparseable historical body */ }
        }

        var closed = InferClosedItems(openIds, parsed, bm, map);
        return open.Concat(closed).ToList();
    }

    // Pure closed-item diff (hermetically tested). A POA&M id present in the history but
    // absent from `currentOpenIds` is inferred closed; the newest historical occurrence
    // supplies the finding/severity/controls and the "last seen open" opened date.
    internal static List<PoamItemDto> InferClosedItems(
        ISet<string> currentOpenIds,
        IReadOnlyList<(DateTime CapturedUtc, BenchmarkedPostureDto Score)> history,
        string bm, BenchmarkMapService map)
    {
        var closed = new List<PoamItemDto>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (capturedUtc, score) in history.OrderByDescending(h => h.CapturedUtc))
        {
            foreach (var gap in score.Gaps ?? [])
            {
                var id = PoamIdFromParts(gap.Category, gap.Description);
                if (currentOpenIds.Contains(id) || !seen.Add(id)) continue;
                var dueDays = DueDays.GetValueOrDefault(gap.Severity, 90);
                closed.Add(new PoamItemDto(
                    Id: id,
                    Severity: gap.Severity,
                    Category: gap.Category,
                    Finding: gap.Description,
                    Controls: map.GapControls(gap.Category, bm).Select(ToRef).ToList(),
                    Remediation: RecommendRemediation(gap.Category, gap.Description),
                    Source: "security-gap",
                    Owner: null,
                    OpenedUtc: capturedUtc.ToString("o"),
                    DueUtc: capturedUtc.AddDays(dueDays).ToString("o"),
                    State: "closed",
                    RemediationCandidateRef: RemediationCandidateRefFor(gap.Category, gap.Description)));
            }
        }
        return closed;
    }

    private static IReadOnlyList<PoamItemDto> BuildPoam(
        SecurityPostureDto p, string bm, BenchmarkMapService map, DateTime now)
    {
        var items = new List<PoamItemDto>();
        foreach (var gap in p.Gaps)
        {
            var dueDays = DueDays.GetValueOrDefault(gap.Severity, 90);
            items.Add(new PoamItemDto(
                Id: PoamIdFromParts(gap.Category, gap.Description),
                Severity: gap.Severity,
                Category: gap.Category,
                Finding: gap.Description,
                Controls: map.GapControls(gap.Category, bm).Select(ToRef).ToList(),
                Remediation: RecommendRemediation(gap.Category, gap.Description),
                Source: "security-gap",
                Owner: null,
                OpenedUtc: now.ToString("o"),
                DueUtc: now.AddDays(dueDays).ToString("o"),
                State: "open",
                RemediationCandidateRef: RemediationCandidateRefFor(gap.Category, gap.Description)));
        }
        return items;
    }

    // Stable id from category + a slug of the finding (no timestamp) so the SAME open
    // gap yields the SAME POA&M id across runs — that's how a closed item is inferred
    // when the gap disappears from a later score.
    internal static string PoamIdFromParts(string category, string description)
    {
        var slug = new string(category.ToLowerInvariant()
            .Where(ch => char.IsLetterOrDigit(ch) || ch == ' ').ToArray())
            .Trim().Replace(' ', '-');
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(description)))[..8].ToLowerInvariant();
        return $"poam-{slug}-{hash}";
    }

    private static string RecommendRemediation(string category, string description) => category switch
    {
        "Conditional Access" when description.Contains("all users") =>
            "Create an enabled CA policy with conditions.users.includeUsers = \"All\" requiring MFA.",
        "Conditional Access" =>
            "Enable at least one Conditional Access policy enforcing MFA for privileged access.",
        "Compliance" =>
            "Author and assign a device compliance policy for each managed platform.",
        "Endpoint Security" =>
            "Configure an endpoint security baseline (Antivirus / Disk Encryption / Firewall).",
        "App Protection" =>
            "Create an App Protection (MAM) policy to protect corporate data on mobile apps.",
        _ => "Address the finding to close the gap against the mapped controls.",
    };

    // Links a gap to an M18 remediation candidate id (the autonomy loop's "what").
    private static string? RemediationCandidateRefFor(string category, string description) => category switch
    {
        "Conditional Access" when description.Contains("all users") => "m18:ca-require-mfa-all-users",
        "Conditional Access" => "m18:ca-enable-mfa",
        "Compliance" => "m18:compliance-baseline",
        "Endpoint Security" => "m18:endpoint-security-baseline",
        "App Protection" => "m18:app-protection-policy",
        "Named Locations" or "Auth & Locations" => "m18:named-locations",
        _ => null,
    };

    // Category-level candidate (a trend regression has no single gap description, so it
    // maps by category only). Keep in step with RemediationCandidateRefFor.
    internal static string? RemediationCandidateForCategory(string category) => category switch
    {
        "Conditional Access" => "m18:ca-enable-mfa",
        "Compliance" => "m18:compliance-baseline",
        "Endpoint Security" => "m18:endpoint-security-baseline",
        "App Protection" => "m18:app-protection-policy",
        "Named Locations" or "Auth & Locations" => "m18:named-locations",
        _ => null,
    };

    // ── Evidence pack ─────────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<ConfigSnapshotRecord>> CollectPinnedSnapshotsAsync(
        ISnapshotStore store, DateTime asOf, string? tenantId, CancellationToken ct)
    {
        // The newest config snapshot per object at or before asOf — the point-in-time
        // pin. Excludes our own synthetic security-posture snapshots. Ordered by id so
        // the zip entries + manifest hash are stable. Tenant-scoped (tenant isolation).
        var objects = await store.GetSnapshottedObjectsAsync(ct: ct, tenantId: tenantId);
        var pinned = new List<ConfigSnapshotRecord>();
        foreach (var o in objects.Where(o => o.ObjectType != PostureObjectType))
        {
            var hist = await store.GetSnapshotsForObjectAsync(o.ObjectId, 200, ct, tenantId); // newest first
            var rec = hist.FirstOrDefault(s => s.CapturedUtc <= asOf);
            if (rec is not null) pinned.Add(rec);
        }
        return pinned.OrderBy(r => r.SnapshotId, StringComparer.Ordinal).ToList();
    }

    private static async Task<EvidencePackManifestDto> WritePackAsync(
        Microsoft.Graph.Beta.GraphServiceClient g,
        BenchmarkedPostureDto posture, SecurityPostureDto rawPosture, IReadOnlyList<PoamItemDto> poam,
        string tenantName, string bm, DateTime asOf, IReadOnlyList<ConfigSnapshotRecord> pinned,
        IReadOnlyList<string>? formats, CancellationToken ct)
    {
        var packId = $"evp-{asOf:yyyy-MM-dd}-{bm}";
        var evidenceDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cmProjectX", "evidence");
        Directory.CreateDirectory(evidenceDir);
        var zipPath = Path.Combine(evidenceDir, $"{packId}.zip");

        // Build the artifacts by delegating to the REAL Core export engines — the
        // manifest's `engine` attribution names the actual producer (honest provenance
        // matters for a compliance artifact). `Deterministic` marks whether the bytes are
        // a pure function of (asOf + tenant state at capture): those, and only those,
        // feed the manifest hash — so the pack is reproducible "modulo render timestamp"
        // (docs/part-ii/M19-posture.md). The CA PPTX and the assignment HTML embed a
        // wall-clock generation stamp, so they ship in the zip but are excluded from the hash.
        var artifacts = new List<(EvidenceArtifactRefDto Ref, byte[] Bytes, bool Deterministic)>();

        // 1. Posture score → HTML + PDF. The PDF is produced by the dependency-free
        //    SimplePdf writer (Base-14 Helvetica) — honouring the doc's "no heavy dep"
        //    guidance while still shipping the assessor-friendly PDF, and avoiding a trial
        //    watermark on an audit document. Both are deterministic (only timestamp is asOf).
        var scoreHtml = RenderScoreHtml(posture, tenantName, asOf);
        artifacts.Add((new("posture-score", "html", "score-summary.html", "PostureRenderer"),
            Encoding.UTF8.GetBytes(scoreHtml), true));
        artifacts.Add((new("posture-score", "pdf", "score-summary.pdf", "SimplePdf"),
            RenderScorePdf(posture, tenantName, asOf), true));

        // 2. POA&M → CSV (CSV-injection-safe).
        var poamCsv = RenderPoamCsv(poam);
        artifacts.Add((new("poam", "csv", "poam.csv", "PostureEndpoints.RenderPoamCsv"),
            Encoding.UTF8.GetBytes(poamCsv), true));

        // 3. Baseline comparison → Markdown, a REAL BaselineService.CompareSettingsCatalog
        //    run per tenant Settings-Catalog policy (best-matching OIB baseline).
        var baselineMd = await BuildBaselineComparisonMarkdownAsync(g, posture, ct);
        artifacts.Add((new("baseline-comparison", "md", "oib-drift.md", "BaselineService.CompareSettingsCatalog"),
            Encoding.UTF8.GetBytes(baselineMd), true));

        // 4. The benchmarked posture JSON itself (machine-readable evidence).
        var postureJson = JsonSerializer.Serialize(posture, new JsonSerializerOptions(Web) { WriteIndented = true });
        artifacts.Add((new("posture-json", "json", "posture.json", "PostureEndpoints"),
            Encoding.UTF8.GetBytes(postureJson), true));

        // 5. Assignment report → HTML + CSV (AssignmentReportExporter over the tenant's
        //    posture policy inventory). The HTML embeds DateTime.Now → non-deterministic.
        var rows = BuildAssignmentRows(rawPosture);
        var assignHtml = AssignmentReportExporter.GenerateHtml("Posture Evidence", rows);
        artifacts.Add((new("assignments", "html", "assignment-report.html", "AssignmentReportExporter.GenerateHtml"),
            Encoding.UTF8.GetBytes(assignHtml), false));
        var assignCsv = AssignmentReportExporter.GenerateCsv("Posture Evidence", rows);
        artifacts.Add((new("assignments", "csv", "assignment-report.csv", "AssignmentReportExporter.GenerateCsv"),
            Encoding.UTF8.GetBytes(assignCsv), true));

        // 6. Conditional Access → PPTX (ConditionalAccessPptExportService). Best-effort:
        //    a live-Graph + Syncfusion render, skipped if it throws. Embeds a generation
        //    timestamp → non-deterministic (excluded from the hash).
        var caPptx = await BuildCaPptxAsync(g, tenantName, ct);
        if (caPptx is not null)
            artifacts.Add((new("conditional-access", "pptx", "ca-policies.pptx", "ConditionalAccessPptExportService"),
                caPptx, false));

        // 7. The pinned config-snapshot BODIES themselves — the reproducible, point-in-time
        //    material (not just their ids in the manifest). Deterministic by construction.
        foreach (var rec in pinned)
        {
            var path = $"snapshots/{SanitizeEntry(rec.ObjectType)}-{rec.SnapshotId}.json";
            artifacts.Add((new("config-snapshot", "json", path, "SnapshotStore"),
                Encoding.UTF8.GetBytes(rec.BodyJson), true));
        }

        // Optionally filter to requested formats (default: all).
        if (formats is { Count: > 0 })
        {
            var keep = new HashSet<string>(formats, StringComparer.OrdinalIgnoreCase);
            artifacts = artifacts.Where(a => keep.Contains(a.Ref.Format)).ToList();
        }

        // Manifest hash: SHA-256 over each DETERMINISTIC artifact's (path + content).
        // REPRODUCIBLE — the same asOf ⇒ the same inputs ⇒ the same hash (no render
        // timestamp in the hashed material; the score summary timestamp uses asOf).
        var manifestHash = ComputeManifestHash(
            artifacts.Where(a => a.Deterministic).Select(a => (a.Ref.Path, a.Bytes)));

        var manifest = new EvidencePackManifestDto(
            PackId: packId, AsOf: asOf.ToString("o"), Benchmark: bm, TenantName: tenantName,
            Score: posture.Score, ZipPath: zipPath,
            Artifacts: artifacts.Select(a => a.Ref).ToList(),
            SnapshotIds: pinned.Select(r => r.SnapshotId).ToList(), ManifestHash: manifestHash);

        // Write the zip (artifacts + the manifest itself). Entry timestamps are pinned
        // to asOf so the archive bytes don't drift with wall-clock time.
        await using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            foreach (var (aref, bytes, _) in artifacts)
                await WriteEntryAsync(zip, aref.Path, bytes, asOf, ct);
            var manifestBytes = Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions(Web) { WriteIndented = true }));
            await WriteEntryAsync(zip, "manifest.json", manifestBytes, asOf, ct);
        }

        return manifest;
    }

    // Build one AssignmentReportRow per posture policy (CA + compliance) so the real
    // AssignmentReportExporter engine renders over live tenant inventory.
    private static List<AssignmentReportRow> BuildAssignmentRows(SecurityPostureDto p)
    {
        var rows = new List<AssignmentReportRow>();
        foreach (var ca in p.CaPolicies)
            rows.Add(new AssignmentReportRow
            {
                PolicyId = ca.Id,
                PolicyName = ca.Name,
                PolicyType = "Conditional Access",
                AssignmentSummary = ca.State,
            });
        foreach (var c in p.CompliancePolicies)
            rows.Add(new AssignmentReportRow
            {
                PolicyId = c.Id,
                PolicyName = c.Name,
                PolicyType = "Compliance Policy",
                Platform = c.Platform,
            });
        return rows;
    }

    private static async Task<byte[]?> BuildCaPptxAsync(
        Microsoft.Graph.Beta.GraphServiceClient g, string tenantName, CancellationToken ct)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "cmprojectx-evp-ca-" + Guid.NewGuid().ToString("n") + ".pptx");
        try
        {
            // Same fully-built export service the /conditional-access/pptx endpoint wires.
            var export = new ConditionalAccessPptExportService(
                new ConditionalAccessPolicyService(g),
                new NamedLocationService(g),
                new AuthenticationStrengthService(g),
                new AuthenticationContextService(g),
                new ApplicationService(g),
                new DirectoryObjectResolver(g),
                new TermsOfUseService(g));
            await export.ExportAsync(tmp, tenantName, ct);
            return await File.ReadAllBytesAsync(tmp, ct);
        }
        catch
        {
            // Best-effort supplementary artifact — the deterministic core pack still ships.
            return null;
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort */ }
        }
    }

    private static async Task<string> BuildBaselineComparisonMarkdownAsync(
        Microsoft.Graph.Beta.GraphServiceClient g, BenchmarkedPostureDto fallback, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Baseline Comparison (OIB Settings Catalog)");
        sb.AppendLine();
        sb.AppendLine("Source engine: `BaselineService.CompareSettingsCatalog` — each tenant Settings " +
                      "Catalog policy diffed against its best-matching OIB baseline (Matching / Drifted / Missing / Extra).");
        sb.AppendLine();
        try
        {
            var baselines = new BaselineService();
            var scBaselines = baselines.GetBaselinesByType(BaselinePolicyType.SettingsCatalog);
            var sc = new SettingsCatalogService(g);
            var policies = await sc.ListSettingsCatalogPoliciesAsync(ct);

            if (scBaselines.Count == 0 || policies.Count == 0)
            {
                sb.AppendLine(policies.Count == 0
                    ? "_No tenant Settings Catalog policies found._"
                    : "_No OIB Settings Catalog baselines embedded._");
                return sb.ToString();
            }

            var compared = 0;
            // Ordered + capped for a bounded, deterministic run.
            foreach (var pol in policies.OrderBy(p => p.Name, StringComparer.Ordinal).Take(25))
            {
                if (string.IsNullOrEmpty(pol.Id)) continue;
                var settings = await sc.GetPolicySettingsAsync(pol.Id, ct);

                BaselineComparisonResult? best = null;
                BaselinePolicy? bestBaseline = null;
                var bestOverlap = 0;
                foreach (var b in scBaselines)
                {
                    var r = baselines.CompareSettingsCatalog(b, settings, pol.Id, pol.Name);
                    var overlap = r.Matching.Count + r.Drifted.Count;
                    if (overlap > bestOverlap) { bestOverlap = overlap; best = r; bestBaseline = b; }
                }
                if (best is null || bestOverlap == 0) continue;

                compared++;
                sb.AppendLine($"## {pol.Name} → {bestBaseline!.Name}");
                sb.AppendLine();
                sb.AppendLine($"- Matching: {best.Matching.Count}");
                sb.AppendLine($"- Drifted: {best.Drifted.Count}");
                sb.AppendLine($"- Missing (in baseline, absent in tenant): {best.Missing.Count}");
                sb.AppendLine($"- Extra (in tenant, not in baseline): {best.Extra.Count}");
                if (best.Drifted.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Drifted settings:");
                    foreach (var d in best.Drifted.OrderBy(x => x.SettingDefinitionId, StringComparer.Ordinal))
                        sb.AppendLine($"  - `{d.SettingDefinitionId}`: baseline=`{d.BaselineValue}` tenant=`{d.TenantValue}`");
                }
                sb.AppendLine();
            }

            if (compared == 0)
                sb.AppendLine("_No tenant Settings Catalog policy overlapped an OIB baseline._");
            return sb.ToString();
        }
        catch
        {
            // Live fetch failed — fall back to the deterministic gap summary (no re-fetch).
            return RenderBaselineFallbackMarkdown(fallback);
        }
    }

    private static async Task WriteEntryAsync(ZipArchive zip, string path, byte[] bytes, DateTime asOf, CancellationToken ct)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(DateTime.SpecifyKind(asOf, DateTimeKind.Utc));
        await using var s = entry.Open();
        await s.WriteAsync(bytes, ct);
    }

    private static string ComputeManifestHash(IEnumerable<(string Path, byte[] Bytes)> artifacts)
    {
        using var sha = SHA256.Create();
        foreach (var (path, bytes) in artifacts.OrderBy(a => a.Path, StringComparer.Ordinal))
        {
            var header = Encoding.UTF8.GetBytes(path + "\n");
            sha.TransformBlock(header, 0, header.Length, null, 0);
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        sha.TransformFinalBlock([], 0, 0);
        return "sha256:" + Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    // Path-safe zip entry component (ObjectType goes into a file name).
    private static string SanitizeEntry(string s)
    {
        var cleaned = new string(s.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-').ToArray());
        return string.IsNullOrEmpty(cleaned) ? "object" : cleaned;
    }

    // The methodology caveat embedded in every evidence pack. Coverage/score are
    // indicative signals (policy presence + configuration), NOT a per-control audit
    // attestation — stating that plainly is what keeps the pack honest for an assessor.
    internal const string MethodologyCaveat =
        "> **Methodology — read before relying on this for an audit.** Score and coverage are an " +
        "*indicative* posture signal derived from the presence and configuration of policies, NOT a " +
        "per-control attestation. A mapped control is counted as covered once its category scores above " +
        "zero; this does not verify that every policy fully satisfies the control (e.g. that a Conditional " +
        "Access policy actually enforces MFA). Treat the listed **gaps as authoritative** (a gap means a " +
        "requirement is unmet) and coverage as directional.";

    internal static string RenderScoreMarkdown(BenchmarkedPostureDto p, string tenantName, DateTime asOf)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Security Posture — {tenantName}");
        sb.AppendLine();
        sb.AppendLine($"- **As of:** {asOf:o}");
        sb.AppendLine($"- **Benchmark:** {p.Benchmark} ({p.BenchmarkVersion})");
        sb.AppendLine($"- **Score:** {p.Score} / 100");
        sb.AppendLine($"- **Coverage:** {p.Coverage.ControlsCovered}/{p.Coverage.ControlsTotal} controls ({p.Coverage.Percent}%)");
        sb.AppendLine();
        sb.AppendLine(MethodologyCaveat);
        sb.AppendLine();
        sb.AppendLine("## Breakdown");
        sb.AppendLine();
        sb.AppendLine("| Category | Score | Max | Controls |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var c in p.Breakdown ?? [])
            sb.AppendLine($"| {c.Category} | {c.Score} | {c.MaxScore} | {string.Join(", ", (c.Controls ?? []).Select(x => $"{x.Framework}:{x.Id}"))} |");
        sb.AppendLine();
        sb.AppendLine("## Gaps");
        sb.AppendLine();
        foreach (var gap in p.Gaps ?? [])
            sb.AppendLine($"- **[{gap.Severity}]** {gap.Category}: {gap.Description}");
        return sb.ToString();
    }

    // Standalone HTML score summary (PostureRenderer-style). Deterministic: the only
    // timestamp is asOf, so the same input renders byte-identical bytes.
    internal static string RenderScoreHtml(BenchmarkedPostureDto p, string tenantName, DateTime asOf)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"UTF-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append($"<title>Security Posture — {He(tenantName)}</title>");
        sb.Append("<style>body{font-family:-apple-system,Segoe UI,Roboto,sans-serif;margin:24px;color:#111;line-height:1.5}" +
                  "h1{font-size:22px}h2{font-size:16px;margin-top:24px}table{border-collapse:collapse;width:100%}" +
                  "th,td{border:1px solid #ddd;padding:6px 10px;text-align:left;font-size:13px}th{background:#f3f4f6}" +
                  "blockquote{background:#fff7ed;border-left:4px solid #d97706;padding:10px 14px;margin:12px 0;font-size:13px}" +
                  ".meta li{margin:2px 0}.sev-high{color:#dc2626;font-weight:600}.sev-medium{color:#d97706;font-weight:600}" +
                  ".sev-low{color:#2563eb;font-weight:600}</style></head><body>");
        sb.Append($"<h1>Security Posture — {He(tenantName)}</h1>");
        sb.Append("<ul class=\"meta\">");
        sb.Append($"<li><b>As of:</b> {He(asOf.ToString("o"))}</li>");
        sb.Append($"<li><b>Benchmark:</b> {He(p.Benchmark)} ({He(p.BenchmarkVersion)})</li>");
        sb.Append($"<li><b>Score:</b> {p.Score} / 100</li>");
        sb.Append($"<li><b>Coverage:</b> {p.Coverage.ControlsCovered}/{p.Coverage.ControlsTotal} controls ({p.Coverage.Percent}%)</li>");
        sb.Append("</ul>");
        // The methodology caveat, rendered from the same canonical text (strip the leading "> ").
        sb.Append($"<blockquote>{He(MethodologyCaveat.Replace("> ", "").Replace("**", "").Replace("*", ""))}</blockquote>");
        sb.Append("<h2>Breakdown</h2><table><thead><tr><th>Category</th><th>Score</th><th>Max</th><th>Controls</th></tr></thead><tbody>");
        foreach (var c in p.Breakdown ?? [])
            sb.Append($"<tr><td>{He(c.Category)}</td><td>{c.Score}</td><td>{c.MaxScore}</td>" +
                      $"<td>{He(string.Join(", ", (c.Controls ?? []).Select(x => $"{x.Framework}:{x.Id}")))}</td></tr>");
        sb.Append("</tbody></table>");
        sb.Append("<h2>Gaps</h2><ul>");
        foreach (var gap in p.Gaps ?? [])
            sb.Append($"<li><span class=\"sev-{He(gap.Severity)}\">[{He(gap.Severity)}]</span> {He(gap.Category)}: {He(gap.Description)}</li>");
        sb.Append("</ul></body></html>");
        return sb.ToString();
    }

    // Standalone PDF score summary — same content as RenderScoreHtml, via the dependency-free
    // SimplePdf writer. Deterministic (only timestamp is asOf), so it feeds the manifest hash.
    internal static byte[] RenderScorePdf(BenchmarkedPostureDto p, string tenantName, DateTime asOf)
    {
        var lines = new List<string>
        {
            $"Security Posture - {tenantName}",
            "",
            $"As of:     {asOf:o}",
            $"Benchmark: {p.Benchmark} ({p.BenchmarkVersion})",
            $"Score:     {p.Score} / 100",
            $"Coverage:  {p.Coverage.ControlsCovered}/{p.Coverage.ControlsTotal} controls ({p.Coverage.Percent}%)",
            "",
            "Breakdown",
            "---------",
        };
        foreach (var c in p.Breakdown ?? [])
            lines.Add($"  {c.Category}: {c.Score}/{c.MaxScore}  [{string.Join(", ", (c.Controls ?? []).Select(x => $"{x.Framework}:{x.Id}"))}]");
        lines.Add("");
        lines.Add("Gaps");
        lines.Add("----");
        foreach (var gap in p.Gaps ?? [])
            lines.Add($"  [{gap.Severity}] {gap.Category}: {gap.Description}");
        lines.Add("");
        lines.Add("Methodology: " + MethodologyCaveat.Replace("> ", "").Replace("**", "").Replace("*", ""));
        return SimplePdf.FromLines(lines);
    }

    private static string RenderPoamCsv(IReadOnlyList<PoamItemDto> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("id,severity,category,finding,controls,remediation,owner,openedUtc,dueUtc,state");
        foreach (var i in items)
        {
            var controls = string.Join(";", i.Controls.Select(c => $"{c.Framework}:{c.Id}"));
            sb.AppendLine(string.Join(",", new[]
            {
                CsvQ(i.Id), CsvQ(i.Severity), CsvQ(i.Category), CsvQ(i.Finding),
                CsvQ(controls), CsvQ(i.Remediation), CsvQ(i.Owner ?? ""),
                CsvQ(i.OpenedUtc), CsvQ(i.DueUtc), CsvQ(i.State),
            }));
        }
        return sb.ToString();
    }

    // Deterministic fallback when the live Settings-Catalog fetch is unavailable —
    // summarises the score's gaps against the mapped OIB families (no live re-fetch).
    private static string RenderBaselineFallbackMarkdown(BenchmarkedPostureDto p)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Baseline Comparison (OIB)");
        sb.AppendLine();
        sb.AppendLine("Source engine: `BaselineService.CompareSettingsCatalog` (unavailable at capture — " +
                      "showing the score's category gaps against the mapped OIB families instead).");
        sb.AppendLine();
        foreach (var c in p.Breakdown ?? [])
        {
            sb.AppendLine($"## {c.Category} — {c.Score}/{c.MaxScore}");
            sb.AppendLine();
            foreach (var item in c.Items ?? []) sb.AppendLine($"- {item}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // CSV-injection-safe quote: neutralise leading =,+,-,@ and double-quote fields.
    private static string CsvQ(string s)
    {
        if (s.Length > 0 && (s[0] is '=' or '+' or '-' or '@')) s = "'" + s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    // Minimal HTML escape for the score summary.
    private static string He(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;").Replace("'", "&#39;");
    }

    private static DateTime ParseAsOf(string? asOf) =>
        DateTime.TryParse(asOf, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
            ? dt.ToUniversalTime()
            : DateTime.UtcNow;
}
