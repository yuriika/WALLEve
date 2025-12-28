using WALLEve.Models.Map;

namespace WALLEve.Services.Map.Interfaces;

/// <summary>
/// Service für Live-Statistiken von ESI (Jumps, Kills)
/// </summary>
public interface IMapStatisticsService
{
    /// <summary>
    /// Holt System-Jump Statistiken von ESI
    /// </summary>
    Task<List<SystemStatistics>> GetSystemJumpsAsync();

    /// <summary>
    /// Holt System-Kill Statistiken von ESI
    /// </summary>
    Task<List<SystemStatistics>> GetSystemKillsAsync();

    /// <summary>
    /// Holt Statistiken für ein spezifisches System
    /// </summary>
    Task<SystemStatistics?> GetStatisticsForSystemAsync(int systemId);

    /// <summary>
    /// Holt Statistiken für mehrere Systeme (bulk operation)
    /// </summary>
    Task<Dictionary<int, SystemStatistics>> GetStatisticsBulkAsync(List<int> systemIds);

    /// <summary>
    /// Erzwingt Refresh der Statistiken von ESI
    /// </summary>
    Task RefreshStatisticsAsync();
}
