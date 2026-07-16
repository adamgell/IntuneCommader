using System.Diagnostics;
using Intune.Commander.Core.Services;

namespace CmProjectX.Api;

// M12.1 — blob read-through over the LiteDB ICacheService (see docs/CACHE-M12.1.md).
// Wraps a Core fetch as a delegate so one method-name-agnostic path caches every
// LIST/DETAIL surface, whether or not the underlying Core service is cache-aware
// (only AssignmentCheckerService has a cache ctor). The cache stores RAW Graph
// List<T> / single objects; DTO projection stays in the handler, after the read.
//
// Tenant scoping rides on CacheService's {tenantId}|{dataType} key. Callers MUST
// keep the signed-out 409 check (auth.Graph is null) ABOVE these calls, so the
// cache is never read while signed out. On NullCacheService (IsAvailable == false)
// every path degrades to a clean pass-through to the live fetch.
internal static class CachedReader
{
    // LIST read-through. Hit → cached List<T>; miss → fetch, populate, return.
    public static async Task<List<T>> ListAsync<T>(
        ICacheService cache, AuthSession auth, string cacheKey,
        Func<CancellationToken, Task<List<T>>> fetch, CancellationToken ct) where T : class
    {
        using var span = Telemetry.ActivitySource.StartActivity("cache.read");
        span?.SetTag("cmpx.op", "list");
        span?.SetTag("cmpx.data_type", cacheKey);

        var tid = auth.ActiveProfile?.TenantId;
        if (cache.IsAvailable && tid is not null && cache.Get<T>(tid, cacheKey) is { } hit)
        {
            span?.SetTag("cmpx.cache.hit", true);
            Telemetry.CacheHit(cacheKey);
            return hit;
        }
        span?.SetTag("cmpx.cache.hit", false);
        Telemetry.CacheMiss(cacheKey);

        var started = Stopwatch.GetTimestamp();
        var fresh = await fetch(ct);
        Telemetry.RecordGraphFetch(Stopwatch.GetElapsedTime(started).TotalMilliseconds, cacheKey, "list");

        if (cache.IsAvailable && tid is not null)
            cache.Set(tid, cacheKey, fresh);
        return fresh;
    }

    // DETAIL read-through. Keyed {cacheKey}/{id}; LAZY populate only — warm
    // (PrefetchAllToCacheAsync) fills LIST keys, never single-object keys, so
    // offline detail works only for objects opened at least once online. We do NOT
    // seed detail from list elements: several LIST Core methods return $select-shaped
    // partial objects, so that would serve truncated JSON.
    public static async Task<T?> GetAsync<T>(
        ICacheService cache, AuthSession auth, string cacheKey, string id,
        Func<CancellationToken, Task<T?>> fetch, CancellationToken ct) where T : class
    {
        using var span = Telemetry.ActivitySource.StartActivity("cache.read");
        span?.SetTag("cmpx.op", "get");
        span?.SetTag("cmpx.data_type", cacheKey);

        var tid = auth.ActiveProfile?.TenantId;
        var dk = $"{cacheKey}/{id}";
        if (cache.IsAvailable && tid is not null && cache.GetSingle<T>(tid, dk) is { } hit)
        {
            span?.SetTag("cmpx.cache.hit", true);
            Telemetry.CacheHit(cacheKey);
            return hit;
        }
        span?.SetTag("cmpx.cache.hit", false);
        Telemetry.CacheMiss(cacheKey);

        var started = Stopwatch.GetTimestamp();
        var fresh = await fetch(ct);
        Telemetry.RecordGraphFetch(Stopwatch.GetElapsedTime(started).TotalMilliseconds, cacheKey, "get");

        if (fresh is not null && cache.IsAvailable && tid is not null)
            cache.SetSingle(tid, dk, fresh);
        return fresh;
    }
}
