using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CmProjectX.Api;

// M15 Policy-as-Code / GitOps — the pure (Graph-free) core: the surface registry,
// the on-disk repo layout, and the tree differ. Kept apart from GitOpsEndpoints.cs
// (which names Microsoft.Graph.Beta.Models types) so the hermetic Unit tests can
// exercise the diff with plain temp directories. The Graph wiring (pull/apply) and
// HTTP surface live in the endpoint module.

// A GitOps-managed surface. `Folder` matches the subfolder ExportService writes
// into; `Writable=false` surfaces are pull/plan only — Conditional Access stays
// read-only (locked guarantee), so apply never writes it.
public sealed record GitOpsSurfaceDef(
    string Key, string Folder, string ObjectType, string CacheKey, bool Writable);

public static class GitOpsSurfaces
{
    // v1 set. Adding a surface is one row here plus one arm in each per-surface
    // switch in GitOpsEndpoints. Conditional Access is read-only.
    public static readonly IReadOnlyList<GitOpsSurfaceDef> All =
    [
        new("device-configs",      "DeviceConfigurations",      "DeviceConfiguration",     "DeviceConfigurations",      true),
        new("compliance-policies", "CompliancePolicies",        "CompliancePolicy",        "CompliancePolicies",        true),
        new("settings-catalog",    "SettingsCatalog",           "SettingsCatalog",         "SettingsCatalog",           true),
        new("conditional-access",  "ConditionalAccessPolicies", "ConditionalAccessPolicy", "ConditionalAccessPolicies", false),
    ];

    public static GitOpsSurfaceDef? Find(string key) =>
        All.FirstOrDefault(s => string.Equals(s.Key, key.Trim().TrimStart('/'), StringComparison.OrdinalIgnoreCase));

    // Resolve a requested surfaces[] — null / empty / ["*"] mean "all supported".
    public static IReadOnlyList<GitOpsSurfaceDef> Resolve(IReadOnlyList<string>? requested)
    {
        if (requested is null || requested.Count == 0 || requested.Any(s => s.Trim() == "*"))
            return All;
        return requested.Select(Find).Where(s => s is not null).Select(s => s!).ToList();
    }
}

// One object on disk: its surface/file identity, display name, and normalized body.
public sealed record GitOpsFile(GitOpsSurfaceDef Surface, string FileName, string ObjectName, string NormalizedBody);

// One row of a plan — richer than the wire GitOpsPlanObject (carries FileName so
// apply can locate the repo file). Noop objects are not emitted as rows.
public sealed record GitOpsPlanRow(
    GitOpsSurfaceDef Surface, string FileName, string ObjectName, string Verdict, List<DriftChangeDto> Changes);

public static class GitOpsTree
{
    public const int SchemaVersion = 1;
    public const string CmpxVersion = "0.15.0";

