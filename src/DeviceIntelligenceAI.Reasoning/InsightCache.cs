using System.Collections.Concurrent;

namespace DeviceIntelligenceAI.Reasoning;

/// <summary>
/// Caches computed reasoning insights so they can be served instantly
/// when triggered by App Actions or agent queries without re-running inference.
/// </summary>
public sealed class InsightCache
{
    private readonly ConcurrentDictionary<string, CachedInsight> _cache = new();
    private readonly TimeSpan _defaultTtl;

    public InsightCache(TimeSpan? defaultTtl = null)
    {
        _defaultTtl = defaultTtl ?? TimeSpan.FromMinutes(30);
    }

    /// <summary>
    /// Store a reasoning result as a cached insight.
    /// </summary>
    public void Store(string key, ReasoningResult result, TimeSpan? ttl = null)
    {
        _cache[key] = new CachedInsight
        {
            Key = key,
            Result = result,
            CachedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow + (ttl ?? _defaultTtl)
        };
    }

    /// <summary>
    /// Try to get a cached insight. Returns null if expired or not found.
    /// </summary>
    public CachedInsight? Get(string key)
    {
        if (!_cache.TryGetValue(key, out var insight))
            return null;

        if (insight.ExpiresAt < DateTimeOffset.UtcNow)
        {
            _cache.TryRemove(key, out _);
            return null;
        }

        return insight;
    }

    /// <summary>
    /// Invalidate all cached insights (e.g., after new ingestion).
    /// </summary>
    public void InvalidateAll()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Invalidate a specific cached insight.
    /// </summary>
    public void Invalidate(string key)
    {
        _cache.TryRemove(key, out _);
    }

    /// <summary>
    /// Get all non-expired cached insights.
    /// </summary>
    public IReadOnlyList<CachedInsight> GetAll()
    {
        var now = DateTimeOffset.UtcNow;
        return _cache.Values
            .Where(i => i.ExpiresAt >= now)
            .OrderByDescending(i => i.CachedAt)
            .ToList();
    }

    /// <summary>
    /// Remove all expired entries.
    /// </summary>
    public int Cleanup()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = _cache.Where(kv => kv.Value.ExpiresAt < now).Select(kv => kv.Key).ToList();
        foreach (var key in expired)
        {
            _cache.TryRemove(key, out _);
        }
        return expired.Count;
    }
}

public sealed class CachedInsight
{
    public required string Key { get; init; }
    public required ReasoningResult Result { get; init; }
    public DateTimeOffset CachedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}
