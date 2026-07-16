using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace CmProjectX.Api.Tests;

// One result row for the coverage report.
public sealed record EndpointResult(string Name, string Path, int Status, int? Count, string Note);

// Shared across the whole test collection: ensures a signed-in sidecar is reachable
// on 127.0.0.1:5099 (reusing one if already running, else starting the built
// Api.dll), and writes a coverage report when the run finishes.
public sealed class SidecarFixture : IAsyncLifetime
{
    private const string BaseUrl = "http://127.0.0.1:5099";

    public HttpClient Http { get; } = new()
    {
        BaseAddress = new Uri(BaseUrl),
        Timeout = TimeSpan.FromSeconds(90),
    };

    public bool SignedIn { get; private set; }
    public string? SignInError { get; private set; }

    // Every test appends here; the report is written in DisposeAsync.
    public static readonly ConcurrentBag<EndpointResult> Results = new();

    private Process? _started;

    public async Task InitializeAsync()
    {
        if (!await IsHealthyAsync())
        {
            _started = TryStartSidecar();
            for (var i = 0; i < 160 && !await IsHealthyAsync(); i++)
                await Task.Delay(500);
        }

        if (!await IsHealthyAsync())
        {
            SignInError = "sidecar is not reachable on 127.0.0.1:5099 (and could not be started). " +
                          "Build it (dotnet build service/Api/Api.csproj) or start it manually.";
            return;
        }

        var state = await AuthStateAsync();
        if (state != "SignedIn")
        {
            try { await Http.PostAsync("/auth/signin", null); } catch { /* surfaced via health below */ }
            for (var i = 0; i < 120; i++)
            {
                state = await AuthStateAsync();
                if (state is "SignedIn" or "Failed") break;
                await Task.Delay(500);
            }
        }

        SignedIn = state == "SignedIn";
        if (!SignedIn)
            SignInError = await SignInErrorAsync()
                ?? $"sign-in did not reach SignedIn (state={state}). The Entra client secret may be expired.";
    }

    public Task DisposeAsync()
    {
        TryWriteReport();
        if (_started is { HasExited: false })
        {
            try { _started.Kill(entireProcessTree: true); } catch { /* best-effort */ }
        }
        Http.Dispose();
        return Task.CompletedTask;
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private async Task<bool> IsHealthyAsync()
    {
        try
        {
            using var resp = await Http.GetAsync("/health");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private async Task<string?> AuthStateAsync()
    {
        try
        {
            using var resp = await Http.GetAsync("/health");
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("authState", out var s) ? s.GetString() : null;
        }
        catch { return null; }
    }

    private async Task<string?> SignInErrorAsync()
    {
        try
        {
            using var resp = await Http.GetAsync("/health");
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
        }
        catch { return null; }
    }

    private static Process? TryStartSidecar()
    {
        var dll = FindApiDll();
        if (dll is null) return null;

        var psi = new ProcessStartInfo("dotnet", $"\"{dll}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(dll)!,
        };
        try
        {
            var p = Process.Start(psi);
            if (p is not null)
            {
                // The sidecar logs every HTTP request to stdout. We MUST drain the
                // redirected pipes (discard) or their ~64KB buffer fills mid-run and
                // deadlocks the sidecar — hanging the whole test run.
                p.OutputDataReceived += static (_, _) => { };
                p.ErrorDataReceived += static (_, _) => { };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }
            return p;
        }
        catch { return null; }
    }

    // Walk up from the test binary to the repo, then locate the built sidecar.
    private static string? FindApiDll()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var apiDir = Path.Combine(dir.FullName, "service", "Api");
            if (Directory.Exists(apiDir))
            {
                foreach (var cfg in new[] { "Debug", "Release" })
                {
                    var dll = Path.Combine(apiDir, "bin", cfg, "net10.0", "Api.dll");
                    if (File.Exists(dll)) return dll;
                }
            }
            dir = dir.Parent;
        }
        return null;
    }

    private static string? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "service", "Api"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static void TryWriteReport()
    {
        var root = RepoRoot();
        if (root is null) return;
        var path = Path.Combine(root, "service", "Api.Tests", "endpoint-coverage.md");

        var rows = Results.OrderBy(r => r.Path, StringComparer.Ordinal).ToList();
        var skipped = rows.Count(r => r.Note.StartsWith("SKIPPED"));
        var empty = rows.Count(r => r.Note.StartsWith("EMPTY"));
        var failed = rows.Count(r => r.Status != 200 && !r.Note.StartsWith("SKIPPED"));
        var ok = rows.Count - skipped - empty - failed;

        var sb = new StringBuilder();
        sb.AppendLine("# cmProjectX — endpoint coverage report");
        sb.AppendLine();
        sb.AppendLine("Generated by `dotnet test service/Api.Tests` against the signed-in sidecar.");
        sb.AppendLine("Empty arrays are reported, not failed (a surface can be empty by design).");
        sb.AppendLine();
        sb.AppendLine($"- **With data:** {ok}");
        sb.AppendLine($"- **Empty (verify by-design):** {empty}");
        sb.AppendLine($"- **Skipped detail (list empty, nothing to fetch):** {skipped}");
        sb.AppendLine($"- **Failed (non-200 / non-JSON):** {failed}");
        sb.AppendLine();
        sb.AppendLine("| Endpoint | HTTP | Rows | Note |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var r in rows)
            sb.AppendLine($"| `{r.Path}` | {r.Status} | {(r.Count?.ToString() ?? "—")} | {r.Note} |");

        try { File.WriteAllText(path, sb.ToString()); } catch { /* best-effort */ }
    }
}

[CollectionDefinition("sidecar")]
public sealed class SidecarCollection : ICollectionFixture<SidecarFixture> { }
