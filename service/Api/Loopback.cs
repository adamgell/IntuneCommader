namespace CmProjectX.Api;

// One long-lived loopback HttpClient the sidecar uses to call its OWN HTTP contract:
//  • the MCP tools proxy reads/writes through it (reusing every endpoint's per-surface
//    Graph handling), and
//  • the approval-apply path replays an approved write through it (the same request a
//    human edit would send).
// Base address is the constant the host binds to, so this needs nothing from the built app.
internal static class Loopback
{
    public const string BaseUrl = "http://127.0.0.1:5099";

    public static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri(BaseUrl),
        Timeout = TimeSpan.FromSeconds(60),
    };
}
