using System.Text;
using System.Text.Json;
using CmProjectX.Store;

namespace CmProjectX.Api.Cli;

// Headless CI drift gate: `Api.exe drift-gate [flags]`. Evaluates the local snapshot store's
// per-object drift (reusing DriftEvaluator — the same engine GET /objects uses), prints a
// report, optionally alerts Teams/Slack/GitHub, and returns a CI exit code. No auth, no Graph
// call, and it doesn't take the single-instance mutex or start Kestrel. It DOES open the local
// store (SQLite + the Lucene index, which is single-writer), so a live UI sidecar must be
// stopped first — a held index lock surfaces as a clean exit 2, not a crash.
//
// Flags:
//   --fail-on-drift          exit 1 when drift is detected (else always 0 on a clean run)
//   --min-changes <n>        an object counts as drifted at >= n field changes (default 1)
//   --format text|json|markdown   stdout format (default text)
//   --output <path>          also write the JSON report to a file
//   --teams  <url>           POST a MessageCard to a Teams incoming webhook   (or CMPX_TEAMS_WEBHOOK)
//   --slack  <url>           POST {text} to a Slack incoming webhook          (or CMPX_SLACK_WEBHOOK)
//   --github <owner/repo>    open a GitHub issue (needs GITHUB_TOKEN)
// Alerts fire only when drift is detected and are best-effort (a webhook failure logs to
// stderr and never changes the drift verdict / exit code).
//
// Exit codes: 0 = ran OK (no drift, or drift without --fail-on-drift); 1 = drift + --fail-on-drift;
// 2 = operational error (bad flags, unreadable store, or an unimplemented mode).
public static class DriftGate
{
    private static readonly HttpClient Http = new();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            // --live (app-only sign-in + delta sync) and --baseline <zip> (export-vs-baseline)
            // are deferred: both need a live tenant secret to exercise, so they're not shipped
            // unverified. The store-only gate below is the buildable, offline-verifiable core.
            if (Has(args, "--live") || Flag(args, "--baseline") is not null)
            {
                Console.Error.WriteLine(
                    "drift-gate: --live and --baseline are not implemented yet. Use the default store-only " +
                    "mode, which gates on the local snapshot store's per-object drift. See docs/CI-DRIFT-GATE.md.");
                return 2;
            }

            var minChanges = int.TryParse(Flag(args, "--min-changes"), out var mc) && mc > 0 ? mc : 1;
            var format = (Flag(args, "--format") ?? "text").ToLowerInvariant();
            var failOnDrift = Has(args, "--fail-on-drift");

            using var store = new SnapshotStore();
            await store.InitializeAsync();
            var all = await DriftEvaluator.EvaluateAsync(store);
            var drifted = all.Where(o => o.ChangeCount >= minChanges).ToList();
            var report = new DriftReportDto(
                DateTime.UtcNow.ToString("o"), "store-only", null,
                all.Count, drifted.Count, minChanges, drifted.Count > 0, drifted);

            var rendered = format switch
            {
                "json" => JsonSerializer.Serialize(report, JsonOpts),
                "markdown" => RenderMarkdown(report),
                _ => RenderText(report),
            };
            Console.WriteLine(rendered);

            var outPath = Flag(args, "--output");
            if (!string.IsNullOrWhiteSpace(outPath))
                await File.WriteAllTextAsync(outPath, JsonSerializer.Serialize(report, JsonOpts));

            if (report.DriftDetected)
            {
                var teams = Flag(args, "--teams") ?? Environment.GetEnvironmentVariable("CMPX_TEAMS_WEBHOOK");
                var slack = Flag(args, "--slack") ?? Environment.GetEnvironmentVariable("CMPX_SLACK_WEBHOOK");
                var github = Flag(args, "--github");
                if (!string.IsNullOrWhiteSpace(teams)) await SafePost("Teams", PostTeams(teams!, report));
                if (!string.IsNullOrWhiteSpace(slack)) await SafePost("Slack", PostSlack(slack!, report));
                if (!string.IsNullOrWhiteSpace(github)) await SafePost("GitHub", PostGithub(github!, report));
            }

