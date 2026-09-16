using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Esi.Markets;
using WALLEve.Services.Authentication.Interfaces;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Market;

/// <summary>
/// Background Service für kontinuierliche Market-Daten-Sammlung
/// Sammelt Order-Daten alle 5 Minuten und historische Daten täglich
/// </summary>
public class MarketDataCollectorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MarketDataCollectorService> _logger;

    // Major Trade Hubs to track
    private readonly int[] _trackedRegions =
    {
        10000002,  // The Forge (Jita)
        10000043,  // Domain (Amarr)
        10000032,  // Sinq Laison (Dodixie)
        10000030,  // Heimatar (Rens)
        10000042   // Metropolis (Hek)
    };

    // Popular trading items (PLEX, Injectors, etc.) - Start with small set for testing
    private readonly int[] _trackedTypeIds =
    {
        44992,  // PLEX
        40520,  // Large Skill Injector
        40519,  // Small Skill Injector
        34,     // Tritanium
        35,     // Pyerite
        36,     // Mexallon
        37,     // Isogen
        38,     // Nocxium
        39,     // Zydrine
        40,     // Megacyte
    };

    private DateTime _lastHistoryUpdate = DateTime.MinValue;
    private int _loopCount = 0;

    public MarketDataCollectorService(
        IServiceScopeFactory scopeFactory,
        ILogger<MarketDataCollectorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Market Data Collector Service starting...");

        try
        {
            // Wait a bit before starting to let the app initialize
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Market Data Collector Service cancelled during startup delay");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CollectMarketDataAsync(stoppingToken);

                // Check if we need to update historical data (once per day)
                if (DateTime.UtcNow - _lastHistoryUpdate > TimeSpan.FromHours(24))
                {
                    await CollectHistoricalDataAsync(stoppingToken);
                    _lastHistoryUpdate = DateTime.UtcNow;
                }

                // Bestands-Opportunities alle 15 Min (3 Loops à 5 Min) aktualisieren
                _loopCount++;
                if (_loopCount % 3 == 0)
                {
                    await RunInventoryAnalysisAsync(stoppingToken);
                }

                // Wait 5 minutes before next collection
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown, ignore
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Market Data Collector Service main loop");
                // Wait a bit longer on error
                try 
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (OperationCanceledException) { }
            }
        }

        _logger.LogInformation("Market Data Collector Service stopping...");
    }

    private async Task CollectMarketDataAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var marketDataService = scope.ServiceProvider.GetRequiredService<IMarketDataService>();
        var regionCache = scope.ServiceProvider.GetRequiredService<IRegionalMarketCacheService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<WalletDbContext>();

        // Favoriten/Watchlist pro Owner isoliert laden (AK3: Owner-Suchprofile nicht vermischen).
        // Der authentifizierte Character erhält Priorität, danach die übrigen Owner in fester Ordnung.
        var allFavorites = await dbContext.MarketFavorits
            .ToListAsync(ct);
        var favoritesByOwner = allFavorites
            .GroupBy(f => f.CharacterId)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.Select(f => f.TypeId).Distinct().ToList());

        var authService = scope.ServiceProvider.GetRequiredService<IEveAuthenticationService>();
        var authState = await authService.GetAuthStateAsync();
        var ownerPriority = new List<(int CharacterId, List<int> TypeIds)>();
        if (authState?.IsValid == true && favoritesByOwner.TryGetValue(authState.CharacterId, out var ownFavorites))
        {
            ownerPriority.Add((authState.CharacterId, ownFavorites));
        }

        foreach (var (characterId, typeIds) in favoritesByOwner.OrderBy(g => g.Key))
        {
            if (characterId == authState?.CharacterId) continue;
            ownerPriority.Add((characterId, typeIds));
        }

        var allTypeIds = _trackedTypeIds.ToHashSet();
        foreach (var (_, typeIds) in ownerPriority)
        {
            foreach (var typeId in typeIds)
            {
                allTypeIds.Add(typeId);
            }
        }

        // Auto-Track: Top-N Bestands-Items nach Marktwert (0 = aus).
        // Eigener try/catch: ein Inventory-Fehler (ESI/Token) darf die normale
        // Sammlung der Standard-Items + Favoriten NICHT blockieren.
        var autoTrackLimit = await GetAutoTrackLimitAsync(dbContext, ct);
        if (autoTrackLimit > 0)
        {
            try
            {
                if (authState?.IsValid == true)
                {
                    var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();
                    var items = await inventoryService.GetInventoryAsync(authState.CharacterId);
                    var topTypeIds = TrackSelection.SelectTopValueItems(items, autoTrackLimit);
                    foreach (var typeId in topTypeIds)
                    {
                        allTypeIds.Add(typeId);
                    }
                    _logger.LogInformation("Auto-track: adding Top {Limit} inventory items by market value ({Count} tracked total)",
                        topTypeIds.Count, allTypeIds.Count);
                }
                else
                {
                    _logger.LogInformation("Auto-track: no authenticated character — skipping inventory top items");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auto-track: inventory loading failed — continuing with standard items + favorites only");
            }
        }

        _logger.LogInformation("Starting market data collection for {RegionCount} regions and {TypeCount} items",
            _trackedRegions.Length, allTypeIds.Count);

        var timestamp = DateTime.UtcNow;

        foreach (var regionId in _trackedRegions)
        {
            if (ct.IsCancellationRequested) break;

            // Region pro Cachefenster EINMAL vollständig laden, danach lokal filtern
            // (#69: keine Vollregionsschleife pro Item; Messgrenzen/Cancellation des
            // Regionalscans werden vom Cache-Service übernommen).
            IReadOnlyList<RegionalMarketOrder> regionOrders;
            try
            {
                regionOrders = await regionCache.GetRegionOrdersAsync(regionId, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading regional market data for Region {RegionId}", regionId);
                continue;
            }

            if (regionOrders.Count == 0)
            {
                _logger.LogDebug("Region {RegionId}: keine Orders im Regionalscan — weiter", regionId);
                continue;
            }

            var cacheInfo = regionCache.GetCacheInfo(regionId);
            _logger.LogInformation(
                "Region {RegionId}: {Count} Orders aus Regionalscan (Messgrundlage {ScannedAt:O}, {Pages} Seiten, aus Cachefenster: {FromCache})",
                regionId, regionOrders.Count,
                cacheInfo?.ScannedAtUtc, cacheInfo?.PageCount, cacheInfo?.FromCacheWindow);

            var regionSnapshots = new List<MarketSnapshot>();

            // Priorität: eigene Items/Favoriten/Watchlist zuerst, dann übrige Owner,
            // dann Auto-Track-/Standard-Items — unterbrochene Läufe hinterlassen so
            // die nutzerspezifisch wichtigsten Daten zuerst vollständig.
            var prioritizedTypeIds = new List<int>();
            foreach (var (_, typeIds) in ownerPriority)
            {
                foreach (var typeId in typeIds)
                {
                    if (allTypeIds.Contains(typeId)) prioritizedTypeIds.Add(typeId);
                }
            }

            foreach (var typeId in prioritizedTypeIds)
            {
                allTypeIds.Remove(typeId);
            }

            // Eigene Items/Favoriten/Watchlist zuerst, danach übrige Owner-Items,
            // dann Auto-Track-/Standard-Items — unterbrochene Läufe hinterlassen so
            // die nutzerspezifisch wichtigsten Daten zuerst vollständig.
            foreach (var typeId in prioritizedTypeIds)
            {
                if (ct.IsCancellationRequested) break;
                var orders = regionOrders.Where(o => o.TypeId == typeId).ToList();
                if (orders.Count == 0) continue;

                regionSnapshots.Add(ComputeSnapshot(regionId, typeId, orders, timestamp));
            }

            // Übrige Items (Auto-Track + Standard) nach Rang.
            foreach (var typeId in allTypeIds)
            {
                if (ct.IsCancellationRequested) break;
                var orders = regionOrders.Where(o => o.TypeId == typeId).ToList();
                if (orders.Count == 0) continue;

                regionSnapshots.Add(ComputeSnapshot(regionId, typeId, orders, timestamp));
            }

            // Nach jedem Regionalscan speichern: ein Abbruch zwischen Regionen verliert
            // keinen bereits vollständig berechneten Regionenstand (AK3: Datenalter).
            if (regionSnapshots.Any())
            {
                try
                {
                    await dbContext.MarketSnapshots.AddRangeAsync(regionSnapshots, ct);
                    await dbContext.SaveChangesAsync(ct);

                    _logger.LogInformation("Successfully saved {Count} market snapshots for region {RegionId} to database",
                        regionSnapshots.Count, regionId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving market snapshots for region {RegionId}", regionId);
                }
            }
        }

        // Cleanup old snapshots (keep only last 7 days)
        try
        {
            await CleanupOldSnapshotsAsync(dbContext, ct);
        }
        catch (OperationCanceledException)
        {
            // Shutdown während Cleanup — unkritisch
        }
    }

    private static MarketSnapshot ComputeSnapshot(
        int regionId, int typeId, IReadOnlyList<RegionalMarketOrder> orders, DateTime timestamp)
    {
        var buyOrders = orders.Where(o => o.IsBuyOrder).ToList();
        var sellOrders = orders.Where(o => !o.IsBuyOrder).ToList();

        var bestBuyOrder = buyOrders.OrderByDescending(o => o.Price).FirstOrDefault();
        var bestSellOrder = sellOrders.OrderBy(o => o.Price).FirstOrDefault();

        var bestBuyPrice = bestBuyOrder?.Price;
        var bestSellPrice = bestSellOrder?.Price;
        var buyVolume = buyOrders.Sum(o => (long)o.VolumeRemain);
        var sellVolume = sellOrders.Sum(o => (long)o.VolumeRemain);

        double? spread = null;
        if (bestBuyPrice.HasValue && bestSellPrice.HasValue && bestBuyPrice.Value > 0)
        {
            spread = ((bestSellPrice.Value - bestBuyPrice.Value) / bestBuyPrice.Value) * 100;
        }

        return new MarketSnapshot
        {
            RegionId = regionId,
            TypeId = typeId,
            Timestamp = timestamp,
            BestBuyPrice = bestBuyPrice,
            BestSellPrice = bestSellPrice,
            BestBuySystemId = bestBuyOrder?.SystemId,
            BestSellSystemId = bestSellOrder?.SystemId,
            BestBuyLocationId = bestBuyOrder?.LocationId,
            BestSellLocationId = bestSellOrder?.LocationId,
            BuyVolume = buyVolume,
            SellVolume = sellVolume,
            Spread = spread
        };
    }

    private async Task CollectHistoricalDataAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting historical market data collection for {RegionCount} regions and {TypeCount} items",
            _trackedRegions.Length, _trackedTypeIds.Length);

        using var scope = _scopeFactory.CreateScope();
        var esiService = scope.ServiceProvider.GetRequiredService<IEsiApiService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<WalletDbContext>();

        var historyEntries = new List<MarketHistory>();

        // Existierende Einträge EINMAL laden (statt AnyAsync pro Datum — N+1-Problem)
        var existingKeys = await dbContext.MarketHistory
            .Select(h => new { h.RegionId, h.TypeId, h.Date })
            .ToListAsync(ct);
        var existingSet = existingKeys
            .Select(k => (k.RegionId, k.TypeId, k.Date.Date))
            .ToHashSet();

        foreach (var regionId in _trackedRegions)
        {
            if (ct.IsCancellationRequested) break;

            foreach (var typeId in _trackedTypeIds)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var history = await esiService.GetMarketHistoryAsync(regionId, typeId);

                    if (history == null || history.Count == 0)
                    {
                        // Skip logging - too verbose
                        continue;
                    }

                    foreach (var entry in history)
                    {
                        // Dedup in-memory statt DB-Query pro Eintrag
                        var key = (regionId, typeId, entry.Date.Date);
                        if (!existingSet.Contains(key))
                        {
                            existingSet.Add(key);
                            historyEntries.Add(new MarketHistory
                            {
                                RegionId = regionId,
                                TypeId = typeId,
                                Date = entry.Date,
                                Average = entry.Average,
                                Highest = entry.Highest,
                                Lowest = entry.Lowest,
                                Volume = entry.Volume,
                                OrderCount = entry.OrderCount
                            });
                        }
                    }

                    // Skip verbose logging for each collection
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error collecting historical data for Type {TypeId} in Region {RegionId}", typeId, regionId);
                }

                // Delay between requests
                await Task.Delay(200, ct);
            }
        }

        // Save historical data
        if (historyEntries.Any())
        {
            try
            {
                await dbContext.MarketHistory.AddRangeAsync(historyEntries, ct);
                await dbContext.SaveChangesAsync(ct);

                _logger.LogInformation("Successfully saved {Count} historical market entries to database", historyEntries.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving historical market data to database");
            }
        }
    }

    private async Task CleanupOldSnapshotsAsync(WalletDbContext dbContext, CancellationToken ct)
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-7);
            var oldSnapshots = await dbContext.MarketSnapshots
                .Where(s => s.Timestamp < cutoffDate)
                .ToListAsync(ct);

            if (oldSnapshots.Any())
            {
                dbContext.MarketSnapshots.RemoveRange(oldSnapshots);
                await dbContext.SaveChangesAsync(ct);

                _logger.LogInformation("Cleaned up {Count} old market snapshots (older than 7 days)", oldSnapshots.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cleaning up old market snapshots");
        }
    }

    /// <summary>
    /// Liest das Auto-Track-Limit aus den AppSettings (Key "MarketData.AutoTrackTopItems").
    /// 0 oder fehlend = Auto-Tracking aus; eintrag im Format "25".
    /// </summary>
    private static async Task<int> GetAutoTrackLimitAsync(WalletDbContext db, CancellationToken ct)
    {
        var setting = await db.AppSettings.FindAsync("MarketData.AutoTrackTopItems");
        if (setting == null) return 0;
        return int.TryParse(setting.Value, out var limit) ? Math.Max(0, limit) : 0;
    }

    /// <summary>
    /// Aktualisiert die Bestands-Opportunities (inventory_sell) im Hintergrund.
    /// Eigener try/catch: Fehler dürfen die normale Marktdaten-Sammlung nicht stoppen.
    /// </summary>
    private async Task RunInventoryAnalysisAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var analysisService = scope.ServiceProvider.GetRequiredService<IMarketAnalysisService>();
            var opportunities = await analysisService.AnalyzeMarketDataAsync();
            _logger.LogInformation("Inventory analysis (15-min): {Count} active opportunities", opportunities.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Inventory analysis (15-min) failed — continuing market data collection");
        }
    }
}
