// IntuneCommander service — thin ASP.NET host in front of the hard-forked IC core.
// Endpoints mirror contract/openapi.yaml. Runs as a LOCAL SIDECAR (127.0.0.1:5099)
// that the Rust/WinUI client launches.

using System.Diagnostics;
using System.Text.Json;
using CmProjectX.Api;
using CmProjectX.Api.Providers;
using CmProjectX.Store;
using CmProjectX.Sync;
using Intune.Commander.Core.Extensions;
using Intune.Commander.Core.Services;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// Headless CI drift gate — `Api.exe drift-gate ...` runs a one-shot drift check and exits
// with a CI-friendly code (0 clean / 1 drift+--fail-on-drift / 2 error), WITHOUT taking the
// single-instance mutex or starting Kestrel. It still opens the local store (single-writer
// Lucene index), so a running UI sidecar must be stopped first (a held lock → clean exit 2).
// Must come before the mutex below. See docs/CI-DRIFT-GATE.md.
if (args.Length > 0 && args[0] == "drift-gate")
    return await CmProjectX.Api.Cli.DriftGate.RunAsync(args);

// Single-instance guard. The sidecar owns exclusive state — the SQLite store,
// the Lucene index (one writer per directory), and port 5099 — so a second
// instance can only fail; without this it dies later with a cryptic Lucene
// LockObtainFailedException (or "address in use") mid-initialization. Held for
// the process lifetime; the OS destroys the mutex when the last handle closes,
// so a crashed instance never wedges the next one.
using var instanceMutex = new Mutex(initiallyOwned: true, @"Local\IntuneCommander.Service", out var isFirstInstance);
if (!isFirstInstance)
{
    Console.Error.WriteLine(
        "Another IntuneCommander service instance is already running — it owns the data store and port 5099. " +
        "Stop it first (close the other debug session, or: Stop-Process -Name Api).");
    return 1;
}

// Syncfusion license — registered once at startup for the Conditional Access → PowerPoint export
// (service/Core ConditionalAccessPptExportService) and the M19 evidence pack. The key is read from
// the SYNCFUSION_LICENSE_KEY environment variable so it stays OUT of source: it's injected at build
// time (CI from a repo variable) and set on the user's machine by the MSI/MSIX installer (and the
// portable launcher). Absent/blank key → the PPTX path runs unlicensed (trial watermark), so log it.
// (Syncfusion.Licensing is available transitively via Core's Syncfusion.Presentation reference.)
var syncfusionKey = Environment.GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY")?.Trim();
// Guard against an unsubstituted installer placeholder (e.g. a literal "%SYNCFUSION_LICENSE_KEY%").
if (!string.IsNullOrEmpty(syncfusionKey) && !syncfusionKey.StartsWith('%'))
    Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(syncfusionKey);
else
    Console.Error.WriteLine(
        "SYNCFUSION_LICENSE_KEY not set — PowerPoint (PPTX) export will run unlicensed (trial watermark).");

var builder = WebApplication.CreateBuilder(args);

// Hard-forked IC core: DataProtection (existing key ring), ProfileService
// (reads %LocalAppData%\Intune.Commander\profiles.json), Graph client factory,
// drift detection, export normalizer, LiteDB cache.
builder.Services.AddIntuneCommanderCore();

builder.Services.AddSingleton<AuthSession>();
builder.Services.AddSingleton<ISnapshotStore, SnapshotStore>();
builder.Services.AddSingleton<GraphDeltaSync>();
// Tracks the in-flight/last Maester run so POST /maester/run is fire-and-forget and the
// client polls GET /maester/run/status cheaply (see MaesterEndpoints).
builder.Services.AddSingleton<CmProjectX.Api.MaesterRunTracker>();
// M12.1 — the hoisted blob-cache warm routine, DI-resolvable so the cache-dev
// POST /cache/warm endpoint can invoke the SAME warm as sign-in (see CacheWarmer.cs).
builder.Services.AddSingleton<CacheWarmer>();

