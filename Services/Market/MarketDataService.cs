using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Services.Market.Interfaces;
using WALLEve.Services.Sde.Interfaces;

namespace WALLEve.Services.Market;

/// <summary>
/// Service für Abfragen von MarketSnapshot-Daten
/// </summary>
public class MarketDataService : IMarketDataService
{
    private readonly WalletDbContext _dbContext;
    private readonly ISdeUniverseService _sdeUniverse;
    private readonly ILogger<MarketDataService> _logger;

    public MarketDataService(
        WalletDbContext dbContext,
        ISdeUniverseService sdeUniverse,
        ILogger<MarketDataService> logger)
    {
        _dbContext = dbContext;
        _sdeUniverse = sdeUniverse;
        _logger = logger;
    }

    public async Task<List<int>> GetTrackedTypeIdsAsync()
    {
        try
        {
            return await _dbContext.MarketSnapshots
                .Select(s => s.TypeId)
                .Distinct()
                .OrderBy(id => id)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tracked type IDs");
            return new List<int>();
        }
    }

    public async Task<List<int>> GetTrackedRegionIdsAsync()
    {
        try
        {
            return await _dbContext.MarketSnapshots
                .Select(s => s.RegionId)
                .Distinct()
                .OrderBy(id => id)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tracked region IDs");
            return new List<int>();
        }
    }

    public async Task<List<MarketSnapshot>> GetMarketSnapshotsAsync(
        int typeId,
        int? regionId = null,
        DateTime? from = null,
        DateTime? to = null)
    {
        try
        {
            var fromDate = from ?? DateTime.UtcNow.AddDays(-7);
            var toDate = to ?? DateTime.UtcNow;

            var query = _dbContext.MarketSnapshots
                .Where(s => s.TypeId == typeId)
                .Where(s => s.Timestamp >= fromDate && s.Timestamp <= toDate);

            if (regionId.HasValue)
            {
                query = query.Where(s => s.RegionId == regionId.Value);
            }

            return await query
                .OrderBy(s => s.Timestamp)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting market snapshots for typeId {TypeId}, regionId {RegionId}",
                typeId, regionId);
            return new List<MarketSnapshot>();
        }
    }

    public async Task<MarketSnapshot?> GetLatestSnapshotAsync(int typeId, int regionId)
    {
        try
        {
            return await _dbContext.MarketSnapshots
                .Where(s => s.TypeId == typeId && s.RegionId == regionId)
                .OrderByDescending(s => s.Timestamp)
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting latest snapshot for typeId {TypeId}, regionId {RegionId}",
                typeId, regionId);
            return null;
        }
    }

    public async Task<MarketDataStatistics> GetMarketDataStatisticsAsync()
    {
        try
        {
            var stats = new MarketDataStatistics
            {
                TotalSnapshots = await _dbContext.MarketSnapshots.CountAsync(),
                TrackedTypes = await _dbContext.MarketSnapshots.Select(s => s.TypeId).Distinct().CountAsync(),
                TrackedRegions = await _dbContext.MarketSnapshots.Select(s => s.RegionId).Distinct().CountAsync(),
                OldestSnapshot = await _dbContext.MarketSnapshots.MinAsync(s => (DateTime?)s.Timestamp),
                NewestSnapshot = await _dbContext.MarketSnapshots.MaxAsync(s => (DateTime?)s.Timestamp)
            };

            // Load region names
            var regionIds = await GetTrackedRegionIdsAsync();
            var sdeAvailable = await _sdeUniverse.IsDatabaseAvailableAsync();

            if (sdeAvailable)
            {
                foreach (var regionId in regionIds)
                {
                    var name = await _sdeUniverse.GetRegionNameAsync(regionId);
                    stats.RegionNames[regionId] = name ?? $"Region {regionId}";
                }

                // Load type names
                var typeIds = await GetTrackedTypeIdsAsync();
                foreach (var typeId in typeIds)
                {
                    var name = await _sdeUniverse.GetTypeNameAsync(typeId);
                    stats.TypeNames[typeId] = name ?? $"Type {typeId}";
                }
            }

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting market data statistics");
            return new MarketDataStatistics();
        }
    }
}
