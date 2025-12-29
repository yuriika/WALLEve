namespace WALLEve.Models.Database;

/// <summary>
/// Market Snapshot für Real-Time Tracking
/// Speichert Best Prices und Volumen für ein Item in einer Region zu einem Zeitpunkt
/// </summary>
public class MarketSnapshot
{
    public int Id { get; set; }
    public int RegionId { get; set; }
    public int TypeId { get; set; }
    public DateTime Timestamp { get; set; }

    // Location Information
    public int? BestBuySystemId { get; set; }
    public int? BestSellSystemId { get; set; }
    public long? BestBuyLocationId { get; set; }  // Station/Citadel with best buy order
    public long? BestSellLocationId { get; set; } // Station/Citadel with best sell order

    // Price & Volume
    public double? BestBuyPrice { get; set; }
    public double? BestSellPrice { get; set; }
    public long BuyVolume { get; set; }
    public long SellVolume { get; set; }
    public double? Spread { get; set; }
}