// M18 Autonomy — the scheduled background watcher (service/Sync). Registered as a
// singleton AND as the IHostedService so the host starts it and Program.cs can wire
// its Tick delegate to the Api-side AutonomyEngine after the app is built.
builder.Services.AddSingleton<CmProjectX.Sync.AutonomyScheduler>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CmProjectX.Sync.AutonomyScheduler>());
// M20 fleet — the multi-tenant fan-out plane: FleetSession holds N concurrent Graph
// clients (one per saved profile, built via the same factory AuthSession uses);
// FleetStore persists tenant groups + golden templates + campaign results.
builder.Services.AddSingleton<FleetSession>();
builder.Services.AddSingleton<FleetStore>();
// ─── M22 cross-MDM spike — the provider seam (docs/part-ii/M22-spike.md) ──────
// Today the Intune provider is implicit: endpoint modules new Core services inline.
// The full M22 makes it explicit and keyed — AddSingleton<IMdmProvider, IntuneProvider>()
// (a thin adapter over the same Core calls) alongside one per second backend, with
// IProviderRegistry resolving the active one from the tenant profile's providerId.
// For the SPIKE we register only the read-only stub second provider + the registry,
// so the dispatch endpoint can render a non-Intune surface through the uniform
// contract WITHOUT disturbing the live Intune path (which stays as-is for now).
builder.Services.AddSingleton<IMdmProvider, StubMdmProvider>();
builder.Services.AddSingleton<IProviderRegistry, ProviderRegistry>();

// M13 — expose the management surface to any MCP client (Claude Desktop, Cursor,
// Copilot…). Tools proxy this same sidecar's HTTP contract over loopback, so all
// per-surface Graph quirks the endpoint modules handle are reused. Served at /mcp.
builder.AddCmProjectXMcp();

// ─── Ship-debt: uniform error envelope + telemetry ────────────────────────────
// ProblemDetails backs the global UseExceptionHandler / UseStatusCodePages wired
// right after Build(); every non-2xx response then serializes as the shared ErrorDto
// envelope { error, detail?, status, traceId } (see ErrorEnvelope.cs).
builder.Services.AddProblemDetails();

// OpenTelemetry (tracing + metrics) for the load-bearing paths (see Telemetry.cs).
// GATED: the providers are registered ONLY when an OTLP endpoint (the standard
// OTEL_EXPORTER_OTLP_ENDPOINT) or CMPROJECTX_OTEL_CONSOLE=1 is configured — so with
// nothing set there is zero overhead, no collector is required, and boot never fails
// for want of one. The ActivitySource/Meter in Telemetry.cs stay a cheap no-op then.
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
var otelConsole = builder.Configuration["CMPROJECTX_OTEL_CONSOLE"] is "1" or "true" or "True";
if (!string.IsNullOrWhiteSpace(otlpEndpoint) || otelConsole)
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService(serviceName: "cmprojectx-sidecar", serviceVersion: Telemetry.Version))
        .WithTracing(t =>
        {
            t.AddSource(Telemetry.SourceName);
            t.AddAspNetCoreInstrumentation();
            if (!string.IsNullOrWhiteSpace(otlpEndpoint)) t.AddOtlpExporter();
            if (otelConsole) t.AddConsoleExporter();
        })
        .WithMetrics(m =>
        {
            m.AddMeter(Telemetry.MeterName);                                              // our custom Meter (cache/graph/write instruments)
            m.AddMeter("Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Server.Kestrel"); // built-in ASP.NET request metrics
            if (!string.IsNullOrWhiteSpace(otlpEndpoint)) m.AddOtlpExporter();
            if (otelConsole) m.AddConsoleExporter();
        });
}

var app = builder.Build();

