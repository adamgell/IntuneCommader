using System.Text;
using System.Text.Json;
// Microsoft.Graph.Beta.Models also defines a `WebApplication` type; alias the host
// type so the extension method resolves (see DevicesEndpoints for the rationale).
using WebApplication = Microsoft.AspNetCore.Builder.WebApplication;

namespace CmProjectX.Api;

// M21 Ecosystem — remediation playbooks. A playbook is a parameterized, ordered set
// of proposed writes. Running one binds parameters, substitutes {{token}}s into each
// step, then expands every NATIVE step (propose_create/update/delete/assignments) into
// a PendingChange in the M13 inbox via the loopback /pending-changes endpoint — the
// SAME path the MCP propose_* tools use, so each step renders in the operator's inbox
// with the same diff panel and approves as an exact diff. proposer carries
// playbook:{id} for a clean audit trail. Plugin steps (plugin_<name>_<tool>) are
// pass-through per the M13.3 trust boundary — the playbook records them, but they run
// through the aggregated MCP server, not the inbox.
//
// Nothing here applies a write. Native steps gate; the operator approves each in the
// app. This module never touches Graph directly.
public static class PlaybooksEndpoints
{
    public static void MapPlaybooks(this WebApplication app)
    {
        // GET /playbooks — list playbooks (params + step summary).
        app.MapGet("/playbooks", () => Results.Ok(EcosystemCatalog.ListPlaybooks()));

        // GET /playbooks/{id} — full playbook definition.
        app.MapGet("/playbooks/{id}", (string id) =>
        {
            var pb = EcosystemCatalog.FindPlaybook(id);
            return pb is null ? Results.NotFound() : Results.Ok(pb);
        });

        // POST /playbooks/{id}/run — bind params → expand the ordered steps.
        app.MapPost("/playbooks/{id}/run", async (string id, PlaybookRunRequest? req) =>
        {
            var pb = EcosystemCatalog.FindPlaybook(id);
            if (pb is null) return Results.NotFound();

            var (values, missing) = EcosystemCatalog.ResolveParameters(pb.Parameters, req?.Parameters);
            if (missing is not null)
                return ApiResults.BadRequest($"missing required parameter '{missing}'");

            var proposer = $"playbook:{pb.Id}";
            var stepResults = new List<PlaybookStepResult>();
            var createdIds = new List<string>();

            // dependsOn orders ENQUEUE (approval order stays the operator's). Steps are
            // already authored in order; we honor that and skip a step only if it can't
            // be expanded — the rest still enqueue so a single bad step isn't fatal.
            foreach (var step in pb.Steps)
            {
                var result = await ExpandStepAsync(step, values, proposer);
                stepResults.Add(result);
                if (result.PendingChangeId is { } cid) createdIds.Add(cid);
            }

            return Results.Ok(new PlaybookRunResult(pb.Id, proposer, stepResults, createdIds));
        });
    }

