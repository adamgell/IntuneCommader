using ModelContextProtocol.Server;

namespace CmProjectX.Api;

// Wires the cmProjectX MCP server into the ASP.NET host. Kept in its own file (like
// the endpoint modules) so its usings never leak into Program.cs.
//
// Transport: Streamable HTTP at /mcp on the same loopback host the client already
// uses (127.0.0.1:5099). Built-in tools are built at startup; plugin tools (M13.3)
// are appended before the server starts listening. Each session's ToolCollection is
// populated from one shared list — the SDK's per-session hook is the supported path
// for a runtime-built (non-attribute) tool set, so it requires stateful mode.
public static class McpSetup
{
    private static readonly List<McpServerTool> Tools = new();

    // Read-only kill switch: set CMPROJECTX_MCP_READONLY=1 to expose only read tools
    // (no propose_* writes). The approval inbox is the primary write gate; this is a
    // belt-and-suspenders lockdown for restricted deployments (M13.4).
    private static bool ReadOnly =>
        (Environment.GetEnvironmentVariable("CMPROJECTX_MCP_READONLY") ?? "") is "1" or "true" or "TRUE";

    public static void AddCmProjectXMcp(this WebApplicationBuilder builder)
    {
        Tools.AddRange(McpTools.BuildAll(Loopback.Http, includeWrites: !ReadOnly));

        builder.Services.AddMcpServer()
            .WithHttpTransport(options =>
            {
                // Per-session ToolCollection population requires stateful mode.
                options.Stateless = false;
                options.ConfigureSessionOptions = (ctx, mcpOptions, ct) =>
                {
                    mcpOptions.Capabilities = new();
                    mcpOptions.Capabilities.Tools = new();
                    var collection = mcpOptions.ToolCollection = [];
                    foreach (var tool in Tools) collection.Add(tool);
                    return Task.CompletedTask;
                };
            });
    }

    public static void MapCmProjectXMcp(this WebApplication app) => app.MapMcp("/mcp");

    // M13.3 — connect downstream MCP servers from plugins.json and append their
    // (namespaced) tools. Called after the store is ready but BEFORE app.Run(), so
    // every session sees the full set. Best-effort; never throws.
    public static async Task LoadMcpPluginsAsync(this WebApplication app)
    {
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("McpPlugins");
        try { Tools.AddRange(await McpPlugins.LoadAsync(log)); }
        catch (Exception ex) { log.LogWarning(ex, "MCP plugin load failed — continuing with built-in tools only."); }
    }
}
