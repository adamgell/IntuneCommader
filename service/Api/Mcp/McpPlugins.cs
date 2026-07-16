using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;

namespace CmProjectX.Api;

// M13.3 — runtime MCP plugins. cmProjectX aggregates DOWNSTREAM MCP servers declared
// in %LocalAppData%\cmProjectX\plugins.json and re-exposes their tools (namespaced
// `plugin_<name>_<tool>`) through this one server, so an operator adds a capability
// (a PowerShell-remediation server, ServiceNow, a CMDB…) without rebuilding, and every
// tool — built-in or plugin — flows through one governed endpoint + audit.
//
// Trust model: the operator curates plugins.json; plugin tools are pass-through (we
// can't generically tell a downstream read from a write, so we don't gate them — the
// trust boundary is the curated allowlist). Best-effort: a plugin that fails to
// connect is logged and skipped; it never blocks the rest of the server.
internal static class McpPlugins
{
    // Downstream clients are kept alive for the process lifetime — the re-exposed
    // tools call THROUGH them, so they must not be disposed.
    private static readonly List<IAsyncDisposable> Live = new();

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // Test seam: overrides the plugins.json path so hermetic tests (marketplace install
    // → row write, EcosystemCatalog's installed-flag read) can point at a temp file
    // instead of the real %LocalAppData%\cmProjectX\plugins.json. Null in prod.
    internal static string? ConfigPathOverride;

    public static string ConfigPath => ConfigPathOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "cmProjectX", "plugins.json");

    public static async Task<IReadOnlyList<McpServerTool>> LoadAsync(ILogger log)
    {
        var path = ConfigPath;
        if (!File.Exists(path))
        {
            log.LogInformation("No MCP plugins.json at {Path} — plugin aggregation off.", path);
            return Array.Empty<McpServerTool>();
        }

        PluginsFile? cfg;
        try { cfg = JsonSerializer.Deserialize<PluginsFile>(await File.ReadAllTextAsync(path), JsonOpts); }
        catch (Exception ex) { log.LogWarning(ex, "plugins.json parse failed — skipping all plugins."); return Array.Empty<McpServerTool>(); }

        if (cfg?.Plugins is not { Count: > 0 } plugins) return Array.Empty<McpServerTool>();

        var tools = new List<McpServerTool>();
        foreach (var p in plugins)
        {
            if (string.IsNullOrWhiteSpace(p.Name)) { log.LogWarning("Plugin with no name — skipped."); continue; }
            var transport = BuildTransport(p);
            if (transport is null)
            {
                log.LogWarning("Plugin '{Name}': invalid transport '{T}' (or missing url/command) — skipped.", p.Name, p.Transport);
                continue;
            }
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var client = await McpClient.CreateAsync(transport, cancellationToken: cts.Token);
                Live.Add(client);

                var ns = Sanitize(p.Name);
                var clientTools = await client.ListToolsAsync(cancellationToken: cts.Token);
                foreach (var ct in clientTools)
                {
                    tools.Add(McpServerTool.Create(ct, new McpServerToolCreateOptions
                    {
                        Name = $"plugin_{ns}_{Sanitize(ct.Name)}",
                        Description = $"[plugin:{p.Name}] {ct.Description}",
                    }));
                }
                log.LogInformation("Plugin '{Name}': aggregated {Count} tool(s) over {Transport}.", p.Name, clientTools.Count, p.Transport);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Plugin '{Name}' failed to connect/list tools — skipped.", p.Name);
            }
        }
        return tools;
    }

    private static IClientTransport? BuildTransport(PluginCfg p)
    {
        var t = p.Transport?.Trim().ToLowerInvariant();
        switch (t)
        {
            case "http" or "streamablehttp" or "sse" when !string.IsNullOrWhiteSpace(p.Url):
                return new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = new Uri(p.Url!),
                    Name = p.Name!,
                    TransportMode = t == "sse" ? HttpTransportMode.Sse : HttpTransportMode.StreamableHttp,
                    AdditionalHeaders = p.Headers,
                });
            case "stdio" when !string.IsNullOrWhiteSpace(p.Command):
                return new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = p.Name!,
                    Command = p.Command!,
                    Arguments = p.Args,
                });
            default:
                return null;
        }
    }

    // MCP tool names must be [A-Za-z0-9_]; map anything else to '_'.
    private static string Sanitize(string s) =>
        new(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    private sealed class PluginsFile { public List<PluginCfg>? Plugins { get; set; } }

    private sealed class PluginCfg
    {
        public string? Name { get; set; }
        public string? Transport { get; set; }      // http | streamablehttp | sse | stdio
        public string? Url { get; set; }            // http transports
        public string? Command { get; set; }        // stdio transport
        public List<string>? Args { get; set; }     // stdio transport
        public Dictionary<string, string>? Headers { get; set; } // http transports (auth, etc.)
    }
}
