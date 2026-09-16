using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
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
/// Die Instanz ist als Singleton registriert (Review-Fix): Collector-Scope und
/// UI-Scope (MarketDataService) müssen DIESELBE Instanz sehen, damit das
/// 5-Minuten-Fenster zwischen den Collector-Loops greift und die Messgrundlage
/// (GetCacheInfo/RegionScanBasis) in der UI ankommt. <see cref="IEsiApiService"/>
/// ist scoped registriert und wird deshalb pro Scan aus einem frischen Scope
/// aufgelöst — keine Captive Dependency im Singleton.
/// </summary>
public sealed class RegionalMarketCacheService : IRegionalMarketCacheService
{
    /// <summary>ESI-Cachefenster für /markets/{region}/orders/ (ETag, max-age 5 min).</summary>
    public static readonly TimeSpan DefaultCacheWindow = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RegionalMarketCacheService> _logger;
    private readonly TimeSpan _cacheWindow;
    private readonly ConcurrentDictionary<int, RegionMarketCacheEntry> _regions = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    public RegionalMarketCacheService(
        IServiceScopeFactory scopeFactory,
        ILogger<RegionalMarketCacheService> logger,
        TimeSpan? cacheWindow = null)
    {
        _scopeFactory = scopeFactory;
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

            // IEsiApiService ist scoped registriert (Auth-Kontext pro Anfrage) — die
            // Singleton-Instanz dieses Caches darf keinen scoped Dienst festhalten.
            // Pro Scan wird ein frischer Scope aufgelöst und sofort wieder verworfen.
            var pageCount = 0;
            using var scope = _scopeFactory.CreateScope();
            var esi = scope.ServiceProvider.GetRequiredService<IEsiApiService>();
            var orders = await esi.GetAllRegionalMarketOrdersAsync(
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