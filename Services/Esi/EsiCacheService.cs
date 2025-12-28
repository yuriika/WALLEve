using System.Collections.Concurrent;
using WALLEve.Models.Esi;
using WALLEve.Services.Esi.Interfaces;

namespace WALLEve.Services.Esi;

/// <summary>
/// In-Memory Cache für ESI API Responses mit ETag-Unterstützung
/// </summary>
public class EsiCacheService : IEsiCacheService
{
    private readonly ConcurrentDictionary<string, object> _cache = new();
    private readonly ILogger<EsiCacheService> _logger;

    public EsiCacheService(ILogger<EsiCacheService> logger)
    {
        _logger = logger;
    }

    public ETagCacheEntry<T>? Get<T>(string endpoint)
    {
        if (_cache.TryGetValue(endpoint, out var entry))
        {
            if (entry is ETagCacheEntry<T> typedEntry)
            {
                if (typedEntry.IsValid())
                {
                    _logger.LogDebug("Cache HIT for {Endpoint}", endpoint);
                    return typedEntry;
                }
                else
                {
                    // Cache-Eintrag abgelaufen
                    _cache.TryRemove(endpoint, out _);
                    _logger.LogDebug("Cache EXPIRED for {Endpoint}", endpoint);
                }
            }
        }

        _logger.LogDebug("Cache MISS for {Endpoint}", endpoint);
        return null;
    }

    public void Set<T>(string endpoint, string etag, T data, DateTime? expires = null)
    {
        var cacheEntry = new ETagCacheEntry<T>
        {
            ETag = etag,
            Data = data,
            Expires = expires
        };

        _cache[endpoint] = cacheEntry;
        _logger.LogDebug("Cached {Endpoint} with ETag {ETag}, Expires: {Expires}",
            endpoint, etag, expires?.ToString() ?? "none");
    }

    public void CleanupExpired()
    {
        var expiredKeys = _cache
            .Where(kvp =>
            {
                if (kvp.Value is ETagCacheEntry<object> entry)
                {
                    return !entry.IsValid();
                }
                return false;
            })
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _cache.TryRemove(key, out _);
        }

        if (expiredKeys.Count > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired cache entries", expiredKeys.Count);
        }
    }

    public void Clear()
    {
        var count = _cache.Count;
        _cache.Clear();
        _logger.LogInformation("Cleared {Count} cache entries", count);
    }
}
