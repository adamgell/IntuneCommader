using System.Diagnostics;
using System.Text.Json;
using Azure.Core;
using CmProjectX.Store;
using Intune.Commander.Core.Models;
using Microsoft.Extensions.Logging;
// Alias the host type — keep this file free of a bare Microsoft.Graph.Beta using so
// it doesn't shadow IResult in Minimal API overload resolution (see DevicesEndpoints).
using WebApplication = Microsoft.AspNetCore.Builder.WebApplication;

namespace CmProjectX.Api;

// Maester (maester365/maester) integration. Maester is a PowerShell/Pester
// security-test framework with 280+ checks aligned to CIS / CISA SCuBA / EIDSCA.
// Rather than re-implement those checks in .NET, the sidecar shells out to
// PowerShell 7 + the Maester module, hands it the SAME session token AuthSession
// already holds (no second sign-in), parses Invoke-Maester's JSON, categorizes each
// control by the service it covers (Entra / Intune / …), and snapshots the run into
// the append-only time-machine so posture itself becomes time-travelable (drift).
public static class MaesterEndpoints
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static void MapMaester(this WebApplication app)
    {
        // GET /maester/status — prerequisite probe (PowerShell 7 + Maester module). No auth.
        app.MapGet("/maester/status", async (CancellationToken ct) =>
            Results.Ok(await MaesterRunner.ProbeAsync(ct)));

        // POST /maester/run — START the suite against the signed-in tenant (fire-and-forget).
        // Probes prerequisites synchronously (501 if absent), then runs in the background and
        // returns 202; the client polls GET /maester/run/status, then GET /maester/results. A
        // full pass can take minutes, so it must not hold the request open. 409 signed out or a
        // run already in progress.
        app.MapPost("/maester/run", async (
            MaesterRunRequest? req, AuthSession auth, ISnapshotStore store,
            MaesterRunTracker tracker, ILoggerFactory lf, CancellationToken ct) =>
        {
            var cred = auth.Credential;
            var profile = auth.ActiveProfile;
            if (cred is null || profile is null) return Results.Conflict();

            var log = lf.CreateLogger("Maester");

            // Probe up front so a missing pwsh/module answers 501 before we ack the run.
            var status = await MaesterRunner.ProbeAsync(ct);
            if (!status.Available)
                return ApiResults.Error(501, status.Message ?? "Maester prerequisites are not available.");

            // Live-progress denominator = the prior run's check count; and a progress file the
            // driver appends phase markers + Pester's per-test output to (parsed by the tracker).
            var expectedTotal = 0;
            try
            {
                var prevSnaps = await store.GetSnapshotsForObjectAsync(MaesterRunner.RunObjectId(profile.TenantId));
                if (prevSnaps.Count > 0 &&
                    JsonSerializer.Deserialize<MaesterRunResultDto>(prevSnaps[0].BodyJson, Web) is { } prev)
                    expectedTotal = prev.Total;
            }
            catch { /* unknown total → indeterminate progress */ }
            var progressFile = Path.Combine(Path.GetTempPath(), $"maester-prog-{Guid.NewGuid():n}.txt");

            if (!tracker.TryBegin(progressFile, expectedTotal)) return Results.Conflict(); // a run is already in progress

            var scopes = auth.Scopes;
            var tags = req?.Tags;
            // Detach from the request lifetime (CancellationToken.None): the HTTP request returns
            // immediately, so its ct would otherwise be cancelled out from under the background run.
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await MaesterRunner.RunAsync(cred, scopes, profile, tags, progressFile, log, CancellationToken.None);
                    await PersistRunAsync(store, profile, result, log);
                    tracker.Complete();
                    log.LogInformation("Maester run complete: {Passed}/{Total} passed", result.Passed, result.Total);
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Maester run failed");
                    tracker.Fail(ex.Message);
                }
            });

            return Results.Accepted("/maester/run/status", tracker.Snapshot());
        });

        // GET /maester/run/status — cheap in-memory state of the current/last run (no pwsh
        // probe); the client polls this while status is Running, then re-fetches results.
        app.MapGet("/maester/run/status", (MaesterRunTracker tracker) => Results.Ok(tracker.Snapshot()));

        // GET /maester/results — the latest stored run for the active tenant. The body
        // is the stable (timestamp-free) snapshot; ExecutedAt is filled from the
        // snapshot's capture time so the UI can show "last run".
        app.MapGet("/maester/results", async (AuthSession auth, ISnapshotStore store) =>
        {
            var profile = auth.ActiveProfile;
            if (profile is null) return Results.Conflict();

            var snaps = await store.GetSnapshotsForObjectAsync(MaesterRunner.RunObjectId(profile.TenantId));
            if (snaps.Count == 0)
                return Results.Ok(new MaesterRunResultDto(
                    null, "NotRun", 0, 0, 0, 0, 0, Array.Empty<MaesterCategoryDto>(), Array.Empty<MaesterControlDto>()));

            var latest = snaps[0]; // newest first
            var stored = JsonSerializer.Deserialize<MaesterRunResultDto>(latest.BodyJson, Web);
            return stored is null
                ? Results.Ok(new MaesterRunResultDto(
                    null, "NotRun", 0, 0, 0, 0, 0, Array.Empty<MaesterCategoryDto>(), Array.Empty<MaesterControlDto>()))
                : Results.Ok(stored with { ExecutedAt = latest.CapturedUtc.ToString("o") });
        });
    }

    // Snapshot-on-run + audit. The stored body drops ExecutedAt so an unchanged
    // posture dedups by content hash (AppendSnapshotIfChangedAsync) — a new snapshot
    // appears only when a control's result actually changed, which is the drift signal.
    private static async Task PersistRunAsync(
        ISnapshotStore store, TenantProfile profile, MaesterRunResultDto result, ILogger log)
    {
        try
        {
            var stable = result with { ExecutedAt = null };
            var body = JsonSerializer.Serialize(stable, Web);
            var objectId = MaesterRunner.RunObjectId(profile.TenantId);

            var stored = await store.AppendSnapshotIfChangedAsync(new ConfigSnapshotRecord(
                Guid.NewGuid().ToString("n"), objectId, "maester", "Maester checks", DateTime.UtcNow, body));
            if (stored is not null) await store.CommitIndexAsync();

            await store.AppendAuditEventAsync(new AuditEventRecord(
                Guid.NewGuid().ToString("n"), DateTime.UtcNow, "Maester",
                $"Security test run ({result.Passed}/{result.Total} passed, {result.Failed} failed)",
                "maester", objectId, profile.Name));
            await store.CommitIndexAsync();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Maester run persisted poorly (continuing — results were still returned)");
        }
    }
}

