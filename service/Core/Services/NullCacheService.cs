namespace Intune.Commander.Core.Services;

/// <summary>
/// No-op cache used when the LiteDB database file is unavailable (e.g. locked
/// by another running instance). All reads return null (cache miss) and all
/// writes are silently discarded. The app continues to function — just without
/// any caching benefit.
/// </summary>
public sealed class NullCacheService : ICacheService
{
    public bool IsAvailable => false;

    public List<T>? Get<T>(string tenantId, string dataType) => null;
    public T? GetSingle<T>(string tenantId, string dataType) where T : class => null;
    public void Set<T>(string tenantId, string dataType, List<T> items, TimeSpan? ttl = null) { }
    public void SetSingle<T>(string tenantId, string dataType, T item, TimeSpan? ttl = null) where T : class { }
    public void Invalidate(string tenantId, string? dataType = null) { }
    public int CleanupExpired() => 0;
    public (DateTime CachedAt, int ItemCount)? GetMetadata(string tenantId, string dataType) => null;

    public Task<List<T>?> GetAsync<T>(string tenantId, string dataType) => Task.FromResult<List<T>?>(null);
    public Task<T?> GetSingleAsync<T>(string tenantId, string dataType) where T : class => Task.FromResult<T?>(null);
    public Task SetAsync<T>(string tenantId, string dataType, List<T> items, TimeSpan? ttl = null) => Task.CompletedTask;
    public Task SetSingleAsync<T>(string tenantId, string dataType, T item, TimeSpan? ttl = null) where T : class => Task.CompletedTask;
    public Task InvalidateAsync(string tenantId, string? dataType = null) => Task.CompletedTask;
    public Task<(DateTime CachedAt, int ItemCount)?> GetMetadataAsync(string tenantId, string dataType)
        => Task.FromResult<(DateTime CachedAt, int ItemCount)?>(null);

    public void Dispose() { }
}
