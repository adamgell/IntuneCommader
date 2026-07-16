using System.Text.Json;

namespace CmProjectX.Api;

// M21 Ecosystem — shared on-disk catalog for packs, playbooks, and the marketplace.
// All three artifacts are author-once / adopt-anywhere files under the same
// %LocalAppData%\cmProjectX\ root the rest of the sidecar uses (plugins.json lives
// there too). This is a thin discovery layer: it locates and parses the files, and
// resolves {{param}} tokens. The governed write paths (M15 gitops, the M13 inbox,
// the plugins.json row) live in the endpoint modules — this class never writes Graph.
//
//   packs\<id>\pack.json           — a pack manifest beside its M15 export tree
//   playbooks\<id>.json            — a playbook definition
//   marketplace.json               — the curated plugin registry (operator-editable;
//                                    seeded with a built-in curated allowlist on miss)
internal static class EcosystemCatalog
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    // Test seam: when set, overrides the on-disk root so hermetic tests can point the
    // catalog at a temp dir instead of the real %LocalAppData%\cmProjectX. Null in prod.
    internal static string? RootOverride;

    // Test seam: overrides where EnsureSeeded copies the bundled sample pack/playbook
    // from. Null in prod → the app's shipped assets (AppContext.BaseDirectory\Resources
    // \ecosystem, copied there by Api.csproj). Hermetic tests point it at the in-repo
    // service\Api\Resources\ecosystem so the seed runs without relying on bin content.
    internal static string? SeedSourceOverride;

    private static string Root => RootOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "cmProjectX");

    public static string PacksDir => Path.Combine(Root, "packs");
    public static string PlaybooksDir => Path.Combine(Root, "playbooks");
    public static string MarketplacePath => Path.Combine(Root, "marketplace.json");

    // The bundled seed tree (packs\<id>\… + playbooks\<id>.json). Shipped beside the
    // sidecar and copied to the build output by Api.csproj; overridable for tests.
    private static string SeedSourceDir => SeedSourceOverride
        ?? Path.Combine(AppContext.BaseDirectory, "Resources", "ecosystem");

    private static readonly object SeedLock = new();

    // First-run seed so GET /packs|/playbooks are non-empty out of the box. Copies the
    // bundled sample pack (a full M15 export tree + manifest.json) and sample playbook
    // into the on-disk root. Marker-guarded (.ecosystem-seeded) so it runs exactly once
    // per root and never re-adds a sample an operator has since deleted; never clobbers
    // an existing file. Best-effort: a missing seed source or IO error leaves the
    // catalog empty rather than throwing (the sidecar must still boot). Idempotent —
    // safe to call on every startup. Called from Program.cs at boot.
    public static void EnsureSeeded()
    {
        var marker = Path.Combine(Root, ".ecosystem-seeded");
        if (File.Exists(marker)) return;
        lock (SeedLock)
        {
            if (File.Exists(marker)) return;
            try
            {
                Directory.CreateDirectory(Root);
                var src = SeedSourceDir;
                CopyTreeNoClobber(Path.Combine(src, "packs"), PacksDir);
                CopyTreeNoClobber(Path.Combine(src, "playbooks"), PlaybooksDir);
                File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));
            }
            catch { /* best-effort seed — an unseeded catalog is still a valid empty one */ }
        }
    }

    // Recursive copy that preserves the operator's edits (never overwrites an existing
    // target file) and no-ops when the source is absent (e.g. a trimmed publish).
    private static void CopyTreeNoClobber(string src, string dst)
    {
        if (!Directory.Exists(src)) return;
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(dst, Path.GetRelativePath(src, dir)));
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dst, Path.GetRelativePath(src, file));
            if (!File.Exists(target)) File.Copy(file, target);
        }
    }

    // ── Packs ─────────────────────────────────────────────────────────────────

    // Each pack is a directory <PacksDir>\<id> containing a pack.json manifest beside
    // its M15 export tree. RepoPath is stamped to the pack dir so adopt can run the
    // M15 plan/apply against it. Bad/missing manifests are skipped, never thrown.
    public static IReadOnlyList<PackManifest> ListPacks()
    {
        var dir = PacksDir;
        if (!Directory.Exists(dir)) return Array.Empty<PackManifest>();
        var packs = new List<PackManifest>();
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            var manifest = Path.Combine(sub, "pack.json");
            if (!File.Exists(manifest)) continue;
            if (TryReadPack(manifest, sub, out var pack)) packs.Add(pack!);
        }
        return packs.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static PackManifest? FindPack(string id)
    {
        if (!IsSafeId(id)) return null;
        var manifest = Path.Combine(PacksDir, id, "pack.json");
        if (!File.Exists(manifest)) return null;
        return TryReadPack(manifest, Path.Combine(PacksDir, id), out var pack) ? pack : null;
    }

    private static bool TryReadPack(string manifestPath, string repoDir, out PackManifest? pack)
    {
        pack = null;
        try
        {
            var raw = JsonSerializer.Deserialize<PackManifest>(File.ReadAllText(manifestPath), Json);
            if (raw is null || string.IsNullOrWhiteSpace(raw.Id)) return false;
            // Stamp the on-disk repo path (the manifest itself doesn't carry it).
            pack = raw with { RepoPath = repoDir };
            return true;
        }
        catch { return false; }
    }

    // ── Playbooks ───────────────────────────────────────────────────────────────

    public static IReadOnlyList<Playbook> ListPlaybooks()
    {
        var dir = PlaybooksDir;
        if (!Directory.Exists(dir)) return Array.Empty<Playbook>();
        var books = new List<Playbook>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
            if (TryReadPlaybook(file, out var pb)) books.Add(pb!);
        return books.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static Playbook? FindPlaybook(string id)
    {
        if (!IsSafeId(id)) return null;
        // Prefer <id>.json; fall back to scanning for a body whose id matches.
        var direct = Path.Combine(PlaybooksDir, id + ".json");
        if (File.Exists(direct) && TryReadPlaybook(direct, out var pb) && pb!.Id == id) return pb;
        return ListPlaybooks().FirstOrDefault(p => p.Id == id);
    }

    private static bool TryReadPlaybook(string path, out Playbook? pb)
    {
        pb = null;
        try
        {
            var raw = JsonSerializer.Deserialize<Playbook>(File.ReadAllText(path), Json);
            if (raw is null || string.IsNullOrWhiteSpace(raw.Id)) return false;
            pb = raw;
            return true;
        }
        catch { return false; }
    }

    // ── Marketplace ─────────────────────────────────────────────────────────────

    // The curated registry. Operator-editable at <MarketplacePath>; when absent we
    // return a small built-in curated allowlist so the marketplace is never empty.
    public static IReadOnlyList<MarketplaceEntry> ListMarketplace()
    {
        var builtIn = BuiltInRegistry();
        var curatedIds = new HashSet<string>(builtIn.Select(e => e.Id), StringComparer.OrdinalIgnoreCase);

        // The on-disk marketplace.json is an OVERLAY, not a replacement: the curated
        // built-ins are always present (an operator-editable file can't delete them or
        // spoof a curated id), and any extra entry the file supplies is forced
        // verified=false so its non-curated provenance is unambiguous. The trust
        // boundary stays the built-in allowlist (docs/part-ii/M21-ecosystem.md open Q#3).
        var merged = new Dictionary<string, MarketplaceEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in builtIn) merged[e.Id] = e;
        foreach (var e in LoadMarketplaceFile() ?? (IReadOnlyList<MarketplaceEntry>)Array.Empty<MarketplaceEntry>())
            if (!string.IsNullOrWhiteSpace(e.Id) && !curatedIds.Contains(e.Id))
                merged[e.Id] = e with { Verified = false };

        var installedNames = InstalledPluginNames();
        return merged.Values
            .Select(e => e with { Installed = installedNames.Contains(PluginNameFor(e)) })
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static MarketplaceEntry? FindMarketplace(string id) =>
        ListMarketplace().FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<MarketplaceEntry>? LoadMarketplaceFile()
    {
        if (!File.Exists(MarketplacePath)) return null;
        try
        {
            var doc = JsonSerializer.Deserialize<MarketplaceFile>(File.ReadAllText(MarketplacePath), Json);
            return doc?.Entries;
        }
        catch { return null; }
    }

    // The plugins.json row name we'd write for an entry (used for the Installed flag).
    public static string PluginNameFor(MarketplaceEntry e) => e.Id;

    private static HashSet<string> InstalledPluginNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = McpPlugins.ConfigPath;
        if (!File.Exists(path)) return names;
        try
        {
            var doc = JsonSerializer.Deserialize<PluginsFileShape>(File.ReadAllText(path), Json);
            if (doc?.Plugins is { } list)
                foreach (var p in list)
                    if (!string.IsNullOrWhiteSpace(p.Name)) names.Add(p.Name!);
        }
        catch { /* unreadable plugins.json → treat as none installed */ }
        return names;
    }

    // ── {{token}} substitution ───────────────────────────────────────────────────

    // Replace {{name}} occurrences from the binding map. Unmatched tokens are left
    // intact (so a missing optional param surfaces visibly rather than silently
    // collapsing). Whitespace inside the braces is tolerated.
    public static string Substitute(string input, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(input) || values.Count == 0) return input;
        return System.Text.RegularExpressions.Regex.Replace(input, @"\{\{\s*([A-Za-z0-9_]+)\s*\}\}", m =>
            values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
    }

    // ── path-safety guards ────────────────────────────────────────────────────

    // A pack/playbook id indexes an on-disk path (PacksDir\<id>\…, PlaybooksDir\<id>.json),
    // and it arrives straight from the {id} route segment. Constrain it to a single safe
    // path component — no separators, no "..", no invalid filename chars — so a crafted id
    // can't traverse out of the catalog directory (GET /packs/../../…).
    internal static bool IsSafeId(string id) =>
        !string.IsNullOrWhiteSpace(id)
        && id.IndexOfAny(new[] { '/', '\\' }) < 0
        && !id.Contains("..")
        && id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    // True if a (substituted) loopback surface path carries a traversal sequence — a
    // playbook step's {{param}} value must not be able to walk the path hierarchy.
    internal static bool ContainsTraversal(string path) =>
        path.Contains("..") || path.Contains("//");

    // Resolve effective parameter values: operator-supplied override the declared
    // defaults. Returns (values, firstMissingRequiredName | null).
    public static (Dictionary<string, string> Values, string? MissingRequired) ResolveParameters(
        IReadOnlyList<PackParameter> declared, IReadOnlyDictionary<string, string>? supplied)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in declared)
        {
            if (supplied is not null && supplied.TryGetValue(p.Name, out var v) && !string.IsNullOrEmpty(v))
                values[p.Name] = v;
            else if (p.Default is not null)
                values[p.Name] = p.Default;
            else if (p.Required)
                return (values, p.Name);
        }
        // Carry any extra supplied values (e.g. a token a step references but the
        // manifest didn't declare) so step substitution can still use them.
        if (supplied is not null)
            foreach (var kv in supplied)
                if (!values.ContainsKey(kv.Key) && !string.IsNullOrEmpty(kv.Value))
                    values[kv.Key] = kv.Value;
        return (values, null);
    }

    // The built-in curated allowlist (returned when no marketplace.json is present).
    // Mirrors docs/part-ii/M21-ecosystem.md and docs/plugins.example.json. Curated =
    // the trust boundary: every entry here is reviewed; plugin tools are pass-through.
    private static IReadOnlyList<MarketplaceEntry> BuiltInRegistry() => new[]
    {
        new MarketplaceEntry(
            Id: "mslearn", Name: "Microsoft Learn Docs", Category: "docs",
            Publisher: "cmprojectx-curated", Verified: true,
            Description: "Read-only Microsoft Learn documentation search (no side effects).",
            Transport: "http", UrlTemplate: "https://learn.microsoft.com/api/mcp",
            Command: null, Args: null,
            ConfigFields: Array.Empty<MarketplaceConfigField>(),
            ExposesTools: new[] { "microsoft_docs_search" }, Trust: "read-only", Installed: false),
        new MarketplaceEntry(
            Id: "servicenow-itsm", Name: "ServiceNow ITSM", Category: "ticketing",
            Publisher: "cmprojectx-curated", Verified: true,
            Description: "Open/update ServiceNow incidents from playbook steps.",
            Transport: "http", UrlTemplate: "https://{{instance}}.service-now.com/api/mcp",
            Command: null, Args: null,
            ConfigFields: new[]
            {
                new MarketplaceConfigField("instance", "string", true, null, null),
                new MarketplaceConfigField("token", "secret", true, "Authorization", "Bearer {{token}}"),
            },
            ExposesTools: new[] { "create_incident", "update_incident", "get_incident" },
            Trust: "writes-side-effecting", Installed: false),
    };

    // ── file shapes (loose, decoupled from the DTOs) ─────────────────────────────
    private sealed class MarketplaceFile { public List<MarketplaceEntry>? Entries { get; set; } }
    private sealed class PluginsFileShape { public List<PluginRow>? Plugins { get; set; } }
    internal sealed class PluginRow
    {
        public string? Name { get; set; }
        public string? Transport { get; set; }
        public string? Url { get; set; }
        public string? Command { get; set; }
        public List<string>? Args { get; set; }
        public Dictionary<string, string>? Headers { get; set; }
    }
}