// Tracks the current/last Maester run so /maester/run is fire-and-forget and the client
// polls /maester/run/status cheaply (no pwsh probe). One active run at a time. Registered
// as a DI singleton in Program.cs (in-memory, per sidecar process — a restart resets to Idle;
// the durable record is the time-machine snapshot via /maester/results).
public sealed class MaesterRunTracker
{
    private readonly object _gate = new();
    private string _status = "Idle";
    private string? _startedAt;
    private string? _finishedAt;
    private string? _error;
    private string? _progressFile;
    private int _total;

    // Transition Idle/Completed/Failed → Running. Returns false if a run is already active.
    // progressFile is the growing file the driver writes phase markers + Pester's per-test
    // output to; expectedTotal is the prior run's check count (0 = unknown) as the denominator.
    public bool TryBegin(string? progressFile, int expectedTotal)
    {
        lock (_gate)
        {
            if (_status == "Running") return false;
            _status = "Running";
            _startedAt = DateTime.UtcNow.ToString("o");
            _finishedAt = null;
            _error = null;
            _progressFile = progressFile;
            _total = expectedTotal;
            return true;
        }
    }

    public void Complete()
    {
        lock (_gate) { _status = "Completed"; _finishedAt = DateTime.UtcNow.ToString("o"); _error = null; }
    }

    public void Fail(string error)
    {
        lock (_gate) { _status = "Failed"; _finishedAt = DateTime.UtcNow.ToString("o"); _error = error; }
    }

    public MaesterRunStateDto Snapshot()
    {
        // Snapshot the fields under the lock, then read the growing progress file OUTSIDE it —
        // /maester/run/status is polled every 2s and that file I/O shouldn't contend with
        // TryBegin/Complete/Fail.
        string status;
        string? startedAt, finishedAt, error, progressFile;
        int total;
        lock (_gate)
        {
            status = _status;
            startedAt = _startedAt;
            finishedAt = _finishedAt;
            error = _error;
            progressFile = _progressFile;
            total = _total;
        }
        string? phase = null;
        var completed = 0;
        if (status == "Running" && progressFile is not null && ReadProgress(progressFile) is { } text)
        {
            phase = LastPhase(text);
            completed = CountCompleted(text);
        }
        return new MaesterRunStateDto(status, startedAt, finishedAt, error, phase, completed, total);
    }

