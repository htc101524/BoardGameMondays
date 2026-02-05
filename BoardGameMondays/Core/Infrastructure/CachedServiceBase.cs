using Microsoft.Extensions.Caching.Memory;

namespace BoardGameMondays.Core.Infrastructure;

/// <summary>
/// Base class for services that use IMemoryCache.
/// Provides standard cache invalidation patterns and manages cache dependencies.
/// </summary>
public abstract class CachedServiceBase
{
    protected IMemoryCache Cache { get; }

    protected CachedServiceBase(IMemoryCache cache)
    {
        Cache = cache;
    }

    /// <summary>
    /// Invalidates cache entries specific to this service.
    /// Override this method to define service-specific cache invalidation logic.
    /// </summary>
    protected virtual void InvalidateCache()
    {
        // Default: no-op
        // Derived classes override to invalidate their specific cache keys
    }

    /// <summary>
    /// Helper to remove a single cache key.
    /// </summary>
    /// <param name="key">The cache key to remove.</param>
    protected void RemoveCacheKey(string key)
    {
        Cache.Remove(key);
    }

    /// <summary>
    /// Helper to remove multiple cache keys at once.
    /// </summary>
    /// <param name="keys">The cache keys to remove.</param>
    protected void RemoveCacheKeys(params string[] keys)
    {
        foreach (var key in keys)
        {
            Cache.Remove(key);
        }
    }

    /// <summary>
    /// Helper to remove a range of cache keys matching a prefix or pattern.
    /// Note: IMemoryCache doesn't natively support prefix-based invalidation,
    /// so services must track their own keys for bulk invalidation.
    /// </summary>
    /// <param name="keys">Enumerable of cache keys to remove.</param>
    protected void RemoveCacheKeys(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            Cache.Remove(key);
        }
    }
}
