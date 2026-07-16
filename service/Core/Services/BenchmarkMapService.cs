using System.Text.Json;

namespace Intune.Commander.Core.Services;

/// <summary>
/// M19 — benchmark mapping. Attaches external control references (CIS / OIB /
/// Essential 8 / NIST 800-53) to the five posture score categories produced by
/// <c>ComputeScore</c> and to each severity gap. The OIB column is sourced live
/// from <see cref="BaselineService"/> (the embedded OIB baselines are the
/// authority); CIS / Essential 8 / NIST are a curated overlay — control IDs plus
/// our own one-line titles only (the verbatim CIS control text is not shipped;
/// see the "Benchmark sources &amp; licensing" open question in M19-posture.md).
///
/// The version strings and the per-category control overlay are loaded from the
/// versioned embedded resource <c>Assets/benchmark-map.json</c>
/// (<c>Intune.Commander.Core.Assets.benchmark-map.json</c>) — externalized from
/// hardcoded tables so the overlay can be revised, and versioned via
/// <see cref="MapVersion"/>, without recompiling this engine. It does NOT recompute
/// the score, redefine categories, or fetch Graph state — it maps the labels that
/// <c>ComputeScore</c> already emits.
/// </summary>
public sealed class BenchmarkMapService
{
    public const string DefaultBenchmark = "cis";

    private static readonly BenchmarkMap Map = LoadMap();

    /// <summary>The version stamp of the loaded benchmark-map resource.</summary>
    public static string MapVersion => Map.MapVersion;

    // The recognised benchmark keys + their human-readable version strings (from the resource).
    private static readonly Dictionary<string, string> Versions = Map.Versions;

    // Curated overlay keyed by ScoreCategory.category (from the resource): MULTIPLE controls
    // per category so the coverage denominators (AllControls) are meaningful. The OIB column
    // is filled in at runtime from BaselineService categories (see CategoryControls).
    private static readonly Dictionary<string, ControlOverlay> Overlay = Map.Overlay;