// ─── Uniform error envelope (ship-debt) ───────────────────────────────────────
// Every unhandled exception and every bare error status code serializes as the
// shared ErrorDto { error, detail?, status, traceId } (see ErrorEnvelope.cs) — so the
// thin client sees ONE error shape. Endpoint handlers that already wrote a body
// (ApiResults.*, or a success payload) are left untouched by UseStatusCodePages.
app.UseExceptionHandler(errorApp => errorApp.Run(async ctx =>
{
    var ex = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var traceId = ErrorEnvelope.TraceId(ctx);
    if (ex is not null)
        app.Logger.LogError(ex, "Unhandled exception (traceId={TraceId})", traceId);
    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await ctx.Response.WriteAsJsonAsync(
        ErrorEnvelope.FromException(ex ?? new Exception("unknown error"), ctx.Response.StatusCode, traceId));
}));
app.UseStatusCodePages(async statusCtx =>
    await statusCtx.HttpContext.Response.WriteAsJsonAsync(
        ErrorEnvelope.FromStatus(statusCtx.HttpContext.Response.StatusCode, ErrorEnvelope.TraceId(statusCtx.HttpContext))));

// Load saved tenant profiles up front so the active client is known at /health time.
var authSession = app.Services.GetRequiredService<AuthSession>();
await authSession.InitializeAsync();

// M12.1 — blob read-through cache warming (docs/CACHE-M12.1.md). The warm routine is
// hoisted into the DI-resolvable CacheWarmer (so the cache-dev POST /cache/warm
// endpoint invokes the SAME warm), but sign-in's CacheWarm hook is still wired here:
// it calls warmer.WarmAsync(graph, tenantId, force:false). Reused by sign-in
// (force=false) and POST /sync (force=true); both fire-and-forget, stamping
// LastWarmedUtc only on full success. Reuses the existing 31-type PrefetchAllToCacheAsync.
var cacheWarmer = app.Services.GetRequiredService<CacheWarmer>();
authSession.CacheWarm = (graph, tenantId) => cacheWarmer.WarmAsync(graph, tenantId, force: false);

// Open the append-only store (SQLite + Lucene) before serving reads.
await app.Services.GetRequiredService<ISnapshotStore>().InitializeAsync();

// M18 Autonomy — wire the scheduler's tick to the Api-side engine. The engine runs
// the full watch→detect→plan→simulate→propose→verify loop for the active tenant and
// returns the policy cadence so the scheduler waits the right interval. LOCKED:
// nothing here applies a write — the loop terminates at the /pending-changes inbox.
// Errors are swallowed by the scheduler; the tick itself is best-effort.
var autonomyScheduler = app.Services.GetRequiredService<CmProjectX.Sync.AutonomyScheduler>();
var autonomyStore = app.Services.GetRequiredService<ISnapshotStore>();
var autonomySync = app.Services.GetRequiredService<GraphDeltaSync>();
var autonomyLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Autonomy");
autonomyScheduler.Tick = async ct =>
{
    var tenantId = authSession.ActiveProfile?.TenantId;
    if (authSession.Graph is null || tenantId is null) return null; // signed out → idle cadence
    var policy = await AutonomyPolicyStore.GetAsync(autonomyStore, tenantId, ct);
    var engine = new AutonomyEngine(autonomyStore, autonomySync, authSession, autonomyLog);
    await engine.RunTickAsync(ct);
    // Drive the scheduler's wait off the policy cadence (even when disabled, so a
    // re-enable is picked up within one cadence); fall back to default if unset.
    return policy.CadenceMinutes > 0 ? policy.CadenceMinutes : 15;
};

// M13.3 — aggregate downstream MCP servers declared in plugins.json (best-effort;
// no-op when absent). Runs before app.Run() so every MCP session sees the full set.
await app.LoadMcpPluginsAsync();

// GET /health — liveness + auth/sync status. The device-code prompt surfaces here
// while authState == AwaitingDeviceCode.
app.MapGet("/health", (AuthSession auth) =>
{
    var (state, deviceCode, error) = auth.Snapshot();
    var profile = auth.ActiveProfile;
    return Results.Ok(new SyncStatusDto(
        Healthy: true,
        AuthState: state.ToString(),
        TenantId: profile?.TenantId,
        ProfileName: profile?.Name,
        LastSyncUtc: auth.LastSyncUtc?.ToString("o"),
        Cloud: profile?.Cloud.ToString(),
        DeviceCode: deviceCode,
        Error: error,
        LastWarmedUtc: auth.LastWarmedUtc?.ToString("o")));
});

