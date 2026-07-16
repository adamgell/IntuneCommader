using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CmProjectX.Api;

// ─── OpenTelemetry seam: one ActivitySource + Meter for the load-bearing paths ─
// Deliberately lightweight and ALWAYS-on at the instrumentation site: starting an
// Activity when nobody is listening is ~free (StartActivity returns null), and a
// Counter/Histogram record with no MeterListener is a cheap no-op. The exporters are
// wired in Program.cs ONLY when an OTLP endpoint (OTEL_EXPORTER_OTLP_ENDPOINT) or the
// console flag (CMPROJECTX_OTEL_CONSOLE=1) is configured — so with nothing set the
// app runs with zero telemetry overhead and needs no collector to exist.
//
// Instruments the three load-bearing paths:
//   • the blob read-through cache (hit/miss) + the live Graph fetch behind it —
//     CachedReader.List/GetAsync,
//   • cache eviction on write — CacheInvalidation.OnWrite,
//   • the loopback write-apply pipeline (MCP propose→approve REPLAY) — Program.cs.
internal static class Telemetry
{
    internal const string SourceName = "CmProjectX.Api";  // ActivitySource name (traces)
    internal const string MeterName = "CmProjectX.Api";   // Meter name (metrics) — same value, registered via AddMeter
    internal static readonly string Version =
        typeof(Telemetry).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    internal static readonly ActivitySource ActivitySource = new(SourceName, Version);
    internal static readonly Meter Meter = new(MeterName, Version);

    // Cache read-through hit/miss, tagged by surface data type (the cache key).
    private static readonly Counter<long> CacheHits = Meter.CreateCounter<long>(
        "cmpx.cache.hits", unit: "{read}", description: "Blob read-through cache hits.");
    private static readonly Counter<long> CacheMisses = Meter.CreateCounter<long>(
        "cmpx.cache.misses", unit: "{read}", description: "Blob read-through cache misses (fell through to a live fetch).");

    // Wall-clock of the live Graph fetch behind the read-through cache (on a miss).
    private static readonly Histogram<double> GraphFetch = Meter.CreateHistogram<double>(
        "cmpx.graph.fetch.duration", unit: "ms", description: "Duration of a live Graph fetch behind the read-through cache.");

    // Cache keys evicted after a write (CacheInvalidation.OnWrite).
    private static readonly Counter<long> CacheEvictions = Meter.CreateCounter<long>(
        "cmpx.cache.evictions", unit: "{eviction}", description: "Cache keys evicted after a write.");

    // Writes applied through the loopback apply pipeline (propose→approve replay), by outcome.
    private static readonly Counter<long> WriteApplies = Meter.CreateCounter<long>(
        "cmpx.write.applies", unit: "{apply}", description: "Writes applied through the loopback apply pipeline, tagged by outcome.");

    internal static void CacheHit(string dataType) =>
        CacheHits.Add(1, new KeyValuePair<string, object?>("cmpx.data_type", dataType));

    internal static void CacheMiss(string dataType) =>
        CacheMisses.Add(1, new KeyValuePair<string, object?>("cmpx.data_type", dataType));

    internal static void RecordGraphFetch(double milliseconds, string dataType, string op) =>
        GraphFetch.Record(milliseconds,
            new KeyValuePair<string, object?>("cmpx.data_type", dataType),
            new KeyValuePair<string, object?>("cmpx.op", op));

    internal static void CacheEviction(long count = 1) => CacheEvictions.Add(count);

    internal static void WriteApply(string outcome) =>
        WriteApplies.Add(1, new KeyValuePair<string, object?>("cmpx.outcome", outcome));
}
