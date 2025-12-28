using WALLEve.Models.Esi.Universe;
using WALLEve.Models.Map;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Map.Interfaces;

namespace WALLEve.Services.Map;

public class MapStatisticsService : IMapStatisticsService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MapStatisticsService> _logger;

    // In-memory cache (Singleton service, refresh every 5 minutes)
    private Dictionary<int, SystemStatistics>? _statisticsCache;
    private DateTime _lastRefresh = DateTime.MinValue;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public MapStatisticsService(
        IServiceScopeFactory scopeFactory,
        ILogger<MapStatisticsService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<List<SystemStatistics>> GetSystemJumpsAsync()
    {
        await RefreshCacheIfNeededAsync();
        return _statisticsCache?.Values.Where(s => s.Jumps > 0).ToList() ?? new List<SystemStatistics>();
    }

    public async Task<List<SystemStatistics>> GetSystemKillsAsync()
    {
        await RefreshCacheIfNeededAsync();
        return _statisticsCache?.Values.Where(s => s.TotalKills > 0).ToList() ?? new List<SystemStatistics>();
    }

    public async Task<SystemStatistics?> GetStatisticsForSystemAsync(int systemId)
    {
        await RefreshCacheIfNeededAsync();

        if (_statisticsCache?.TryGetValue(systemId, out var stats) == true)
            return stats;

        return null;
    }

    public async Task<Dictionary<int, SystemStatistics>> GetStatisticsBulkAsync(List<int> systemIds)
    {
        await RefreshCacheIfNeededAsync();

        var result = new Dictionary<int, SystemStatistics>();
        foreach (var systemId in systemIds)
        {
            if (_statisticsCache?.TryGetValue(systemId, out var stats) == true)
                result[systemId] = stats;
        }
        return result;
    }

    public async Task RefreshStatisticsAsync()
    {
        await _refreshLock.WaitAsync();
        try
        {
            await FetchStatisticsFromEsiAsync();
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task RefreshCacheIfNeededAsync()
    {
        // Refresh if cache is null or older than 5 minutes
        if (_statisticsCache != null &&
            DateTime.UtcNow - _lastRefresh < TimeSpan.FromMinutes(5))
            return;

        await _refreshLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (_statisticsCache != null &&
                DateTime.UtcNow - _lastRefresh < TimeSpan.FromMinutes(5))
                return;

            await FetchStatisticsFromEsiAsync();
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task FetchStatisticsFromEsiAsync()
    {
        try
        {
            _logger.LogInformation("Fetching system statistics from ESI...");

            // Fetch jumps and kills in parallel
            var jumpsTask = FetchSystemJumpsAsync();
            var killsTask = FetchSystemKillsAsync();

            await Task.WhenAll(jumpsTask, killsTask);

            var jumps = await jumpsTask;
            var kills = await killsTask;

            var cache = new Dictionary<int, SystemStatistics>();

            // Merge jumps
            if (jumps != null)
            {
                foreach (var jump in jumps)
                {
                    if (!cache.ContainsKey(jump.SystemId))
                        cache[jump.SystemId] = new SystemStatistics { SystemId = jump.SystemId };
                    cache[jump.SystemId].Jumps = jump.ShipJumps;
                }
            }

            // Merge kills
            if (kills != null)
            {
                foreach (var kill in kills)
                {
                    if (!cache.ContainsKey(kill.SystemId))
                        cache[kill.SystemId] = new SystemStatistics { SystemId = kill.SystemId };
                    cache[kill.SystemId].ShipKills = kill.ShipKills;
                    cache[kill.SystemId].NpcKills = kill.NpcKills;
                    cache[kill.SystemId].PodKills = kill.PodKills;
                }
            }

            _statisticsCache = cache;
            _lastRefresh = DateTime.UtcNow;

            _logger.LogInformation("Successfully cached statistics for {Count} systems", cache.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching statistics from ESI");
        }
    }

    private async Task<List<SystemJumps>?> FetchSystemJumpsAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var esiApi = scope.ServiceProvider.GetRequiredService<IEsiApiService>();
            return await esiApi.GetSystemJumpsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching system jumps from ESI");
            return null;
        }
    }

    private async Task<List<SystemKills>?> FetchSystemKillsAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var esiApi = scope.ServiceProvider.GetRequiredService<IEsiApiService>();
            return await esiApi.GetSystemKillsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching system kills from ESI");
            return null;
        }
    }
}
