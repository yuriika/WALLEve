namespace WALLEve.Models.Database;

/// <summary>
/// AI-generierte Trading Opportunity
/// Speichert erkannte Handelsmöglichkeiten mit AI-Reasoning
/// </summary>
public class TradingOpportunity
{
    public int Id { get; set; }
    public int TypeId { get; set; }
    public string OpportunityType { get; set; } = string.Empty; // "arbitrage", "station_trading", "trend"

    // Regions (null for single-region opportunities)
    public int? BuyRegionId { get; set; }
    public int? SellRegionId { get; set; }

    // Map integration - System IDs und Route-Daten
    public int? BuySystemId { get; set; }
    public int? SellSystemId { get; set; }
    public long? BuyLocationId { get; set; }  // Station/Citadel ID
    public long? SellLocationId { get; set; } // Station/Citadel ID
    public int? JumpDistance { get; set; }
    public string? RouteSecurityAnalysis { get; set; } // JSON: {highsec: 10, lowsec: 5, nullsec: 2}

    // Pricing
    public double? BuyPrice { get; set; }
    public double? SellPrice { get; set; }
    public double EstimatedProfit { get; set; }
    public double RequiredCapital { get; set; }

    // AI Assessment
    public double Confidence { get; set; } // 0-100
    public string AIModel { get; set; } = string.Empty; // e.g., "llama3.1:8b"
    public string Reasoning { get; set; } = string.Empty; // AI explanation

    // Lifecycle
    public DateTime DetectedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Status { get; set; } = string.Empty; // "active", "executed", "expired", "invalid"

    // Performance tracking
    public DateTime? ExecutedAt { get; set; }
    public double? ActualProfit { get; set; }
}
