using WALLEve.Models.Database;

namespace WALLEve.Services.Market.Interfaces;

/// <summary>
/// Service für Abfragen von MarketSnapshot-Daten aus der Datenbank
/// </summary>
public interface IMarketDataService
{
    /// <summary>
    /// Holt alle verfügbaren TypeIds, für die Marktdaten existieren
    /// </summary>
    Task<List<int>> GetTrackedTypeIdsAsync();

    /// <summary>
    /// Holt alle Regionen, für die Marktdaten existieren
    /// </summary>
    Task<List<int>> GetTrackedRegionIdsAsync();

    /// <summary>
    /// Holt MarketSnapshots für ein bestimmtes Item in einer Region über einen Zeitraum
    /// </summary>
    /// <param name="typeId">Item Type ID</param>
    /// <param name="regionId">Region ID (optional, null = alle Regionen)</param>
    /// <param name="from">Start-Zeitpunkt (optional, default: vor 7 Tagen)</param>
    /// <param name="to">End-Zeitpunkt (optional, default: jetzt)</param>
    Task<List<MarketSnapshot>> GetMarketSnapshotsAsync(
        int typeId,
        int? regionId = null,
        DateTime? from = null,
        DateTime? to = null);

    /// <summary>
    /// Holt den neuesten Snapshot für ein Item in einer Region
    /// </summary>
    Task<MarketSnapshot?> GetLatestSnapshotAsync(int typeId, int regionId);

    /// <summary>
    /// Holt Statistiken über alle MarketSnapshots
    /// </summary>
    Task<MarketDataStatistics> GetMarketDataStatisticsAsync();
}

/// <summary>
/// Statistiken über die gesammelten Marktdaten
/// </summary>
public class MarketDataStatistics
{
    public int TotalSnapshots { get; set; }
    public int TrackedTypes { get; set; }
    public int TrackedRegions { get; set; }
    public DateTime? OldestSnapshot { get; set; }
    public DateTime? NewestSnapshot { get; set; }
    public Dictionary<int, string> RegionNames { get; set; } = new();
    public Dictionary<int, string> TypeNames { get; set; } = new();
}