            return failOnDrift && report.DriftDetected ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("drift-gate error: " + ex.Message);
            return 2;
        }
    }

    // ── flag parsing ─────────────────────────────────────────────────────────
    private static string? Flag(string[] a, string name)
    {
        var i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }
    private static bool Has(string[] a, string name) => Array.IndexOf(a, name) >= 0;

    // ── renderers ────────────────────────────────────────────────────────────
    private static string RenderText(DriftReportDto r)
    {
        if (!r.DriftDetected)
            return $"No drift: 0 of {r.TrackedObjectCount} tracked object(s) changed (min-changes {r.MinChanges}).";
        var sb = new StringBuilder();
        sb.AppendLine($"Drift detected: {r.DriftedObjectCount} of {r.TrackedObjectCount} tracked object(s) changed (min-changes {r.MinChanges}).");
        foreach (var o in r.Objects)
            sb.AppendLine($"  - {o.ObjectName ?? o.ObjectId} ({o.ObjectType}) — {o.ChangeCount} change(s)");
        return sb.ToString().TrimEnd();
    }

    private static string RenderMarkdown(DriftReportDto r)
    {
        if (!r.DriftDetected)
            return $"**No drift** — 0 of {r.TrackedObjectCount} tracked object(s) changed (min-changes {r.MinChanges}).";
        var sb = new StringBuilder();
        sb.AppendLine($"**{r.DriftedObjectCount} object(s) drifted** across {r.TrackedObjectCount} tracked (min-changes {r.MinChanges}).");
        sb.AppendLine();
        foreach (var o in r.Objects)
            sb.AppendLine($"- `{o.ObjectName ?? o.ObjectId}` ({o.ObjectType}) — {o.ChangeCount} change(s)");
        return sb.ToString().TrimEnd();
    }

    private static string RenderSlackText(DriftReportDto r)
    {
        var sb = new StringBuilder();
        sb.Append($"IntuneCommander drift: *{r.DriftedObjectCount} object(s) drifted* across {r.TrackedObjectCount} tracked.");
        foreach (var o in r.Objects)
            sb.Append($"\n• {o.ObjectName ?? o.ObjectId} ({o.ObjectType}) — {o.ChangeCount} change(s)");
        return sb.ToString();
    }

    // ── alert posts (best-effort) ────────────────────────────────────────────
    private static async Task SafePost(string name, Task post)
    {
        try { await post; }
        catch (Exception ex) { Console.Error.WriteLine($"drift-gate: {name} alert failed (ignored): {ex.Message}"); }
    }

    private static StringContent Json(object o) =>
        new(JsonSerializer.Serialize(o, JsonOpts), Encoding.UTF8, "application/json");

    private static async Task PostTeams(string url, DriftReportDto r)
    {
        // Legacy Office 365 connector MessageCard — @context needs a Dictionary (not an
        // anonymous type, which can't produce the '@context' key).
        var card = new Dictionary<string, object?>
        {
            ["type"] = "MessageCard",
            ["@context"] = "https://schema.org/extensions",
            ["summary"] = "IntuneCommander drift detected",
            ["themeColor"] = "E81123",
            ["title"] = "IntuneCommander Drift Report",
            ["text"] = RenderMarkdown(r),
        };
        (await Http.PostAsync(url, Json(card))).EnsureSuccessStatusCode();
    }

    private static async Task PostSlack(string url, DriftReportDto r) =>
        (await Http.PostAsync(url, Json(new { text = RenderSlackText(r) }))).EnsureSuccessStatusCode();

    private static async Task PostGithub(string ownerRepo, DriftReportDto r)
    {
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            Console.Error.WriteLine("drift-gate: --github set but GITHUB_TOKEN is empty; skipping the GitHub issue.");
            return;
        }
        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://api.github.com/repos/{ownerRepo}/issues")
        {
            Content = Json(new { title = $"IntuneCommander drift detected ({DateTime.UtcNow:yyyy-MM-dd})", body = RenderMarkdown(r) }),
        };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        req.Headers.UserAgent.ParseAdd("intunecommander-drift-gate");
        (await Http.SendAsync(req)).EnsureSuccessStatusCode();
    }
}