    // "Named Locations" gaps reuse the "Auth & Locations" overlay (the category they roll up into).
    private static readonly Dictionary<string, string> GapCategoryAlias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Named Locations"] = "Auth & Locations",
    };

    private readonly BaselineService _baselines;

    public BenchmarkMapService(BaselineService baselines) => _baselines = baselines;

    /// <summary>True if <paramref name="benchmark"/> is a recognised key.</summary>
    public static bool IsKnown(string? benchmark) =>
        !string.IsNullOrWhiteSpace(benchmark) && Versions.ContainsKey(benchmark);

    /// <summary>Normalises a benchmark query value to a known key (defaults to "cis").</summary>
    public static string Normalize(string? benchmark) =>
        IsKnown(benchmark) ? benchmark!.ToLowerInvariant() : DefaultBenchmark;

    public static string VersionFor(string benchmark) =>
        Versions.TryGetValue(Normalize(benchmark), out var v) ? v : Versions[DefaultBenchmark];

    /// <summary>
    /// Control references for a score <paramref name="category"/> under
    /// <paramref name="benchmark"/>. For OIB, the IDs come from
    /// <see cref="BaselineService"/> categories of the relevant policy type, falling
    /// back to a static OIB family label when no baselines are loaded.
    /// </summary>
    public IReadOnlyList<ControlRef> CategoryControls(string category, string benchmark)
    {
        var bm = Normalize(benchmark);
        if (!Overlay.TryGetValue(category, out var ov))
            return [];

        if (bm == "oib")
            return OibControls(ov);

        var pairs = bm switch
        {
            "cis" => ov.Cis,
            "e8" => ov.E8,
            "nist" => ov.Nist,
            _ => ov.Cis,
        };
        return pairs.Select(p => new ControlRef(bm, p.Id, p.Title)).ToList();
    }

    /// <summary>Control references for a gap, honouring the gap-category alias table.</summary>
    public IReadOnlyList<ControlRef> GapControls(string gapCategory, string benchmark)
    {
        var resolved = GapCategoryAlias.TryGetValue(gapCategory, out var alias) ? alias : gapCategory;
        return CategoryControls(resolved, benchmark);
    }

    /// <summary>
    /// Every distinct control in scope for <paramref name="benchmark"/> across all
    /// five categories — the denominator for the coverage rollup.
    /// </summary>
    public IReadOnlyList<ControlRef> AllControls(string benchmark)
    {
        var bm = Normalize(benchmark);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var all = new List<ControlRef>();
        foreach (var category in Overlay.Keys)
            foreach (var c in CategoryControls(category, bm))
                if (seen.Add($"{c.Framework}|{c.Id}"))
                    all.Add(c);
        return all;
    }

    private IReadOnlyList<ControlRef> OibControls(ControlOverlay ov)
    {
        // Pull live OIB category labels from the embedded baselines for the relevant
        // policy type; each becomes one OIB control id "OIB-{Type}-{Category}".
        if (ov.OibType != BaselinePolicyTypeKey.None)
        {
            var type = ov.OibType switch
            {
                BaselinePolicyTypeKey.Compliance => Models.BaselinePolicyType.Compliance,
                BaselinePolicyTypeKey.EndpointSecurity => Models.BaselinePolicyType.EndpointSecurity,
                _ => Models.BaselinePolicyType.SettingsCatalog,
            };
            var cats = _baselines.GetCategories(type);
            if (cats.Count > 0)
            {
                var prefix = ov.OibFallback;
                return cats
                    .Select(c => new ControlRef("oib", $"{prefix}-{Slug(c)}", c))
                    .ToList();
            }
        }
        // No baselines loaded (or category with no OIB policy type) — static family label.
        return [new ControlRef("oib", ov.OibFallback, null)];
    }

    private static string Slug(string s) =>
        new string(s.Where(ch => !char.IsWhiteSpace(ch)).ToArray());

    // ── resource loading ──────────────────────────────────────────────────────────

    private static BenchmarkMap LoadMap()
    {
        const string resource = "Intune.Commander.Core.Assets.benchmark-map.json";
        var asm = typeof(BenchmarkMapService).Assembly;
        using var stream = asm.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"embedded resource '{resource}' not found (Core.csproj EmbeddedResource missing?)");
        using var reader = new StreamReader(stream);
        var parsed = JsonSerializer.Deserialize<BenchmarkMapJson>(reader.ReadToEnd(),
                         new JsonSerializerOptions(JsonSerializerDefaults.Web))
                     ?? throw new InvalidOperationException("benchmark-map.json failed to parse");

        var versions = new Dictionary<string, string>(
            parsed.Versions ?? new(), StringComparer.OrdinalIgnoreCase);
        var overlay = new Dictionary<string, ControlOverlay>(StringComparer.OrdinalIgnoreCase);
        foreach (var (category, ov) in parsed.Overlay ?? new())
            overlay[category] = new ControlOverlay(
                Cis: Pairs(ov.Cis), E8: Pairs(ov.E8), Nist: Pairs(ov.Nist),
                OibType: Enum.TryParse<BaselinePolicyTypeKey>(ov.OibType, ignoreCase: true, out var t)
                    ? t : BaselinePolicyTypeKey.None,
                OibFallback: ov.OibFallback ?? "OIB");
        return new BenchmarkMap(parsed.MapVersion ?? "unknown", versions, overlay);

        static (string Id, string Title)[] Pairs(string[][]? arr) =>
            (arr ?? [])
                .Where(p => p.Length >= 1)
                .Select(p => (p[0], p.Length > 1 ? p[1] : p[0]))
                .ToArray();
    }

    public sealed record ControlRef(string Framework, string Id, string? Title);

    private enum BaselinePolicyTypeKey { None, Compliance, EndpointSecurity, SettingsCatalog }

    private sealed record ControlOverlay(
        (string Id, string Title)[] Cis,
        (string Id, string Title)[] E8,
        (string Id, string Title)[] Nist,
        BaselinePolicyTypeKey OibType,
        string OibFallback);

    private sealed record BenchmarkMap(
        string MapVersion,
        Dictionary<string, string> Versions,
        Dictionary<string, ControlOverlay> Overlay);

    // The on-disk JSON shape (camelCase); projected into the runtime tables above.
    private sealed record BenchmarkMapJson(
        string? MapVersion,
        Dictionary<string, string>? Versions,
        Dictionary<string, OverlayJson>? Overlay);

    private sealed record OverlayJson(
        string[][]? Cis, string[][]? E8, string[][]? Nist, string? OibType, string? OibFallback);
}
