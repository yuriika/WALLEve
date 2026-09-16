using System.Collections.Concurrent;
using WALLEve.Models.Esi.Markets;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Market;

/// <summary>
/// Regionen-Cache (#69): pro Region und Cachefenster genau EIN vollständiger
/// Regionalscan über den gemessenen Pfad (<c>GetAllRegionalMarketOrdersAsync</c> —
/// gestaffelte Parallelität, atomares Publizieren, ETag/304). Item-Abfragen werden
/// lokal auf dem gespeicherten Scan gefiltert — keine Vollregionsschleife pro Item.
/// Ein fehlgeschlagener oder abgebrochener Refresh publiziert keine Teildaten: der
/// letzte vollständige Stand bleibt aktiv (AK2).
/// </summary>
public sealed class RegionalMarketCacheService : IRegionalMarketCacheService
{
    /// <summary>ESI-Cachefenster für /markets/{region}/orders/ (ETag, max-age 5 min).</summary>
    public static readonly TimeSpan DefaultCacheWindow = TimeSpan.FromMinutes(5);

    private readonly IEsiApiService _esi;
    private readonly ILogger<RegionalMarketCacheService> _logger;
    private readonly TimeSpan _cacheWindow;
    private readonly ConcurrentDictionary<int, RegionMarketCacheEntry> _regions = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    public RegionalMarketCacheService(
        IEsiApiService esi,
        ILogger<RegionalMarketCacheService> logger,
        TimeSpan? cacheWindow = null)
    {
        _esi = esi;
        _logger = logger;
        _cacheWindow = cacheWindow ?? DefaultCacheWindow;
    }

    public async Task<IReadOnlyList<RegionalMarketOrder>> GetRegionOrdersAsync(
        int regionId, CancellationToken ct = default)
    {
        if (TryGetValid(regionId, out var existing))
        {
            return existing.Orders;
        }

        // Single-Flight: mehrere Aufrufer derselben Region teilen sich EINEN Scan.
        var gate = _locks.GetOrAdd(regionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (TryGetValid(regionId, out existing))
            {
                return existing.Orders;
            }

            var pageCount = 0;
            var orders = await _esi.GetAllRegionalMarketOrdersAsync(
                regionId, ct: ct,
                telemetrySink: t => pageCount = Math.Max(pageCount, t.Page));
            if (orders == null)
            {
                // Fehler/Cancellation: nichts publizieren. Bisherigen vollständigen
                // Stand halten, sonst leere Region melden (nicht als Neuscan wiederholen).
                if (_regions.TryGetValue(regionId, out var previous))
                {
                    _logger.LogWarning(
                        "Regionscan {RegionId} fehlgeschlagen/abgebrochen — veralteter vollständiger Stand ({Count} Orders) bleibt aktiv",
                        regionId, previous.Orders.Count);
                    return previous.Orders;
                }

                _logger.LogWarning("Regionscan {RegionId} fehlgeschlagen/abgebrochen — kein vollständiger Stand vorhanden", regionId);
                return Array.Empty<RegionalMarketOrder>();
            }

            var entry = new RegionMarketCacheEntry(orders, DateTime.UtcNow, pageCount);
            _regions[regionId] = entry;
            _logger.LogInformation(
                "Regionscan {RegionId}: {Count} Orders vollständig gespeichert (Messgrundlage {ScannedAt:O})",
                regionId, orders.Count, entry.ScannedAtUtc);
            return orders;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<RegionalMarketOrder>> GetOrdersForTypeAsync(
        int regionId, int typeId, CancellationToken ct = default)
    {
        var orders = await GetRegionOrdersAsync(regionId, ct);
        if (orders.Count == 0)
        {
            return orders;
        }

        // Lokale Filterung statt ESI-Request pro Item (keine Vollregionsschleife pro Item).
        return orders.Where(o => o.TypeId == typeId).ToList();
    }

    public RegionalMarketCacheInfo? GetCacheInfo(int regionId)
    {
        if (!_regions.TryGetValue(regionId, out var entry))
        {
            return null;
        }

        return new RegionalMarketCacheInfo(
            regionId,
            entry.ScannedAtUtc,
            entry.Orders.Count,
            entry.PageCount,
            DateTime.UtcNow - entry.ScannedAtUtc <= _cacheWindow);
    }

    public void Clear()
    {
        _regions.Clear();
        _locks.Clear();
    }

    private bool TryGetValid(int regionId, out RegionMarketCacheEntry entry)
    {
        if (_regions.TryGetValue(regionId, out entry!) &&
            DateTime.UtcNow - entry.ScannedAtUtc <= _cacheWindow)
        {
            return true;
        }

        entry = null!;
        return false;
    }

    private sealed record RegionMarketCacheEntry(
        IReadOnlyList<RegionalMarketOrder> Orders,
        DateTime ScannedAtUtc,
        int PageCount);
}