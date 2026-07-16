using System.Text.Json;
using System.Text.Json.Nodes;
// Microsoft.Graph.Beta.Models also defines a `WebApplication` type; alias the host
// type so the extension method resolves (see DevicesEndpoints for the rationale).
using WebApplication = Microsoft.AspNetCore.Builder.WebApplication;

namespace CmProjectX.Api;

// M21 Ecosystem — plugin marketplace. A discovery layer over the existing M13.3
// plugins.json aggregation (McpPlugins.cs). It does NOT change how a plugin runs —
// installing just appends a plugins.json row (the docs/plugins.example.json shape);
// the aggregation picks it up on the next sidecar start and re-exposes the tools
// namespaced plugin_<name>_<tool>.
//
// Trust model = curated allowlist. The registry ships curated/verified entries; the
// install flow writes the plugins.json trust decision explicitly. Plugin reads pass
// through; side-effecting plugin tools are surfaced as trust:"writes-side-effecting".
// Native writes a plugin triggers (via a playbook propose_*) still gate through the
// M13 inbox — only the plugin's own side-effects are the operator's curation decision.
public static class MarketplaceEndpoints
{
    public static void MapMarketplace(this WebApplication app)
    {
        // GET /marketplace — the curated registry (Installed flag set per plugins.json).
        app.MapGet("/marketplace", () => Results.Ok(EcosystemCatalog.ListMarketplace()));

        // POST /marketplace/{id}/install — bind configFields → append a plugins.json row.
        app.MapPost("/marketplace/{id}/install", (string id, MarketplaceInstallRequest? req) =>
        {
            var entry = EcosystemCatalog.FindMarketplace(id);
            if (entry is null) return Results.NotFound();

            var config = req?.Config ?? new Dictionary<string, string>();

            // Every required configField must be supplied.
            foreach (var f in entry.ConfigFields)
                if (f.Required && (!config.TryGetValue(f.Name, out var v) || string.IsNullOrEmpty(v)))
                    return ApiResults.BadRequest($"missing required configField '{f.Name}'");

            var row = BuildPluginRow(entry, config, out var error);
            if (row is null)
                return ApiResults.BadRequest(error);

            try { AppendPluginRow(row); }
            catch (Exception ex)
            {
                return Results.Ok(new MarketplaceInstallResult(
                    entry.Id, EcosystemCatalog.PluginNameFor(entry), entry.Transport, Installed: false,
                    Message: $"failed to write plugins.json: {ex.Message}"));
            }

            // Surface the trust decision: secret config fields land in plugins.json in
            // cleartext (the existing M13.3 plugin model; OS-keychain storage is M21 open
            // question #4). The operator should see that before they walk away.
            var hasSecret = entry.ConfigFields.Any(f => string.Equals(f.Type, "secret", StringComparison.OrdinalIgnoreCase));
            var secretNote = hasSecret
                ? " NOTE: secret values are stored in cleartext in plugins.json under %LocalAppData%\\cmProjectX (see M21 open question #4)."
                : "";
            return Results.Ok(new MarketplaceInstallResult(
                entry.Id, EcosystemCatalog.PluginNameFor(entry), entry.Transport, Installed: true,
                Message: "plugins.json row written — restart the sidecar to aggregate its tools (plugin_" +
                         EcosystemCatalog.PluginNameFor(entry) + "_*)." + secretNote));
        });
    }

    // Build the plugins.json row from the entry + bound config. Resolves {{token}}s in
    // the url template and folds header-bearing fields into the headers map.
    internal static JsonObject? BuildPluginRow(MarketplaceEntry entry, IReadOnlyDictionary<string, string> config, out string error)
    {
        error = "";
        var name = EcosystemCatalog.PluginNameFor(entry);
        var transport = entry.Transport;
        var row = new JsonObject
        {
            ["name"] = name,
            ["transport"] = transport,
        };

        var t = transport.Trim().ToLowerInvariant();
        if (t is "http" or "streamablehttp" or "sse")
        {
            if (string.IsNullOrWhiteSpace(entry.UrlTemplate)) { error = "http entry has no urlTemplate"; return null; }
            var url = EcosystemCatalog.Substitute(entry.UrlTemplate!, config);
            // The resolved url must be a well-formed ABSOLUTE http/https endpoint. This
            // rejects file://, ftp://, and other schemes (so a crafted entry can't point
            // the MCP transport at file:///… for exfiltration), and unbound {{tokens}}
            // (a misconfigured template would otherwise write a broken row).
            if (url.Contains("{{") ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                error = $"resolved url must be an absolute http/https URL (got '{url}')";
                return null;
            }
            row["url"] = url;

            // Header-bearing configFields → the headers map (applying the field format,
            // e.g. "Bearer {{token}}"). The token's own value is substituted first.
            var headers = new JsonObject();
            foreach (var f in entry.ConfigFields)
            {
                if (string.IsNullOrWhiteSpace(f.Header)) continue;
                config.TryGetValue(f.Name, out var val);
                val ??= "";
                var headerVal = string.IsNullOrWhiteSpace(f.Format)
                    ? val
                    : EcosystemCatalog.Substitute(f.Format!, config);
                headers[f.Header!] = headerVal;
            }
            if (headers.Count > 0) row["headers"] = headers;
        }
        else if (t is "stdio")
        {
            if (string.IsNullOrWhiteSpace(entry.Command)) { error = "stdio entry has no command"; return null; }
            row["command"] = EcosystemCatalog.Substitute(entry.Command!, config);
            if (entry.Args is { Count: > 0 })
            {
                var args = new JsonArray();
                foreach (var a in entry.Args) args.Add(EcosystemCatalog.Substitute(a, config));
                row["args"] = args;
            }
        }
        else
        {
            error = $"unsupported transport '{transport}'";
            return null;
        }

        return row;
    }

    // Append (or replace by name) a row in %LocalAppData%\cmProjectX\plugins.json,
    // creating the file with the expected { "plugins": [...] } shape if absent. We
    // preserve any existing rows; a re-install of the same entry replaces its row.
    // Internal so the hermetic install test can drive the real write path (pointed at a
    // temp plugins.json via McpPlugins.ConfigPathOverride).
    internal static void AppendPluginRow(JsonObject row)
    {
        var path = McpPlugins.ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        JsonObject root;
        JsonArray plugins;
        if (File.Exists(path))
        {
            // Tolerate comments / trailing commas (the example file uses them).
            var parsed = JsonNode.Parse(File.ReadAllText(path), null, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) as JsonObject;
            root = parsed ?? new JsonObject();
            plugins = root["plugins"] as JsonArray ?? new JsonArray();
        }
        else
        {
            root = new JsonObject();
            plugins = new JsonArray();
        }

        // Replace an existing row with the same name (idempotent re-install).
        var name = row["name"]!.GetValue<string>();
        for (var i = plugins.Count - 1; i >= 0; i--)
            if (plugins[i] is JsonObject o && o["name"]?.GetValue<string>() == name)
                plugins.RemoveAt(i);
        plugins.Add(row);
        root["plugins"] = plugins;

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