// GET /profiles — saved Entra app registrations.
app.MapGet("/profiles", (AuthSession auth) =>
{
    var activeId = auth.ActiveProfileId;
    var profiles = auth.Profiles.Select(p => new TenantProfileSummaryDto(
        p.Id, p.Name, p.TenantId, p.Cloud.ToString(), p.AuthMethod.ToString(), p.Id == activeId));
    return Results.Ok(profiles);
});

// POST /profiles/{id}/activate — choose the active tenant.
app.MapPost("/profiles/{id}/activate", async (string id, AuthSession auth, CancellationToken ct) =>
    await auth.ActivateAsync(id, ct) ? Results.NoContent() : Results.NotFound());

// POST /profiles — create a saved tenant profile (M11 profile lifecycle).
app.MapPost("/profiles", async (CreateProfileRequest r, AuthSession auth) =>
{
    if (string.IsNullOrWhiteSpace(r.Name) || string.IsNullOrWhiteSpace(r.TenantId) || string.IsNullOrWhiteSpace(r.ClientId))
        return ApiResults.BadRequest("name, tenantId and clientId are required");
    try
    {
        var p = await auth.AddProfileAsync(r.Name, r.TenantId, r.ClientId, r.Cloud, r.AuthMethod, r.ClientSecret);
        return Results.Ok(new { id = p.Id });
    }
    catch (ArgumentException ex) { return ApiResults.BadRequest(ex.Message); }
});

// PATCH /profiles/{id} — edit a saved profile (null fields keep their value).
app.MapPatch("/profiles/{id}", async (string id, CreateProfileRequest r, AuthSession auth) =>
    await auth.UpdateProfileAsync(id, r.Name, r.TenantId, r.ClientId, r.Cloud, r.AuthMethod, r.ClientSecret)
        ? Results.NoContent() : Results.NotFound());

// DELETE /profiles/{id} — remove a saved profile.
app.MapDelete("/profiles/{id}", async (string id, AuthSession auth) =>
    await auth.DeleteProfileAsync(id) ? Results.NoContent() : Results.NotFound());

// POST /auth/signin — begin sign-in for the active profile (background; poll /health).
app.MapPost("/auth/signin", (AuthSession auth) =>
    auth.BeginSignIn() ? Results.Accepted() : Results.Conflict());

// POST /auth/signout — clear the session.
app.MapPost("/auth/signout", (AuthSession auth) => { auth.SignOut(); return Results.NoContent(); });

// POST /sync — trigger an incremental Graph sync for the active tenant. Runs in the
// background (it can take a while); the client polls /health for LastSyncUtc to advance.
app.MapPost("/sync", (AuthSession auth, GraphDeltaSync sync, ILoggerFactory loggerFactory) =>
{
    var graph = auth.Graph;
    var tenantId = auth.ActiveProfile?.TenantId;
    if (graph is null || tenantId is null) return Results.Conflict(); // not signed in

    _ = Task.Run(async () =>
    {
        var log = loggerFactory.CreateLogger("Sync");
        try
        {
            var result = await sync.RunAsync(graph, tenantId);
            auth.LastSyncUtc = DateTime.UtcNow;
            log.LogInformation(
                "Sync complete: {AuditEvents} audit events, {Snapshots} new snapshots",
                result.AuditEvents, result.Snapshots);
            // M12.1 — an explicit /sync is the user asking for fresh data, so re-warm
            // the blob cache with forceRefresh (this also stamps LastWarmedUtc).
            await cacheWarmer.WarmAsync(graph, tenantId, force: true);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Sync failed");
        }
    });

    return Results.Accepted();
});

