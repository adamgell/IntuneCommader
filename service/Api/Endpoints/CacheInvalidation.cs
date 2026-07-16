using Intune.Commander.Core.Services;

namespace CmProjectX.Api;

// M12.1 — the single cache-invalidation point for writes (see docs/CACHE-M12.1.md).
// Called from the per-surface POST/PATCH/DELETE/assign handlers AFTER the Core write
// succeeds. This one place covers human edits, the MCP propose→approve REPLAY path,
// AND direct MCP writes — because approve-replay and MCP writes both re-issue the
// same per-surface HTTP write endpoints over Loopback, so there is nothing to
// invalidate in the pending-change machinery.
internal static class CacheInvalidation
{
    // Drop the LIST cache for a surface (mandatory on any write); on PATCH/DELETE
    // also drop the DETAIL key {cacheKey}/{id}. CREATE / assign pass id = null
    // (create has no id yet; assign only flips the LIST "assigned" badge, the cached
    // detail body is unchanged). No-op on NullCacheService / signed out.
    public static void OnWrite(ICacheService cache, AuthSession auth, string cacheKey, string? id = null)
    {
        var tid = auth.ActiveProfile?.TenantId;
        if (!cache.IsAvailable || tid is null) return;
        cache.Invalidate(tid, cacheKey);
        if (id is not null) cache.Invalidate(tid, $"{cacheKey}/{id}");
        Telemetry.CacheEviction(id is not null ? 2 : 1);
    }
}
