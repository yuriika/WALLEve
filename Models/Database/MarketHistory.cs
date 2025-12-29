namespace WALLEve.Models.Database;

/// <summary>
/// Historische Market-Daten aus ESI
/// Tägliche Statistiken für Price Trend Analysis
/// </summary>
public class MarketHistory
{
    public int Id { get; set; }
    public int RegionId { get; set; }
    public int TypeId { get; set; }
    public DateTime Date { get; set; }

    public double Average { get; set; }
    public double Highest { get; set; }
    public double Lowest { get; set; }
    public long Volume { get; set; }
    public long OrderCount { get; set; }
}