// GET /audit — the audit-event timeline (time-machine).
app.MapGet("/audit", async (DateTime? from, DateTime? to, string? q, ISnapshotStore store, AuthSession auth) =>
{
    var events = await store.QueryAuditAsync(from, to, q, tenantId: auth.ActiveProfile?.TenantId);
    return Results.Ok(events.Select(e => new AuditEventDto(
        e.Id, e.Timestamp.ToString("o"), e.Actor, e.Action, e.ObjectType, e.ObjectId, e.ObjectName)));
});

// GET /drift — drift between two config snapshots of an object. With no snapshot
// ids, diffs the latest snapshot against the one before it.
app.MapGet("/drift", async (string objectId, string? baseSnapshotId, string? headSnapshotId, ISnapshotStore store, AuthSession auth) =>
{
    var history = await store.GetSnapshotsForObjectAsync(objectId, tenantId: auth.ActiveProfile?.TenantId); // newest first
    if (history.Count == 0)
        return Results.Ok(new DriftRecordDto(objectId, null, null, Array.Empty<DriftChangeDto>()));

    var head = headSnapshotId is not null
        ? history.FirstOrDefault(s => s.SnapshotId == headSnapshotId)
        : history[0];
    if (head is null) return Results.NotFound();

    var @base = baseSnapshotId is not null
        ? history.FirstOrDefault(s => s.SnapshotId == baseSnapshotId)
        : history.SkipWhile(s => s.SnapshotId != head.SnapshotId).Skip(1).FirstOrDefault();

    var changes = @base is null
        ? new List<DriftChangeDto>() // first snapshot for this object — nothing to diff against
        : JsonDrift.Diff(@base.BodyJson, head.BodyJson);

    return Results.Ok(new DriftRecordDto(objectId, @base?.SnapshotId, head.SnapshotId, changes));
});

// GET /objects — objects with config-snapshot history, for the drift picker.
// Objects with detected drift (their two latest snapshots differ) sort to the top,
// most-changed first, so the picker leads with what actually changed.
app.MapGet("/objects", async (ISnapshotStore store, AuthSession auth) =>
    // Shared with the headless drift-gate CLI (DriftEvaluator) so both stay in lockstep.
    Results.Ok(await DriftEvaluator.EvaluateAsync(store, auth.ActiveProfile?.TenantId)));

// POST /preview-diff — field-level diff of two object bodies (current vs the
// pending edit) using the SAME engine the drift timeline uses, so every write can
// be gated behind a visible change set (M6 safe-write rail). Pure compute, no auth.
app.MapPost("/preview-diff", (PreviewDiffRequest req) =>
    Results.Ok(JsonDrift.Diff(req.Before ?? "{}", req.After ?? "{}")));

// POST /snapshots — capture the body the client just wrote so the edit is itself
// time-travelable (snapshot-on-write). Dedups on content hash, so a no-op edit
// appends nothing. Returns whether a new snapshot was actually stored.
app.MapPost("/snapshots", async (SnapshotCaptureRequest r, ISnapshotStore store, AuthSession auth) =>
{
    var rec = new ConfigSnapshotRecord(
        Guid.NewGuid().ToString("n"), r.ObjectId, r.ObjectType, r.ObjectName,
        DateTime.UtcNow, r.BodyJson,
        TenantId: auth.ActiveProfile?.TenantId);
    var stored = await store.AppendSnapshotIfChangedAsync(rec);
    if (stored is not null) await store.CommitIndexAsync();
    return Results.Ok(new { captured = stored is not null, snapshotId = stored?.SnapshotId });
});

// GET /snapshots?objectId= — an object's snapshot history (newest first) for the
// restore/undo picker. Each row carries the full body so "restore" re-PATCHes it.
app.MapGet("/snapshots", async (string objectId, ISnapshotStore store, AuthSession auth) =>
{
    var snaps = await store.GetSnapshotsForObjectAsync(objectId, tenantId: auth.ActiveProfile?.TenantId);
    return Results.Ok(snaps.Select(s => new SnapshotSummaryDto(
        s.SnapshotId, s.CapturedUtc.ToString("o"), s.ObjectType, s.ObjectName, s.BodyJson)));
});