    // SHA-256 hex — the same content-hash basis the snapshot store dedups on.
    public static string Sha256(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    // Read every *.json under <root>/<folder> for the given surfaces, normalized.
    // An unparseable file is skipped rather than failing the whole plan.
    public static List<GitOpsFile> ReadTree(
        string root, IReadOnlyList<GitOpsSurfaceDef> surfaces, Func<string, string> normalize)
    {
        var files = new List<GitOpsFile>();
        foreach (var s in surfaces)
        {
            var dir = Path.Combine(root, s.Folder);
            if (!Directory.Exists(dir)) continue;
            foreach (var path in Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
                         .OrderBy(p => p, StringComparer.Ordinal))
            {
                string normalized;
                try { normalized = normalize(File.ReadAllText(path)); }
                catch (JsonException) { continue; }
                files.Add(new GitOpsFile(
                    s, Path.GetFileName(path),
                    ExtractName(normalized, Path.GetFileNameWithoutExtension(path)),
                    normalized));
            }
        }
        return files;
    }

    // Diff a repo tree (desired state) against a live tree (actual state). The diff
    // is oriented before=live, after=repo, so the change set reads as "what apply
    // does to the tenant" (matching the M6 panel). Objects in the repo but not the
    // live tree are Added; in live but not repo are Removed; in both, field-level
    // Modified (empty diff → Noop, counted but not listed). Also returns a per-object
    // live-body hash map keyed by "<surfaceKey>|<fileName>" so apply can detect drift.
    public static (List<GitOpsPlanRow> Rows, GitOpsPlanSummary Summary, Dictionary<string, string> LiveHashes)
        Diff(IReadOnlyList<GitOpsFile> repo, IReadOnlyList<GitOpsFile> live)
    {
        var repoByKey = repo.ToDictionary(KeyOf, f => f);
        var liveByKey = live.ToDictionary(KeyOf, f => f);
        var liveHashes = live.ToDictionary(KeyOf, f => Sha256(f.NormalizedBody));

        int add = 0, change = 0, destroy = 0, noop = 0;
        var rows = new List<GitOpsPlanRow>();

        foreach (var key in repoByKey.Keys.Union(liveByKey.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            repoByKey.TryGetValue(key, out var r);
            liveByKey.TryGetValue(key, out var l);
            var anchor = (r ?? l)!;

            if (r is not null && l is null)
            {
                add++;
                rows.Add(Row(anchor, "Added", JsonDrift.Diff("", r.NormalizedBody)));
            }
            else if (r is null && l is not null)
            {
                destroy++;
                rows.Add(Row(anchor, "Removed", JsonDrift.Diff(l.NormalizedBody, "")));
            }
            else
            {
                var changes = JsonDrift.Diff(l!.NormalizedBody, r!.NormalizedBody);
                if (changes.Count == 0) noop++;
                else { change++; rows.Add(Row(anchor, "Modified", changes)); }
            }
        }

        return (rows, new GitOpsPlanSummary(add, change, destroy, noop), liveHashes);
    }

    // Merkle-ish roll-up: SHA-256 over each file's path=bodyHash line, path-sorted.
    public static string RollupHash(IReadOnlyList<GitOpsFile> files)
    {
        var sb = new StringBuilder();
        foreach (var f in files.OrderBy(f => $"{f.Surface.Folder}/{f.FileName}", StringComparer.Ordinal))
            sb.Append(f.Surface.Folder).Append('/').Append(f.FileName)
              .Append('=').Append(Sha256(f.NormalizedBody)).Append('\n');
        return Sha256(sb.ToString());
    }

    // Build a MINIMAL patch body for apply-Modified: only the top-level properties
    // that differ from live, plus the @odata.type discriminator (remapped from the
    // export's `odataType`). This is "apply the diff" — it avoids re-sending the
    // whole object, which would carry null-on-read / non-nullable-on-write fields
    // (e.g. supportsScopeTags) that Graph rejects on PATCH. Returns null when nothing
    // beyond the type differs. NOTE: values are taken verbatim from the normalized
    // export, so a changed *enum/nested* field still inherits the System.Text.Json
    // shape — fine for scalar edits (description, names, booleans); broader fields
    // need the STJ↔Kiota reconciliation tracked separately.
    public static string? BuildMinimalPatch(string repoJson, string liveJson)
    {
        if (JsonNode.Parse(repoJson) is not JsonObject repo) return null;
        var live = JsonNode.Parse(liveJson) as JsonObject;

        var minimal = new JsonObject();
        var discriminator = repo["@odata.type"] ?? repo["odataType"];
        if (discriminator is not null) minimal["@odata.type"] = discriminator.DeepClone();

        var changed = 0;
        foreach (var kv in repo)
        {
            if (kv.Key is "id" or "odataType" or "@odata.type") continue;
            var liveVal = live?[kv.Key];
            if (liveVal?.ToJsonString() == kv.Value?.ToJsonString()) continue;
            minimal[kv.Key] = kv.Value?.DeepClone();
            changed++;
        }
        return changed == 0 ? null : minimal.ToJsonString();
    }

    private static GitOpsPlanRow Row(GitOpsFile f, string verdict, List<DriftChangeDto> changes) =>
        new(f.Surface, f.FileName, f.ObjectName, verdict, changes);

    private static string KeyOf(GitOpsFile f) => $"{f.Surface.Key}|{f.FileName}";

    // displayName for a body, handling export wrappers ({policy:{…}}, {intent:{…}}…).
    private static readonly string[] NameProbes =
    [
        "displayName", "name",
        "policy.displayName", "policy.name", "policy.name",
        "intent.displayName", "template.displayName",
        "script.displayName", "application.displayName",
    ];

    public static string ExtractName(string json, string fallback)
    {
        try
        {
            var root = JsonNode.Parse(json);
            foreach (var probe in NameProbes)
            {
                JsonNode? cur = root;
                foreach (var seg in probe.Split('.'))
                    cur = cur is JsonObject o ? o[seg] : null;
                if (cur is JsonValue v && v.TryGetValue<string>(out var str) && !string.IsNullOrWhiteSpace(str))
                    return str;
            }
        }
        catch (JsonException) { /* fall through to filename */ }
        return fallback;
    }
}

// A persisted plan — pins the live state a plan was computed against so apply can
// reject objects that drifted since (per-object hash mismatch → conflict). Stored
// server-side under %LocalAppData%\cmProjectX\gitops\plans\ (out of the repo, so
// nothing leaks into Git). Keyed by "<surfaceKey>|<fileName>".
internal sealed record GitOpsStoredPlan(
    string PlanId,
    string RepoPath,
    string? TenantId,
    IReadOnlyList<string> Surfaces,
    Dictionary<string, string> LiveHashes,
    Dictionary<string, GitOpsStoredObject> Objects);

internal sealed record GitOpsStoredObject(string Surface, string FileName, string ObjectName, string Verdict);

internal static class GitOpsPlanStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string Dir()
    {
        var d = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cmProjectX", "gitops", "plans");
        Directory.CreateDirectory(d);
        return d;
    }

    private static string Sanitize(string s) =>
        new(s.Where(c => char.IsLetterOrDigit(c) || c is '_' or '-').ToArray());

    private static string PathFor(string planId) => Path.Combine(Dir(), Sanitize(planId) + ".json");

    public static async Task SaveAsync(GitOpsStoredPlan plan, CancellationToken ct) =>
        await File.WriteAllTextAsync(PathFor(plan.PlanId), JsonSerializer.Serialize(plan, Json), ct);

    public static async Task<GitOpsStoredPlan?> LoadAsync(string planId, CancellationToken ct)
    {
        var p = PathFor(planId);
        if (!File.Exists(p)) return null;
        try { return JsonSerializer.Deserialize<GitOpsStoredPlan>(await File.ReadAllTextAsync(p, ct), Json); }
        catch { return null; }
    }

    public static void Delete(string planId)
    {
        try { var p = PathFor(planId); if (File.Exists(p)) File.Delete(p); }
        catch { /* best-effort */ }
    }
}
