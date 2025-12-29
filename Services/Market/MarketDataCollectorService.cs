using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Services.Esi.Interfaces;

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

        // Wait a bit before starting to let the app initialize
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

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

                // Wait 5 minutes before next collection
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Market Data Collector Service main loop");
                // Wait a bit longer on error
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        _logger.LogInformation("Market Data Collector Service stopping...");
    }

    private async Task CollectMarketDataAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting market data collection for {RegionCount} regions and {TypeCount} items",
            _trackedRegions.Length, _trackedTypeIds.Length);

        using var scope = _scopeFactory.CreateScope();
        var esiService = scope.ServiceProvider.GetRequiredService<IEsiApiService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<WalletDbContext>();

        var snapshots = new List<MarketSnapshot>();
        var timestamp = DateTime.UtcNow;

        foreach (var regionId in _trackedRegions)
        {
            if (ct.IsCancellationRequested) break;

            foreach (var typeId in _trackedTypeIds)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    // Fetch orders for this item in this region
                    var orders = await esiService.GetRegionalMarketOrdersAsync(regionId, typeId, "all");

                    if (orders == null || orders.Count == 0)
                    {
                        // Skip logging for no orders - too verbose
                        continue;
                    }

                    // Calculate snapshot data
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

                    var snapshot = new MarketSnapshot
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

                    snapshots.Add(snapshot);

                    // Only log if there's a good spread (potential opportunity)
                    if (spread.HasValue && spread.Value > 5.0)
                    {
                        _logger.LogInformation("Good spread found for Type {TypeId} in Region {RegionId}: Buy={BuyPrice:N2}, Sell={SellPrice:N2}, Spread={Spread:N2}%",
                            typeId, regionId, bestBuyPrice, bestSellPrice, spread);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error collecting market data for Type {TypeId} in Region {RegionId}", typeId, regionId);
                }

                // Small delay to avoid overwhelming ESI
                await Task.Delay(100, ct);
            }
        }

        // Save all snapshots to database
        if (snapshots.Any())
        {
            try
            {
                await dbContext.MarketSnapshots.AddRangeAsync(snapshots, ct);
                await dbContext.SaveChangesAsync(ct);

                _logger.LogInformation("Successfully saved {Count} market snapshots to database", snapshots.Count);

                // Cleanup old snapshots (keep only last 7 days)
                await CleanupOldSnapshotsAsync(dbContext, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving market snapshots to database");
            }
        }
    }

    private async Task CollectHistoricalDataAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting historical market data collection for {RegionCount} regions and {TypeCount} items",
            _trackedRegions.Length, _trackedTypeIds.Length);

        using var scope = _scopeFactory.CreateScope();
        var esiService = scope.ServiceProvider.GetRequiredService<IEsiApiService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<WalletDbContext>();

        var historyEntries = new List<MarketHistory>();

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
                        // Check if entry already exists
                        var exists = await dbContext.MarketHistory
                            .AnyAsync(h => h.RegionId == regionId
                                        && h.TypeId == typeId
                                        && h.Date.Date == entry.Date.Date, ct);

                        if (!exists)
                        {
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
}
