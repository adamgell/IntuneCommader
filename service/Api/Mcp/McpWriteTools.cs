using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace CmProjectX.Api;

// M13.2 — human-in-the-loop write tools. A `propose_*` tool NEVER applies a change:
// it computes the diff (the sidecar's /preview-diff engine) and parks a PendingChange
// for the operator to approve in the app's "Pending AI changes" inbox. On approve the
// sidecar REPLAYS the same (kind, path, body) through the normal write pipeline, so
// every per-surface Graph quirk + snapshot-on-write + audit applies unchanged.
internal static partial class McpTools
{
    private static void BuildWriteTools(HttpClient http, List<McpServerTool> tools)
    {
        tools.Add(McpServerTool.Create(
            async (
                [Description("Writable surface key, e.g. 'scope-tags'. See list_surfaces (writable=true).")] string surface,
                [Description("The full object JSON to create (must include the surface's @odata.type).")] string bodyJson) =>
                await ProposeCreate(http, surface, bodyJson),
            new McpServerToolCreateOptions
            {
                Name = "propose_create",
                Description = "Propose creating a new object on a surface. Queues it for operator approval; does NOT create it.",
            }));

        tools.Add(McpServerTool.Create(
            async (
                [Description("Writable surface key, e.g. 'compliance-policies'.")] string surface,
                [Description("The object id to update.")] string id,
                [Description("The full edited object JSON (the desired end state).")] string bodyJson) =>
                await ProposeUpdate(http, surface, id, bodyJson),
            new McpServerToolCreateOptions
            {
                Name = "propose_update",
                Description = "Propose updating an object. Returns the field-level diff and queues it for operator approval; does NOT write.",
            }));

        tools.Add(McpServerTool.Create(
            async (
                [Description("Writable surface key.")] string surface,
                [Description("The object id to delete.")] string id) =>
                await ProposeDelete(http, surface, id),
            new McpServerToolCreateOptions
            {
                Name = "propose_delete",
                Description = "Propose deleting an object. Queues it for operator approval; does NOT delete.",
            }));

        tools.Add(McpServerTool.Create(
            async (
                [Description("Assignable surface key, e.g. 'apps'. See list_surfaces (assignable=true).")] string surface,
                [Description("The object id whose assignments to replace.")] string id,
                [Description("The full assignment array JSON (kind/groupId/filterId/filterMode/intent rows).")] string assignmentsJson) =>
                await ProposeAssignments(http, surface, id, assignmentsJson),
            new McpServerToolCreateOptions
            {
                Name = "propose_assignments",
                Description = "Propose replacing an object's assignments. Queues it for operator approval; does NOT write.",
            }));

        tools.Add(McpServerTool.Create(
            async ([Description("The change id returned by a propose_* tool.")] string changeId) =>
                await GetText(http, $"/pending-changes/{Uri.EscapeDataString(changeId)}"),
            new McpServerToolCreateOptions
            {
                Name = "get_change_status",
                Description = "Check whether a proposed change is still pending, or was applied / rejected / failed (with any error).",
            }));

        tools.Add(McpServerTool.Create(
            async () => await GetText(http, "/pending-changes"),
            new McpServerToolCreateOptions
            {
                Name = "list_pending_changes",
                Description = "List the changes currently awaiting operator approval in cmProjectX.",
            }));
    }

    // ── propose_* implementations ─────────────────────────────────────────────

    private static async Task<string> ProposeCreate(HttpClient http, string surface, string bodyJson)
    {
        if (Surfaces.Find(surface) is not { } s) return UnknownSurface(surface);
        if (!s.Writable) return ReadOnly(s);
        if (!IsJsonObject(bodyJson)) return "bodyJson must be a JSON object.";
        var diff = await Diff(http, "{}", bodyJson);
        var sim = await Simulate(http, "create", s.Path, null, bodyJson);
        return await Enqueue(http, "create", s, objectId: null, NameFrom(bodyJson), bodyJson, diff, sim);
    }

    private static async Task<string> ProposeUpdate(HttpClient http, string surface, string id, string bodyJson)
    {
        if (Surfaces.Find(surface) is not { } s) return UnknownSurface(surface);
        if (!s.Writable) return ReadOnly(s);
        if (!IsJsonObject(bodyJson)) return "bodyJson must be a JSON object.";
        var (okBefore, before) = await Send(http, HttpMethod.Get, $"{s.Path}/{Uri.EscapeDataString(id)}");
        if (!okBefore) return before;
        var diff = await Diff(http, before, bodyJson);
        var sim = await Simulate(http, "update", s.Path, id, bodyJson);
        return await Enqueue(http, "update", s, id, NameFrom(bodyJson) ?? NameFrom(before), bodyJson, diff, sim);
    }