    // Read the driver's progress file even while pwsh is still appending to it.
    private static string? ReadProgress(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            return sr.ReadToEnd();
        }
        catch { return null; }
    }

    // Pester -Verbosity Detailed writes one "  [+]/[-]/[!] <id> …" line per test as it finishes.
    private static int CountCompleted(string text)
    {
        var n = 0;
        foreach (var line in text.AsSpan().EnumerateLines())
        {
            var t = line.TrimStart();
            if (t.Length >= 3 && t[0] == '[' && (t[1] is '+' or '-' or '!') && t[2] == ']') n++;
        }
        return n;
    }

    private static string? LastPhase(string text)
    {
        string? phase = null;
        foreach (var line in text.AsSpan().EnumerateLines())
        {
            var t = line.TrimStart();
            if (t.StartsWith("PHASE:")) phase = t[6..].Trim().ToString();
        }
        return phase;
    }
}

// Thrown when PowerShell 7 or the Maester module is not installed; surfaced as 501.
public sealed class MaesterUnavailableException(string message) : Exception(message);

// The runner: process orchestration + pure parse/categorize helpers (the latter are
// unit-tested in Api.Tests.Unit/MaesterResultTests.cs without any PowerShell present).
public static class MaesterRunner
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // One time-machine object per tenant — every run appends to the same history so
    // /drift shows what changed between two runs.
    public static string RunObjectId(string tenantId) => $"maester-run|{tenantId}";

    // Probe whether `pwsh` resolves and the Maester module is installed.
    public static async Task<MaesterStatusDto> ProbeAsync(CancellationToken ct)
    {
        try
        {
            var (code, stdout, stderr) = await RunPwshAsync(
                "-NoProfile -NonInteractive -Command " +
                "\"$PSVersionTable.PSVersion.ToString(); " +
                "$m = Get-Module -ListAvailable Maester | Select-Object -First 1; " +
                "if ($m) { $m.Version.ToString() } else { 'none' }\"",
                env: null, timeout: TimeSpan.FromSeconds(30), ct);

            if (code != 0)
                return new MaesterStatusDto(false, null, null,
                    $"PowerShell probe failed: {Trim(stderr)}");

            var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var pwsh = lines.Length > 0 ? lines[0] : null;
            var maester = lines.Length > 1 ? lines[1] : null;
            var has = maester is not null && !maester.Equals("none", StringComparison.OrdinalIgnoreCase);
            return new MaesterStatusDto(
                has, pwsh, has ? maester : null,
                has ? null : "PowerShell 7 is present but the Maester module is not installed (Install-Module Maester).");
        }
        catch (MaesterUnavailableException ex)
        {
            return new MaesterStatusDto(false, null, null, ex.Message);
        }
        catch (Exception ex)
        {
            return new MaesterStatusDto(false, null, null, ex.Message);
        }
    }

    // Mint a token from the live session, hand it to Connect-MgGraph -AccessToken, run
    // Invoke-Maester to a temp JSON file, then parse + categorize it.
    public static async Task<MaesterRunResultDto> RunAsync(
        TokenCredential credential, string[]? scopes, TenantProfile profile,
        IReadOnlyList<string>? tags, string? progressFile, ILogger log, CancellationToken ct)
    {
        var status = await ProbeAsync(ct);
        if (!status.Available)
            throw new MaesterUnavailableException(status.Message ?? "Maester prerequisites are not available.");

        // Maester's Entra/Intune/Defender/Identity tests read far more of Graph than the
        // Intune-scoped session token (.default) covers. Mint the token with an explicit
        // broad READ scope set so an interactive/delegated sign-in incrementally consents
        // the extra read permissions and more controls actually run (instead of skipping
        // or erroring). If that acquisition fails — scopes not consentable, the interactive
        // prompt is unavailable, admin-consent required — fall back to the session's scopes
        // (cloud-correct .default) so the run never regresses below the prior behavior.
        var fallbackScopes = scopes is { Length: > 0 } ? scopes : CloudEndpoints.GetScopes(profile.Cloud);
        AccessToken token;
        try
        {
            // Bound the acquisition (like the per-service tokens below): a delegated cred that
            // needs incremental consent on the broad read scopes can otherwise block the
            // background run indefinitely — no interactive prompt is available here, and the
            // run's only other time bound (RunPwshAsync) doesn't cover pre-pwsh token work.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(120));
            token = await credential.GetTokenAsync(
                new TokenRequestContext(MaesterReadScopes(profile.Cloud)), cts.Token);
            log.LogInformation("Maester token acquired with broad read scopes.");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex,
                "Broad Maester read scopes unavailable; falling back to session scopes. " +
                "Some Entra/Defender controls may report Skipped until the extra read " +
                "permissions are consented on the app registration.");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(120));
            token = await credential.GetTokenAsync(new TokenRequestContext(fallbackScopes), cts.Token);
        }

        // Best-effort per-service tokens for Maester's non-Graph tests (Exchange Online,
        // Teams, SharePoint). Each targets a different resource audience than Graph; acquisition
        // is bounded (timeout) and non-fatal — a failure (audience not consented, interactive
        // prompt unavailable) just leaves that service's tests Skipped, never breaking the run.
        var exoToken = await TryServiceTokenAsync(credential, "https://outlook.office365.com/.default", "Exchange Online", log, ct);
        var teamsToken = await TryServiceTokenAsync(credential, "48ac35b8-9aa8-4d74-927d-1f4a14a0b239/.default", "Microsoft Teams", log, ct);
        var (spoToken, spoAdminUrl) = await TrySharePointTokenAsync(credential, token.Token, profile.Cloud, log, ct);
        // Security & Compliance (EOP) rides the SAME Office 365 Exchange Online resource
        // (ps.compliance.protection.outlook.com is one of its servicePrincipalNames), so a token
        // for that audience carries the already-consented Exchange.Manage — no extra app-reg grant.
        var ippsToken = await TryServiceTokenAsync(credential, "https://ps.compliance.protection.outlook.com/.default", "Security & Compliance", log, ct);

        var jsonOut = Path.Combine(Path.GetTempPath(), $"maester-{Guid.NewGuid():n}.json");
        var diagOut = Path.Combine(Path.GetTempPath(), $"maester-diag-{Guid.NewGuid():n}.txt");
        var scriptPath = Path.Combine(Path.GetTempPath(), $"maester-{Guid.NewGuid():n}.ps1");
        await File.WriteAllTextAsync(scriptPath, RunScript, ct);

        try
        {
            // The access token already encodes the tenant, so Connect-MgGraph -AccessToken
            // binds to the right tenant without a separate -TenantId (which isn't valid in
            // the AccessToken parameter set anyway).
            var env = new Dictionary<string, string?>
            {
                ["MT_GRAPH_TOKEN"] = token.Token,
                ["MT_JSON_OUT"] = jsonOut,
                ["MT_DIAG_OUT"] = diagOut,
                ["MT_PROGRESS_OUT"] = progressFile,
                ["MT_ENV"] = GraphEnvironment(profile.Cloud),
                ["MT_TAGS"] = tags is { Count: > 0 } ? string.Join(",", tags) : null,
                // Best-effort service tokens (null → that service's Maester tests Skip).
                ["MT_EXO_TOKEN"] = exoToken,
                ["MT_TEAMS_TOKEN"] = teamsToken,
                ["MT_SPO_TOKEN"] = spoToken,
                ["MT_SPO_ADMIN_URL"] = spoAdminUrl,
                ["MT_IPPS_TOKEN"] = ippsToken,
            };

            log.LogInformation("Running Maester ({Env}, tags={Tags})", env["MT_ENV"], env["MT_TAGS"] ?? "(all)");
            var (code, _, stderr) = await RunPwshAsync(
                $"-NoProfile -NonInteractive -File \"{scriptPath}\"",
                env, timeout: TimeSpan.FromMinutes(20), ct);

            // Surface the per-service connection diagnostics the driver recorded, so a
            // connect failure (and Maester's own view of each connection) is visible in logs.
            if (File.Exists(diagOut))
                log.LogInformation("Maester service connections —\n{Diag}", await File.ReadAllTextAsync(diagOut, ct));
            if (!string.IsNullOrWhiteSpace(stderr))
                log.LogInformation("Maester driver stderr: {Stderr}", Trim(stderr));

            if (!File.Exists(jsonOut))
                throw new InvalidOperationException(
                    $"Invoke-Maester produced no JSON (exit {code}). {Trim(stderr)}");

            var json = await File.ReadAllTextAsync(jsonOut, ct);
            return Parse(json, DateTime.UtcNow.ToString("o"));
        }
        finally
        {
            TryDelete(scriptPath);
            TryDelete(jsonOut);
            TryDelete(diagOut);
            if (progressFile is not null) TryDelete(progressFile);
        }
    }

    // ── Pure helpers (unit-tested without PowerShell) ───────────────────────────

    // Project Invoke-Maester's JSON into the normalized run DTO. Parses defensively:
    // Maester's schema varies across versions, so every field is optional. Counts are
    // always recomputed from the projected controls (not read from the top-level
    // PassedCount/FailedCount/…) so the summary stays consistent with the per-category
    // roll-ups, which are derived from the same controls.
    public static MaesterRunResultDto Parse(string json, string? executedAt)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var controls = new List<MaesterControlDto>();
        if (root.TryGetProperty("Tests", out var tests) && tests.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in tests.EnumerateArray())
            {
                var id = Str(t, "Id") ?? Str(t, "Name") ?? "(unknown)";
                var title = Str(t, "Title") ?? Str(t, "Name") ?? id;
                var result = NormalizeResult(Str(t, "Result"));
                var severity = Str(t, "Severity");
                var helpUrl = Str(t, "HelpUrl");
                var tagList = ExtractTags(t);
                var block = Str(t, "Block");
                controls.Add(new MaesterControlDto(
                    id, title, Categorize(tagList, block), severity, result, helpUrl));
            }
        }

        controls.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

        var passed = Count(controls, "Passed");
        var failed = Count(controls, "Failed");
        var skipped = Count(controls, "Skipped") + Count(controls, "NotRun");
        var error = Count(controls, "Error");
        var total = controls.Count;

        var categories = controls
            .GroupBy(c => c.Category)
            .Select(g =>
            {
                var p = g.Count(c => c.Result == "Passed");
                var f = g.Count(c => c.Result == "Failed");
                var s = g.Count(c => c.Result is "Skipped" or "NotRun");
                var denom = p + f;
                return new MaesterCategoryDto(g.Key, g.Count(), p, f, s, denom == 0 ? 100 : (int)Math.Round(100.0 * p / denom));
            })
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ToList();

        var overall = Str(root, "Result") ?? (failed > 0 || error > 0 ? "Failed" : "Passed");

        return new MaesterRunResultDto(executedAt, overall, total, passed, failed, skipped, error, categories, controls);
    }

    // Map a control to the service it covers, by Pester tags first (most reliable) then
    // the Describe block path. Order matters: more specific services win.
    public static string Categorize(IReadOnlyList<string> tags, string? block)
    {
        var hay = (string.Join(" ", tags) + " " + (block ?? "")).ToLowerInvariant();
        // (category, keyword fragments) in priority order — more specific services win.
        var table = new (string Cat, string[] Keys)[]
        {
            ("Intune",     new[] { "intune", "ms.intune", "endpoint manager", "device compliance", "mdm" }),
            ("Entra",      new[] { "entra", "eidsca", "ms.aad", "azuread", "azure ad", "conditional access" }),
            ("Exchange",   new[] { "exchange", "ms.exo", "exo", "orca" }),
            ("Defender",   new[] { "defender", "ms.defender", "mdo", "atp" }),
            ("Teams",      new[] { "teams", "ms.teams" }),
            ("SharePoint", new[] { "sharepoint", "ms.sharepoint", "onedrive" }),
        };
        foreach (var (cat, keys) in table)
            if (keys.Any(k => hay.Contains(k, StringComparison.Ordinal)))
                return cat;
        return "Other";
    }

    // ── internals ───────────────────────────────────────────────────────────────

    private static int Count(List<MaesterControlDto> controls, string result) =>
        controls.Count(c => c.Result == result);

    private static string NormalizeResult(string? raw) => (raw ?? "").Trim() switch
    {
        var s when s.Equals("Passed", StringComparison.OrdinalIgnoreCase) => "Passed",
        var s when s.Equals("Failed", StringComparison.OrdinalIgnoreCase) => "Failed",
        var s when s.Equals("Skipped", StringComparison.OrdinalIgnoreCase) => "Skipped",
        var s when s.Equals("NotRun", StringComparison.OrdinalIgnoreCase) => "NotRun",
        var s when s.Equals("Inconclusive", StringComparison.OrdinalIgnoreCase) => "NotRun",
        var s when s.Length == 0 => "NotRun",
        _ => "Error",
    };

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    // Tag may serialize as an array, a single string, or a hashtable (object).
    private static IReadOnlyList<string> ExtractTags(JsonElement test)
    {
        if (test.ValueKind != JsonValueKind.Object || !test.TryGetProperty("Tag", out var tag))
            return [];
        var list = new List<string>();
        switch (tag.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var el in tag.EnumerateArray())
                    if (el.ValueKind == JsonValueKind.String) list.Add(el.GetString()!);
                break;
            case JsonValueKind.String:
                list.Add(tag.GetString()!);
                break;
            case JsonValueKind.Object:
                foreach (var p in tag.EnumerateObject())
                {
                    list.Add(p.Name);
                    if (p.Value.ValueKind == JsonValueKind.String) list.Add(p.Value.GetString()!);
                }
                break;
        }
        return list;
    }

    // Connect-MgGraph -Environment value for the tenant's national cloud. GCC
    // (moderate) rides the commercial Graph endpoint, so it maps to Global.
    private static string GraphEnvironment(CloudEnvironment cloud) => cloud switch
    {
        CloudEnvironment.GCCHigh => "USGov",
        CloudEnvironment.DoD => "USGovDoD",
        _ => "Global",
    };

    // Delegated READ permissions Maester's Graph-based tests exercise (Entra config,
    // policies, roles/PIM, auth methods, apps/domains, Intune, Defender/security,
    // identity risk, audit logs), resource-qualified for the tenant's cloud. Requested
    // explicitly (not .default) so an interactive sign-in incrementally consents them.
    // These are the Graph scopes only — Maester's Exchange/Teams/SharePoint tests need
    // their own service connections (Connect-ExchangeOnline/-MicrosoftTeams/-PnPOnline),
    // which the driver does not open, so those stay Skipped regardless of Graph scope.
    private static readonly string[] MaesterReadPermissions =
    [
        "Directory.Read.All",
        "Policy.Read.All",
        "Reports.Read.All",
        "RoleManagement.Read.Directory",
        "PrivilegedAccess.Read.AzureAD",
        "UserAuthenticationMethod.Read.All",
        "Application.Read.All",
        "Domain.Read.All",
        "DeviceManagementConfiguration.Read.All",
        "DeviceManagementManagedDevices.Read.All",
        "DeviceManagementServiceConfig.Read.All",
        "SecurityEvents.Read.All",
        "IdentityRiskyUser.Read.All",
        "AuditLog.Read.All",
    ];

    private static string[] MaesterReadScopes(CloudEnvironment cloud)
    {
        var root = CloudEndpoints.GetGraphRootUrl(cloud);
        return Array.ConvertAll(MaesterReadPermissions, p => $"{root}/{p}");
    }

    // Acquire a token for a non-Graph service audience (Exchange/Teams/SharePoint), bounded by
    // a short timeout so an interactive consent prompt (a hidden WAM window) can't hang the run.
    // Null on any failure — the caller passes null through and that service's tests just Skip.
    private static async Task<string?> TryServiceTokenAsync(
        TokenCredential cred, string scope, string service, ILogger log, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(90));
            var t = await cred.GetTokenAsync(new TokenRequestContext(new[] { scope }), cts.Token);
            log.LogInformation("Acquired {Service} token for Maester.", service);
            return t.Token;
        }
        catch (Exception ex)
        {
            log.LogWarning("No {Service} token — its Maester tests will Skip ({Reason}).", service, ex.Message);
            return null;
        }
    }

    // SharePoint's token audience is the tenant SPO host, so resolve the root site URL via Graph
    // first, then acquire a token for {https://<tenant>.sharepoint.com}/.default and return the
    // admin URL PnP connects to. Best-effort; (null, null) on any failure.
    private static async Task<(string? Token, string? AdminUrl)> TrySharePointTokenAsync(
        TokenCredential cred, string graphToken, CloudEnvironment cloud, ILogger log, CancellationToken ct)
    {
        try
        {
            var root = CloudEndpoints.GetGraphRootUrl(cloud);
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("Authorization", "Bearer " + graphToken);
            using var resp = await http.GetAsync($"{root}/v1.0/sites/root?$select=webUrl", ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var webUrl = doc.RootElement.GetProperty("webUrl").GetString(); // https://<tenant>.sharepoint.<tld>
            var host = new Uri(webUrl!).Host;                               // <tenant>.sharepoint[-mil].{com,us}
            // Admin host = insert "-admin" after the tenant label, preserving the cloud's TLD
            // so GCCHigh (.sharepoint.us) / DoD (.sharepoint-mil.us) resolve correctly — not a
            // hard-coded ".sharepoint.com".
            var dot = host.IndexOf('.');
            var adminUrl = "https://" + (dot > 0 ? host[..dot] + "-admin" + host[dot..] : host);
            // PnP connects to the admin URL, so the token audience must match that host.
            var spoToken = await TryServiceTokenAsync(cred, $"{adminUrl}/.default", "SharePoint", log, ct);
            return (spoToken, spoToken is null ? null : adminUrl);
        }
        catch (Exception ex)
        {
            log.LogWarning("SharePoint token/URL resolution failed — SharePoint tests will Skip ({Reason}).", ex.Message);
            return (null, null);
        }
    }

    private static string Trim(string s) => s.Length > 600 ? s[..600] + "…" : s.Trim();

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    // Modules bundled alongside the sidecar (scripts/release.ps1 stages Maester +
    // Microsoft.Graph.Authentication + Pester into <sidecar>/psmodules). Null when
    // absent — dev runs and unpackaged installs fall back to system-installed modules.
    private static string? BundledModulePath()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "psmodules");
        return Directory.Exists(dir) ? dir : null;
    }

    // Run `pwsh` with the given args. Throws MaesterUnavailableException when the
    // executable can't be found, so the endpoint can answer 501 cleanly.
    private static async Task<(int Code, string Stdout, string Stderr)> RunPwshAsync(
        string args, IReadOnlyDictionary<string, string?>? env, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("pwsh", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (env is not null)
            foreach (var (k, v) in env)
                psi.Environment[k] = v;

        // Prefer Maester + its deps bundled next to the sidecar (psmodules/) so a
        // packaged install runs fully offline (no PSGallery). Prepend to PSModulePath;
        // any system-installed modules still resolve as a fallback (dev machines).
        var bundled = BundledModulePath();
        if (bundled is not null)
        {
            var existing = psi.Environment.TryGetValue("PSModulePath", out var p) && !string.IsNullOrEmpty(p)
                ? p
                : Environment.GetEnvironmentVariable("PSModulePath");
            psi.Environment["PSModulePath"] =
                string.IsNullOrEmpty(existing) ? bundled : $"{bundled}{Path.PathSeparator}{existing}";
        }

        using var proc = new Process { StartInfo = psi };
        try
        {
            if (!proc.Start())
                throw new MaesterUnavailableException("Could not start PowerShell (pwsh).");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new MaesterUnavailableException(
                "PowerShell 7 (pwsh) was not found on PATH. Install it to run Maester checks.");
        }

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException($"Maester run exceeded {timeout.TotalMinutes:0} minutes and was aborted.");
        }

        return (proc.ExitCode, await stdoutTask, await stderrTask);
    }

    // The PowerShell driver. Reads everything from env vars (the token never appears in
    // process arguments). Connects with the shared session token, then runs the suite.
    private const string RunScript = """
        $ErrorActionPreference = 'Stop'
        try {
            # Phase 1: connecting. The sidecar reads MT_PROGRESS_OUT for the live phase + a
            # per-test completed count (Pester's Detailed [+]/[-]/[!] lines teed in below).
            if ($env:MT_PROGRESS_OUT) { Set-Content -Path $env:MT_PROGRESS_OUT -Value 'PHASE:Connecting to services' -Encoding utf8 }
            $secure = ConvertTo-SecureString -String $env:MT_GRAPH_TOKEN -AsPlainText -Force
            $connect = @{ AccessToken = $secure; NoWelcome = $true }
            if ($env:MT_ENV) { $connect.Environment = $env:MT_ENV }
            Connect-MgGraph @connect | Out-Null
            Import-Module Maester -ErrorAction Stop

            # Best-effort connections to Maester's non-Graph services, each using a token the
            # sidecar minted for that service's audience. Each result (success detail or the full
            # error) plus Maester's own Test-MtConnection verdict is recorded to MT_DIAG_OUT so the
            # sidecar logs exactly why a service connected or its tests Skipped. Failures are caught
            # (below the outer Stop handler) so the run always proceeds.
            $diag = [System.Collections.Generic.List[string]]::new()
            if ($env:MT_EXO_TOKEN) {
                try {
                    Import-Module ExchangeOnlineManagement -ErrorAction Stop
                    $org = $null
                    try { $org = ((Invoke-MgGraphRequest -Method GET -Uri 'v1.0/organization').value[0].verifiedDomains | Where-Object { $_.isInitial }).name } catch {}
                    $exo = @{ AccessToken = $env:MT_EXO_TOKEN; ShowBanner = $false; ErrorAction = 'Stop' }
                    if ($org) { $exo.Organization = $org }
                    Connect-ExchangeOnline @exo | Out-Null
                    $ci = Get-ConnectionInformation -ErrorAction SilentlyContinue | Select-Object -First 1
                    $diag.Add("Exchange: connect OK  org=$org  state=$($ci.State)  tokenStatus=$($ci.TokenStatus)")
                } catch { $diag.Add("Exchange: connect FAILED  $($_.Exception.GetType().Name): $($_.Exception.Message)") }
            } else { $diag.Add('Exchange: no token minted') }
            if ($env:MT_TEAMS_TOKEN) {
                try {
                    Import-Module MicrosoftTeams -ErrorAction Stop
                    Connect-MicrosoftTeams -AccessTokens @($env:MT_GRAPH_TOKEN, $env:MT_TEAMS_TOKEN) -ErrorAction Stop | Out-Null
                    $diag.Add("Teams: connect OK  tenant=$((Get-CsTenant -ErrorAction SilentlyContinue).DisplayName)")
                } catch { $diag.Add("Teams: connect FAILED  $($_.Exception.GetType().Name): $($_.Exception.Message)") }
            } else { $diag.Add('Teams: no token minted') }
            if ($env:MT_SPO_TOKEN -and $env:MT_SPO_ADMIN_URL) {
                try {
                    Import-Module PnP.PowerShell -ErrorAction Stop
                    Connect-PnPOnline -Url $env:MT_SPO_ADMIN_URL -AccessToken $env:MT_SPO_TOKEN -ErrorAction Stop
                    $diag.Add("SharePoint: connect OK  url=$env:MT_SPO_ADMIN_URL")
                } catch { $diag.Add("SharePoint: connect FAILED  $($_.Exception.GetType().Name): $($_.Exception.Message)") }
            } else { $diag.Add('SharePoint: no token/url') }
            if ($env:MT_IPPS_TOKEN) {
                try {
                    # Security & Compliance (EOP) via the same ExchangeOnlineManagement module. Its
                    # EOP session shows up in Get-ConnectionInformation with IsEopSession=true, which
                    # is exactly what Maester's Test-MtConnection SecurityCompliance checks.
                    Import-Module ExchangeOnlineManagement -ErrorAction Stop
                    if (-not $org) { try { $org = ((Invoke-MgGraphRequest -Method GET -Uri 'v1.0/organization').value[0].verifiedDomains | Where-Object { $_.isInitial }).name } catch {} }
                    $ipps = @{ AccessToken = $env:MT_IPPS_TOKEN; ShowBanner = $false; BypassMailboxAnchoring = $true; ErrorAction = 'Stop' }
                    if ($org) { $ipps.Organization = $org }
                    Connect-IPPSSession @ipps | Out-Null
                    $eop = Get-ConnectionInformation -ErrorAction SilentlyContinue | Where-Object { $_.IsEopSession } | Select-Object -First 1
                    $diag.Add("SecurityCompliance: connect OK  state=$($eop.State)  isEop=$($eop.IsEopSession)")
                } catch { $diag.Add("SecurityCompliance: connect FAILED  $($_.Exception.GetType().Name): $($_.Exception.Message)") }
            } else { $diag.Add('SecurityCompliance: no token minted') }
            # What Maester itself sees — the exact gate its tests use to decide Skip vs run.
            foreach ($svc in 'Graph', 'ExchangeOnline', 'SecurityCompliance', 'Teams') {
                try { $diag.Add("Maester Test-MtConnection ${svc} = $([bool](Test-MtConnection -Service $svc))") }
                catch { $diag.Add("Maester Test-MtConnection ${svc} threw: $($_.Exception.Message)") }
            }
            if ($env:MT_DIAG_OUT) { $diag -join [Environment]::NewLine | Set-Content -Path $env:MT_DIAG_OUT -Encoding utf8 }

            # Maester ships its Pester tests bundled inside the module (<ModuleBase>/maester-tests).
            # Invoke-Maester's -Path defaults to the current directory, so without pointing it at
            # that folder it fails with "No test files found in the path '.'".
            $testPath = Join-Path (Get-Module Maester).ModuleBase 'maester-tests'
            if (-not (Test-Path $testPath)) { throw "Maester tests folder not found at $testPath" }
            $params = @{
                Path             = $testPath
                OutputJsonFile   = $env:MT_JSON_OUT
                NonInteractive   = $true
                SkipVersionCheck = $true      # no PSGallery version probe (locked-down tenants)
                DisableTelemetry = $true      # don't phone home during an embedded run
                Verbosity        = 'Detailed' # per-test [+]/[-]/[!] lines → live progress
            }
            if ($env:MT_TAGS) { $params.Tag = ($env:MT_TAGS -split ',') }
            # Phase 2: running. Tee the per-test output to MT_PROGRESS_OUT so the sidecar can
            # count completed checks live; JSON results still come from OutputJsonFile.
            if ($env:MT_PROGRESS_OUT) {
                Add-Content -Path $env:MT_PROGRESS_OUT -Value 'PHASE:Running checks' -Encoding utf8
                Invoke-Maester @params *>&1 | Tee-Object -FilePath $env:MT_PROGRESS_OUT -Append | Out-Null
            } else {
                Invoke-Maester @params | Out-Null
            }
        }
        catch {
            Write-Error $_.Exception.Message
            exit 1
        }
        """;
}
