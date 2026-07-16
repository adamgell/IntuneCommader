using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CmProjectX.Sync;

// M18 — the scheduled background watcher. This is the net-new "scheduler" half of
// M18 (the planner half lives in service/Api/Autonomy). It wraps the one-shot
// GraphDeltaSync in a cadence loop: every interval it invokes the registered tick
// delegate (the Api-side AutonomyEngine.RunTickAsync), which runs the full
// watch→detect→plan→simulate→propose loop and returns the next cadence to wait.
//
// This project (Sync) deliberately has NO dependency on service/Api: the tick is an
// injected delegate the host wires up, so the scheduler stays a thin timer and the
// orchestration (which needs Loopback / AuthSession / the planner) lives in Api.
//
// Graph 429 Retry-After is already honored by the SDK middleware (see GraphDeltaSync),
// so the scheduler only owns the OUTER inter-tick delay. Nothing here applies a write —
// the loop terminates at the human-gated /pending-changes inbox.
public sealed class AutonomyScheduler : BackgroundService
{
    // The tick: run one loop iteration; return the cadence (minutes) to wait before the
    // next, or null to fall back to the default (e.g. signed out / autonomy disabled).
    public Func<CancellationToken, Task<int?>>? Tick { get; set; }

    private readonly ILogger<AutonomyScheduler> _log;

    // Idle cadence used when no tick is registered yet, or a tick declines to set one
    // (signed out, policy disabled). Short enough to pick up sign-in / a policy flip
    // within a couple of minutes, long enough to stay quiet while idle.
    private static readonly TimeSpan IdleCadence = TimeSpan.FromMinutes(2);

    public AutonomyScheduler(ILogger<AutonomyScheduler> log) => _log = log;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("Autonomy scheduler started (idle cadence {Idle} min).", IdleCadence.TotalMinutes);

        // A brief startup delay so the host finishes binding + the store/auth initialize
        // before the first tick (mirrors how /sync is fire-and-forget post-startup).
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var wait = IdleCadence;
            try
            {
                if (Tick is { } tick)
                {
                    var cadenceMinutes = await tick(stoppingToken);
                    if (cadenceMinutes is { } m && m > 0)
                        wait = TimeSpan.FromMinutes(m);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // host shutting down
            }
            catch (Exception ex)
            {
                // A tick failure must never kill the BackgroundService — log and back off.
                _log.LogWarning(ex, "Autonomy tick failed; backing off to idle cadence.");
            }

            try { await Task.Delay(wait, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _log.LogInformation("Autonomy scheduler stopped.");
    }
}