    private static async Task<string> ProposeDelete(HttpClient http, string surface, string id)
    {
        if (Surfaces.Find(surface) is not { } s) return UnknownSurface(surface);
        if (!s.Writable) return ReadOnly(s);
        var (okBefore, before) = await Send(http, HttpMethod.Get, $"{s.Path}/{Uri.EscapeDataString(id)}");
        var diff = okBefore ? await Diff(http, before, "{}") : "[]";
        var sim = await Simulate(http, "delete", s.Path, id, null);
        return await Enqueue(http, "delete", s, id, okBefore ? NameFrom(before) : null, body: null, diff, sim);
    }

    private static async Task<string> ProposeAssignments(HttpClient http, string surface, string id, string assignmentsJson)
    {
        if (Surfaces.Find(surface) is not { } s) return UnknownSurface(surface);
        if (!s.Assignable) return $"Surface '{s.Key}' is not assignable. See list_surfaces (assignable=true).";
        var sim = await Simulate(http, "assign", s.Path, id, assignmentsJson);
        return await Enqueue(http, "assign", s, id, $"assignments for {id}", assignmentsJson, diffJson: "[]", sim);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static async Task<string> Enqueue(
        HttpClient http, string kind, Surface s, string? objectId, string? name, string? body, string diffJson,
        string? simulationJson = null)
    {
        var payload = JsonSerializer.Serialize(new
        {
            proposer = "MCP client",
            kind,
            path = s.Path,
            objectId,
            objectName = name,
            bodyJson = body,
            diffJson,
            simulationJson, // M16 blast-radius report (null if /simulate was unavailable)
        });
        var (ok, resp) = await Send(http, HttpMethod.Post, "/pending-changes", payload);
        if (!ok) return resp;

        var changeId = ChangeIdFrom(resp);
        var verb = kind switch
        {
            "create" => "create",
            "update" => "update",
            "delete" => "delete",
            _ => "set assignments on",
        };
        var target = objectId is null ? s.Key : $"{s.Key}/{objectId}";
        var diffNote = diffJson is "[]" or "" ? "(no field-level diff to show)" : diffJson;
        return $"Proposed: {verb} {target}. Queued for operator approval in cmProjectX as change {changeId}. " +
               "It will NOT be applied until the operator approves it in the app." +
               BlastNote(simulationJson) + "\n\nProposed change set:\n" + diffNote;
    }

    private static async Task<string> Diff(HttpClient http, string before, string after)
    {
        var (ok, d) = await Send(http, HttpMethod.Post, "/preview-diff",
            JsonSerializer.Serialize(new { before, after }));
        return ok ? d : "[]";
    }

    // M16 — pre-flight the proposed write's blast radius and attach it to the pending
    // change (so the AI never proposes blind and the inbox shows impact + diff). Returns
    // null when /simulate is unavailable (e.g. signed out); the propose still queues.
    private static async Task<string?> Simulate(HttpClient http, string verb, string path, string? objectId, string? body)
    {
        var (ok, report) = await Send(http, HttpMethod.Post, "/simulate",
            JsonSerializer.Serialize(new { verb, path, objectId, bodyJson = body }));
        return ok ? report : null;
    }

    // One-line blast-radius note for the propose tool's reply (best-effort parse).
    private static string BlastNote(string? simulationJson)
    {
        if (string.IsNullOrWhiteSpace(simulationJson)) return "";
        try
        {
            using var d = JsonDocument.Parse(simulationJson);
            var sev = d.RootElement.TryGetProperty("severity", out var sv) ? sv.GetString() : null;
            var sum = d.RootElement.TryGetProperty("summary", out var sm) ? sm.GetString() : null;
            if (sev is null && sum is null) return "";
            return $"\n\nBlast radius [{sev}]: {sum}";
        }
        catch { return ""; }
    }

    private static string ReadOnly(Surface s) => $"Surface '{s.Key}' is read-only — it exposes no write tools.";

    private static bool IsJsonObject(string s)
    {
        try { using var d = JsonDocument.Parse(s); return d.RootElement.ValueKind == JsonValueKind.Object; }
        catch { return false; }
    }

    private static string? NameFrom(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            if (d.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var key in new[] { "displayName", "name" })
                if (d.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                    return v.GetString();
        }
        catch { /* not an object / unparseable → no name */ }
        return null;
    }

    private static string ChangeIdFrom(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            if (d.RootElement.TryGetProperty("id", out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? "(unknown)";
        }
        catch { /* fall through */ }
        return "(unknown)";
    }
}
