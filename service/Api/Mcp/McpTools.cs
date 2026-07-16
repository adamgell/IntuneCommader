using System.ComponentModel;
using System.Net;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace CmProjectX.Api;

// Builds the cmProjectX MCP tool catalog. Tools are thin proxies over the sidecar's
// OWN HTTP contract via a loopback HttpClient, so every per-surface Graph quirk the
// endpoint modules already handle (Settings Catalog split update, etc.) is reused
// verbatim — the MCP layer never touches a Graph type.
//
// Rather than ~120 per-surface tools (which bloat a client's tool list), the surface
// is a small GENERIC set: a `surface` argument selects the management surface, and
// `list_surfaces` is the discovery tool that enumerates valid keys. Adding a surface
// row to `Surfaces` grows what these tools can reach with zero new tool code.
internal static partial class McpTools
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    // Built once at startup; populated into each MCP session's ToolCollection.
    // `includeWrites` is false in read-only mode (M13.4 kill switch).
    public static IList<McpServerTool> BuildAll(HttpClient http, bool includeWrites = true)
    {
        var tools = new List<McpServerTool>();
        BuildReadTools(http, tools);
        if (includeWrites) BuildWriteTools(http, tools); // M13.2 (defined in McpWriteTools.cs)
        return tools;
    }

    private static void BuildReadTools(HttpClient http, List<McpServerTool> tools)
    {
        tools.Add(McpServerTool.Create(
            () => SurfaceCatalogJson(),
            new McpServerToolCreateOptions
            {
                Name = "list_surfaces",
                Description =
                    "List every cmProjectX (Intune/Entra) management surface, with its `surface` " +
                    "key, display name, and whether it is writable and/or assignable. Call this " +
                    "FIRST to discover valid `surface` values for the other tools.",
            }));

        tools.Add(McpServerTool.Create(
            async ([Description("Surface key, e.g. 'device-configs'. See list_surfaces.")] string surface) =>
                await ListObjects(http, surface),
            new McpServerToolCreateOptions
            {
                Name = "list_objects",
                Description = "List all objects on a management surface as normalized id/title/subtitle rows.",
            }));

        tools.Add(McpServerTool.Create(
            async (
                [Description("Surface key, e.g. 'compliance-policies'. See list_surfaces.")] string surface,
                [Description("The object id.")] string id) =>
                await GetObject(http, surface, id),
            new McpServerToolCreateOptions
            {
                Name = "get_object",
                Description = "Get the full JSON of one object on a surface (the view/edit source).",
            }));

        tools.Add(McpServerTool.Create(
            async (
                [Description("Assignable surface key, e.g. 'apps'. See list_surfaces (assignable=true).")] string surface,
                [Description("The object id.")] string id) =>
                await GetAssignments(http, surface, id),
            new McpServerToolCreateOptions
            {
                Name = "get_assignments",
                Description = "Get the assignment rows (group include/exclude + filters + intent) for an object.",
            }));

        tools.Add(McpServerTool.Create(
            async ([Description("Full-text query across audit events and config snapshots.")] string query) =>
                await GetText(http, $"/search?q={Uri.EscapeDataString(query)}"),
            new McpServerToolCreateOptions
            {
                Name = "search",
                Description = "Full-text search across the audit timeline and config-snapshot history.",
            }));

        tools.Add(McpServerTool.Create(
            async ([Description("Optional substring filter (actor, action, object). Omit for the latest events.")] string? query) =>
                await GetText(http, string.IsNullOrWhiteSpace(query)
                    ? "/audit"
                    : $"/audit?q={Uri.EscapeDataString(query)}"),
            new McpServerToolCreateOptions
            {
                Name = "audit",
                Description = "Read the audit-event timeline (the append-only time-machine).",
            }));

        tools.Add(McpServerTool.Create(
            async ([Description("The object id to diff (latest snapshot vs the one before it).")] string objectId) =>
                await GetText(http, $"/drift?objectId={Uri.EscapeDataString(objectId)}"),
            new McpServerToolCreateOptions
            {
                Name = "get_drift",
                Description = "Get field-level drift for an object between its two most recent config snapshots.",
            }));

        tools.Add(McpServerTool.Create(
            async ([Description("The object id whose snapshot history to list.")] string objectId) =>
                await GetText(http, $"/snapshots?objectId={Uri.EscapeDataString(objectId)}"),
            new McpServerToolCreateOptions
            {
                Name = "get_snapshots",
                Description = "List an object's config-snapshot history (newest first), for context or restore.",
            }));
    }

    // ── proxy helpers ─────────────────────────────────────────────────────────

    private static string SurfaceCatalogJson() =>
        JsonSerializer.Serialize(
            Surfaces.All.Select(s => new
            {
                surface = s.Key,
                displayName = s.DisplayName,
                section = s.Section,
                writable = s.Writable,
                assignable = s.Assignable,
            }),
            Json);

    private static async Task<string> ListObjects(HttpClient http, string surface)
        => Surfaces.Find(surface) is { } s ? await GetText(http, s.Path) : UnknownSurface(surface);

    private static async Task<string> GetObject(HttpClient http, string surface, string id)
        => Surfaces.Find(surface) is { } s
            ? await GetText(http, $"{s.Path}/{Uri.EscapeDataString(id)}")
            : UnknownSurface(surface);

    private static async Task<string> GetAssignments(HttpClient http, string surface, string id)
    {
        if (Surfaces.Find(surface) is not { } s) return UnknownSurface(surface);
        if (!s.Assignable) return $"Surface '{s.Key}' is not assignable. See list_surfaces (assignable=true).";
        return await GetText(http, $"{s.Path}/{Uri.EscapeDataString(id)}/assignments");
    }

    // Loopback request → (ok, body). On failure `body` is a short message the model can
    // act on (sign-in / transport / status) rather than an opaque code. Shared by read
    // tools (relay body) and write tools (branch on ok).
    internal static async Task<(bool ok, string body)> Send(
        HttpClient http, HttpMethod method, string url, string? json = null)
    {
        using var req = new HttpRequestMessage(method, url);
        if (json is not null)
            req.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage resp;
        try { resp = await http.SendAsync(req); }
        catch (Exception ex) { return (false, $"Could not reach the cmProjectX service: {ex.Message}"); }

        var body = await resp.Content.ReadAsStringAsync();
        if (resp.StatusCode == HttpStatusCode.Conflict)
            return (false, "Not signed in to a tenant. Ask the operator to sign in to cmProjectX, then retry.");
        if (!resp.IsSuccessStatusCode)
            return (false, $"Request failed ({(int)resp.StatusCode}): {Truncate(body, 800)}");
        return (true, body);
    }

    // Loopback GET → response body (read tools relay this verbatim).
    internal static async Task<string> GetText(HttpClient http, string url)
    {
        var (ok, body) = await Send(http, HttpMethod.Get, url);
        return ok ? (body.Length == 0 ? "(empty)" : body) : body;
    }

    internal static string UnknownSurface(string surface) =>
        $"Unknown surface '{surface}'. Call list_surfaces for the valid keys.";

    internal static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