// ─── Management surfaces — full CRUD via per-resource endpoint modules ────────
// Each module (service/Api/Endpoints/*Endpoints.cs) maps its surfaces'
// list/get/create/update/delete. They live in their own files (which name Graph
// types) so Program.cs stays free of the Microsoft.Graph.Beta.Models using that
// would shadow IResult and break Minimal API overload resolution.
app.MapDevices();
app.MapDeviceScripts();
app.MapApps();
app.MapEnrollment();
app.MapIdentity();
app.MapTenantAdmin();
app.MapGroups();
app.MapPermissionCheck();
app.MapAssignmentExplorer();
app.MapPolicyCompare();
app.MapDashboard();
app.MapSecurityPosture();
app.MapMaester();
app.MapPosture();       // M19 — /posture/score, /trend, /evidence-pack, /poam
app.MapBulk();
app.MapCache();
app.MapGitOps();
app.MapSimulate();
app.MapAutonomy();
app.MapTwin();
app.MapFleet();

// ─── M21 Ecosystem — shareable packs, playbooks, marketplace ──────────────────
// Action/discovery surfaces, not list-surfaces — they're not in Surfaces.cs /
// EndpointInventory.cs. Every write they cause terminates in existing machinery:
// pack adopt → M15 gitops plan/apply (loopback), playbook run → the M13
// /pending-changes inbox, marketplace install → a plugins.json row.
//
// Seed the bundled sample pack + playbook on first run so GET /packs|/playbooks are
// non-empty out of the box (marker-guarded; never clobbers operator edits).
EcosystemCatalog.EnsureSeeded();
app.MapPacks();
app.MapPlaybooks();
app.MapMarketplace();
app.MapProviders(); // M22 spike — provider-dispatch for the stub second provider

// GET /search — full-text across audit events + config snapshots (Lucene.NET).
app.MapGet("/search", async (string q, ISnapshotStore store, AuthSession auth) =>
{
    var hits = await store.SearchAsync(q, tenantId: auth.ActiveProfile?.TenantId);
    return Results.Ok(hits.Select(h => new SearchResultDto(h.Kind, h.Id, h.Score, h.Summary)));
});

// ─── M13.2 — Pending AI changes: the human-in-the-loop write queue ────────────
// An MCP propose_* tool enqueues here; the running app's "Pending AI changes" inbox
// shows the diff and approves/rejects. Approve REPLAYS the (kind, path, body) through
// the normal write pipeline (loopback) so per-surface quirks + snapshot + audit apply.

