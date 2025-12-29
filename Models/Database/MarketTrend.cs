namespace WALLEve.Models.Database;

/// <summary>
/// Market Trend Analysis Results
/// AI-generierte Trend-Erkennung aus historischen Daten
/// </summary>
public class MarketTrend
{
    public int Id { get; set; }
    public int TypeId { get; set; }
    public int RegionId { get; set; }

    // Trend data
    public string TrendType { get; set; } = string.Empty; // "bullish", "bearish", "sideways", "volatile"
    public double Strength { get; set; } // 0-100
    public double? PredictedChange { get; set; } // Predicted % change

    // Time window
    public string TimeWindow { get; set; } = string.Empty; // "24h", "7d", "30d"
    public DateTime AnalyzedAt { get; set; }

    // AI metadata
    public string AIModel { get; set; } = string.Empty;
    public string? Features { get; set; } // JSON for debugging
}
