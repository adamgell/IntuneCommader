using System.Text.Json;

namespace CmProjectX.Api;

// M18 advisory signal — a curated catalog of security advisories (the embedded fixture
// Autonomy/advisories.json) matched against the tenant's current posture gaps. An advisory
// fires when its category+keyword condition is present in the live posture. Advisories may
// carry a REMEDIATION (a proposable corrective write): when one fires, the engine simulates
// (M16) + enqueues it into the M13 pending-changes inbox for HUMAN approval — never
// auto-applied (locked guarantee). Advisories without a remediation (e.g. Conditional
// Access, read-only by design) stay detection-only.
internal static class AdvisoryCatalog
{
    internal sealed record Advisory(
        string Id, string Title, string Severity, string MatchCategory, string? MatchKeyword,
        string? Guidance, AdvisoryRemediation? Remediation = null);

    // An advisory's proposable corrective write. Null ⇒ detection-only.
    internal sealed record AdvisoryRemediation(string Verb, string Path, string? ObjectId, string BodyJson);

    // A matched advisory + the detection it produces.
    internal sealed record AdvisoryMatch(Advisory Advisory, AutonomyDetectionDto Detection);

    private static readonly IReadOnlyList<Advisory> Catalog = Load();
    private static readonly string[] SeverityOrder = ["info", "low", "medium", "high", "critical"];

    // The advisories whose category+keyword condition is present in the posture gaps and whose
    // severity meets the floor — one match (advisory + detection) each. Pure; hermetically tested.
    internal static List<AdvisoryMatch> Match(
        IReadOnlyList<Advisory> catalog, IReadOnlyList<BenchmarkedGapDto> gaps, string? minSeverity)
    {
        var floor = Rank(minSeverity ?? "info");
        var matches = new List<AdvisoryMatch>();
        foreach (var a in catalog)
        {
            if (Rank(a.Severity) < floor) continue;
            var hit = gaps.FirstOrDefault(g =>
                string.Equals(g.Category, a.MatchCategory, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(a.MatchKeyword) ||
                 g.Description.Contains(a.MatchKeyword, StringComparison.OrdinalIgnoreCase)));
            if (hit is null) continue;
            var detection = new AutonomyDetectionDto(
                Signal: "advisory",
                ObjectId: $"advisory:{a.Id}",
                ObjectName: a.Title,
                Severity: a.Severity.ToLowerInvariant(),
                BaseSnapshotId: null, HeadSnapshotId: null,
                Changes: new[] { new DriftChangeDto(a.MatchCategory, "Advisory", a.Guidance ?? a.Title, hit.Description) });
            matches.Add(new AdvisoryMatch(a, detection));
        }
        return matches;
    }

    // Convenience over the embedded catalog.
    internal static List<AdvisoryMatch> Match(IReadOnlyList<BenchmarkedGapDto> gaps, string? minSeverity) =>
        Match(Catalog, gaps, minSeverity);

    internal static int Count => Catalog.Count;

    private static int Rank(string s) => Math.Max(0, Array.IndexOf(SeverityOrder, s.ToLowerInvariant()));

    private static IReadOnlyList<Advisory> Load()
    {
        try
        {
            var asm = typeof(AdvisoryCatalog).Assembly;
            var name = Array.Find(asm.GetManifestResourceNames(),
                n => n.EndsWith("advisories.json", StringComparison.OrdinalIgnoreCase));
            if (name is null) return [];
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var doc = JsonSerializer.Deserialize<AdvisoryFile>(reader.ReadToEnd(),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return doc?.Advisories ?? [];
        }
        catch { return []; }
    }

    private sealed record AdvisoryFile(IReadOnlyList<Advisory> Advisories);
}