static PendingChangeDto ToPendingDto(PendingChangeRecord c)
{
    IReadOnlyList<DriftChangeDto> changes;
    try
    {
        changes = JsonSerializer.Deserialize<List<DriftChangeDto>>(
            string.IsNullOrWhiteSpace(c.DiffJson) ? "[]" : c.DiffJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
    }
    catch { changes = []; }

    // M16 — parse the attached blast-radius report (like the diff above) so the inbox
    // row can render severity + summary alongside the change set.
    BlastRadiusReportDto? blast = null;
    if (!string.IsNullOrWhiteSpace(c.SimulationJson))
    {
        try { blast = JsonSerializer.Deserialize<BlastRadiusReportDto>(c.SimulationJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
        catch { blast = null; }
    }

    return new PendingChangeDto(
        c.Id, c.Proposer, c.Kind, c.Path, c.ObjectId, c.ObjectName, changes, c.State, c.CreatedUtc.ToString("o"), blast);
}

// POST /pending-changes — enqueue a proposed write (called by the MCP propose_* tools).
app.MapPost("/pending-changes", async (CreatePendingChangeRequest r, ISnapshotStore store, AuthSession auth) =>
{
    if (string.IsNullOrWhiteSpace(r.Kind) || string.IsNullOrWhiteSpace(r.Path))
        return ApiResults.BadRequest("kind and path are required");
    if (r.BodyJson is { Length: > 256 * 1024 })
        return ApiResults.BadRequest("bodyJson exceeds the 256 KB proposal cap"); // M13.4 size cap
    var id = Guid.NewGuid().ToString("n");
    // M20 — bind the proposal to the active tenant so a cross-tenant approval is refused.
    await store.AppendPendingChangeAsync(new PendingChangeRecord(
        id, string.IsNullOrWhiteSpace(r.Proposer) ? "MCP client" : r.Proposer, r.Kind, r.Path,
        r.ObjectId, r.ObjectName, r.BodyJson, string.IsNullOrWhiteSpace(r.DiffJson) ? "[]" : r.DiffJson!,
        "pending", DateTime.UtcNow,
        SimulationJson: r.SimulationJson,          // M16 blast-radius report
        TenantId: auth.ActiveProfile?.TenantId));  // M20 tenant binding
    return Results.Ok(new { id, state = "pending" });
});

// GET /pending-changes?state= — the approval inbox (default: pending).
app.MapGet("/pending-changes", async (string? state, ISnapshotStore store, AuthSession auth) =>
{
    var rows = await store.GetPendingChangesAsync(string.IsNullOrWhiteSpace(state) ? "pending" : state);
    // M20 — scope the inbox to the active tenant so an MSP operator doesn't see (or act on)
    // another tenant's queue. Null TenantId = legacy/unscoped change, always shown.
    var activeTenant = auth.ActiveProfile?.TenantId;
    var scoped = rows.Where(c => c.TenantId is null || activeTenant is null ||
        string.Equals(c.TenantId, activeTenant, StringComparison.OrdinalIgnoreCase));
    return Results.Ok(scoped.Select(ToPendingDto));
});

// GET /pending-changes/{id} — single-change status (for the MCP get_change_status tool).
app.MapGet("/pending-changes/{id}", async (string id, ISnapshotStore store) =>
{
    var c = await store.GetPendingChangeAsync(id);
    return c is null
        ? Results.NotFound()
        : Results.Ok(new
        {
            id = c.Id, state = c.State, kind = c.Kind, path = c.Path,
            objectId = c.ObjectId, objectName = c.ObjectName, note = c.Note,
            createdUtc = c.CreatedUtc.ToString("o"), decidedUtc = c.DecidedUtc?.ToString("o"),
        });
});

// POST /pending-changes/{id}/approve — REPLAY the proposed write through the normal
// pipeline (loopback), then snapshot-on-write + audit, exactly like a human edit.
app.MapPost("/pending-changes/{id}/approve", async (string id, ISnapshotStore store, AuthSession auth) =>
{
    var c = await store.GetPendingChangeAsync(id);
    if (c is null) return Results.NotFound();
    if (c.State != "pending") return ApiResults.Conflict($"change is '{c.State}', not pending");
    // M20 — refuse a cross-tenant approval: the replay below applies through auth.Graph
    // (the ACTIVE tenant), so approving a change bound to a different tenant would write to
    // the wrong tenant. Require the operator to switch to the originating tenant first.
    if (c.TenantId is { } changeTenant &&
        !string.Equals(changeTenant, auth.ActiveProfile?.TenantId, StringComparison.OrdinalIgnoreCase))
        return ApiResults.Conflict($"change targets tenant '{changeTenant}'; switch to that tenant before approving");

    var (method, url) = c.Kind switch
    {
        "create" => (HttpMethod.Post, c.Path),
        "update" => (HttpMethod.Patch, $"{c.Path}/{c.ObjectId}"),
        "delete" => (HttpMethod.Delete, $"{c.Path}/{c.ObjectId}"),
        "assign" => (HttpMethod.Post, $"{c.Path}/{c.ObjectId}/assignments"),
        _ => (HttpMethod.Post, c.Path),
    };

    using var req = new HttpRequestMessage(method, url);
    if (c.BodyJson is not null)
        req.Content = new StringContent(c.BodyJson, System.Text.Encoding.UTF8, "application/json");

    // Telemetry: span + outcome counter over the loopback write-apply (see Telemetry.cs).
    using var applySpan = Telemetry.ActivitySource.StartActivity("write.apply");
    applySpan?.SetTag("cmpx.kind", c.Kind);
    applySpan?.SetTag("cmpx.path", c.Path);

    HttpResponseMessage resp;
    try { resp = await Loopback.Http.SendAsync(req); }
    catch (Exception ex)
    {
        Telemetry.WriteApply("error");
        applySpan?.SetStatus(ActivityStatusCode.Error, ex.Message);
        await store.SetPendingChangeStateAsync(id, "failed", ex.Message, DateTime.UtcNow);
        return ApiResults.ServerError($"apply failed: {ex.Message}");
    }

    var respBody = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode)
    {
        Telemetry.WriteApply("failed");
        applySpan?.SetStatus(ActivityStatusCode.Error);
        await store.SetPendingChangeStateAsync(id, "failed", $"{(int)resp.StatusCode}: {respBody}", DateTime.UtcNow);
        return ApiResults.ServerError($"apply failed ({(int)resp.StatusCode}): {respBody}");
    }
    Telemetry.WriteApply("applied");

    // create returns a fresh id — resolve it for the snapshot + audit linkage.
    var appliedId = c.ObjectId;
    if (c.Kind is "create")
    {
        try
        {
            using var d = JsonDocument.Parse(respBody);
            if (d.RootElement.TryGetProperty("id", out var v) && v.ValueKind == JsonValueKind.String)
                appliedId = v.GetString();
        }
        catch { /* keep existing */ }
    }

    // Snapshot-on-write so the AI-approved change is itself time-travelable.
    if ((c.Kind is "create" or "update") && c.BodyJson is not null && !string.IsNullOrEmpty(appliedId))
    {
        var stored = await store.AppendSnapshotIfChangedAsync(new ConfigSnapshotRecord(
            Guid.NewGuid().ToString("n"), appliedId!, c.Path, c.ObjectName, DateTime.UtcNow, c.BodyJson,
            TenantId: c.TenantId ?? auth.ActiveProfile?.TenantId));
        if (stored is not null) await store.CommitIndexAsync();
    }

    // Audit: the permanent record of the AI-proposed, operator-approved write.
    await store.AppendAuditEventAsync(new AuditEventRecord(
        Guid.NewGuid().ToString("n"), DateTime.UtcNow, $"{c.Proposer} (approved by operator)",
        $"{char.ToUpperInvariant(c.Kind[0]) + c.Kind[1..]} (AI)", c.Path, appliedId ?? c.ObjectId ?? "", c.ObjectName,
        c.TenantId ?? auth.ActiveProfile?.TenantId));
    await store.CommitIndexAsync();

    await store.SetPendingChangeStateAsync(id, "applied", null, DateTime.UtcNow);
    return Results.Ok(new { applied = true, id, appliedObjectId = appliedId });
});

// POST /pending-changes/{id}/reject — operator declines; recorded in the audit trail.
app.MapPost("/pending-changes/{id}/reject", async (string id, RejectPendingChangeRequest? r, ISnapshotStore store) =>
{
    var c = await store.GetPendingChangeAsync(id);
    if (c is null) return Results.NotFound();
    if (c.State != "pending") return ApiResults.Conflict($"change is '{c.State}', not pending");
    await store.SetPendingChangeStateAsync(id, "rejected", r?.Note, DateTime.UtcNow);
    await store.AppendAuditEventAsync(new AuditEventRecord(
        Guid.NewGuid().ToString("n"), DateTime.UtcNow, $"{c.Proposer} (rejected by operator)",
        $"{char.ToUpperInvariant(c.Kind[0]) + c.Kind[1..]} (AI, rejected)", c.Path, c.ObjectId ?? "", c.ObjectName,
        c.TenantId));
    await store.CommitIndexAsync();
    return Results.Ok(new { rejected = true, id });
});

// MCP server endpoint (M13) — Streamable HTTP at /mcp.
app.MapCmProjectXMcp();

app.Run("http://127.0.0.1:5099");
return 0;