    // Expand one step: plugin tools pass through (recorded, not enqueued); native
    // propose_* steps enqueue a PendingChange via loopback /pending-changes.
    private static async Task<PlaybookStepResult> ExpandStepAsync(
        PlaybookStep step, IReadOnlyDictionary<string, string> values, string proposer)
    {
        // Plugin step — pass-through (M13.3 trust boundary). We don't invoke it from
        // here (the aggregated MCP server runs it); the playbook just records it.
        if (step.Tool.StartsWith("plugin_", StringComparison.Ordinal))
            return new PlaybookStepResult(step.Id, step.Tool, "passthrough",
                Message: "plugin tool — runs through the aggregated MCP server, not inbox-gated");

        var kind = step.Tool switch
        {
            "propose_create" => "create",
            "propose_update" => "update",
            "propose_delete" => "delete",
            "propose_assignments" => "assign",
            _ => null,
        };
        if (kind is null)
            return new PlaybookStepResult(step.Id, step.Tool, "skipped",
                Message: $"unknown tool '{step.Tool}' (expected propose_* or plugin_*)");

        if (string.IsNullOrWhiteSpace(step.Path))
            return new PlaybookStepResult(step.Id, step.Tool, "skipped", Message: "native step missing 'path'");

        // {{token}} substitution against the bound parameter values. Reject a
        // substituted path that carries a traversal sequence so a {{param}} value can't
        // walk the loopback surface hierarchy (defense-in-depth — routing normalizes too).
        var substitutedPath = EcosystemCatalog.Substitute(step.Path!, values);
        if (EcosystemCatalog.ContainsTraversal(substitutedPath))
            return new PlaybookStepResult(step.Id, step.Tool, "skipped",
                Message: "step 'path' contains an invalid traversal sequence after substitution");
        var surfacePath = "/" + substitutedPath.TrimStart('/');
        var objectId = step.ObjectId is null ? null : EcosystemCatalog.Substitute(step.ObjectId, values);
        var bodyJson = SubstituteBody(step.Body, values);

        if (kind is "create" or "update" or "assign" && bodyJson is null)
            return new PlaybookStepResult(step.Id, step.Tool, "skipped", Message: $"'{kind}' step missing 'body'");

        // Compute a best-effort diff for the inbox panel. create/assign diff {} → body;
        // update GETs the live object first (the approval replay recomputes the real
        // write regardless, so a GET miss is non-fatal — empty diff).
        string diffJson = "[]";
        if (kind is "create")
            diffJson = await PreviewDiff("{}", bodyJson!);
        else if (kind is "update")
        {
            var before = await GetObject(surfacePath, objectId);
            diffJson = before is null ? "[]" : await PreviewDiff(before, bodyJson!);
        }

        var name = NameFrom(bodyJson) ?? (objectId is null ? null : $"{step.Tool} {objectId}");
        var payload = JsonSerializer.Serialize(new
        {
            proposer,
            kind,
            path = surfacePath,
            objectId,
            objectName = name,
            bodyJson,
            diffJson,
        });

        var (status, resp) = await Loopback(HttpMethod.Post, "/pending-changes", payload);
        if (status != System.Net.HttpStatusCode.OK)
            return new PlaybookStepResult(step.Id, step.Tool, "error",
                Message: $"enqueue failed ({(int)status}): {Truncate(resp)}");

        var changeId = ChangeIdFrom(resp);
        return new PlaybookStepResult(step.Id, step.Tool, "enqueued", PendingChangeId: changeId);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    // Serialize the step body to JSON, substituting {{token}}s in every STRING value
    // (recursively). Returns null when the step carries no body.
    private static string? SubstituteBody(Dictionary<string, object>? body, IReadOnlyDictionary<string, string> values)
    {
        if (body is null) return null;
        var node = JsonSerializer.SerializeToElement(body, EcosystemCatalog.Json);
        var substituted = SubstituteElement(node, values);
        return substituted?.ToJsonString();
    }

    private static System.Text.Json.Nodes.JsonNode? SubstituteElement(
        JsonElement el, IReadOnlyDictionary<string, string> values)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new System.Text.Json.Nodes.JsonObject();
                foreach (var p in el.EnumerateObject())
                    obj[p.Name] = SubstituteElement(p.Value, values);
                return obj;
            case JsonValueKind.Array:
                var arr = new System.Text.Json.Nodes.JsonArray();
                foreach (var item in el.EnumerateArray())
                    arr.Add(SubstituteElement(item, values));
                return arr;
            case JsonValueKind.String:
                return EcosystemCatalog.Substitute(el.GetString() ?? "", values);
            default:
                return System.Text.Json.Nodes.JsonNode.Parse(el.GetRawText());
        }
    }

    private static async Task<string> PreviewDiff(string before, string after)
    {
        var (ok, body) = await Loopback(HttpMethod.Post, "/preview-diff",
            JsonSerializer.Serialize(new { before, after }));
        return ok == System.Net.HttpStatusCode.OK ? body : "[]";
    }

    private static async Task<string?> GetObject(string surfacePath, string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var (ok, body) = await Loopback(HttpMethod.Get, $"{surfacePath}/{Uri.EscapeDataString(id)}", null);
        return ok == System.Net.HttpStatusCode.OK ? body : null;
    }

    private static async Task<(System.Net.HttpStatusCode Status, string Body)> Loopback(
        HttpMethod method, string url, string? json)
    {
        try
        {
            using var msg = new HttpRequestMessage(method, url);
            if (json is not null)
                msg.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await CmProjectX.Api.Loopback.Http.SendAsync(msg);
            return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
        }
        catch
        {
            return ((System.Net.HttpStatusCode)0, "");
        }
    }

    private static string? NameFrom(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var d = JsonDocument.Parse(json);
            if (d.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var key in new[] { "displayName", "name" })
                if (d.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                    return v.GetString();
        }
        catch { /* not an object → no name */ }
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

    private static string Truncate(string s) => s.Length <= 400 ? s : s[..400] + "…";
}
