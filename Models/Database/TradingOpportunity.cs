namespace WALLEve.Models.Database;

/// <summary>
/// Deterministisch berechnete Trading Opportunity.
/// Speichert erkannte Handelsmöglichkeiten mit konkreter Berechnungs-Evidenz.
/// Bewusst KEINE AI-Angaben: Die Analyse ist rein heuristisch (Issue #33);
/// Provenienz, Algorithmusversion und Datenqualität ersetzen die frühere "AI-Confidence".
/// </summary>
public class TradingOpportunity
{
    public int Id { get; set; }
    public int TypeId { get; set; }

    /// <summary>Charakter, für den diese Opportunity gilt (Bestands-/Verkaufs-Empfehlungen).</summary>
    public int CharacterId { get; set; }
    public string OpportunityType { get; set; } = string.Empty; // "inventory_sell", "station_trading", "arbitrage", "trend"

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

    /// <summary>Provenienz der Berechnung: deterministische Heuristik.</summary>
    public const string ProvenanceHeuristic = "heuristic";

    /// <summary>Provenienz vor Provenienz-Erfassung migrierter Datensätze (keine aktuelle Evidenz).</summary>
    public const string ProvenanceLegacy = "legacy";

    // Provenienz statt AI-Confidence
    /// <summary>
    /// Deterministischer Heuristik-Score (0-100), abgeleitet aus ROI/Evidenz.
    /// Kein AI-Confidence-Wert. Datensätze mit Provenance "legacy" stammen aus der
    /// Zeit der AI-Confidence-Angaben — ihr Score ist keine aktuelle Evidenz.
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Herkunft der Berechnung: "heuristic" (deterministische Analyse) oder
    /// "legacy" (vor Provenienz-Erfassung migrierte Datensätze).
    /// </summary>
    public string Provenance { get; set; } = string.Empty;

    /// <summary>Version der deterministischen Analyse ("inventory-sell-v1").</summary>
    public string? AlgorithmVersion { get; set; }

    /// <summary>
    /// Datenqualität der Eingaben: "complete" (gesicherte Cost-Basis aus Transaktion/Manuell)
    /// oder "partial" (geschätzte oder unbekannte Cost-Basis).
    /// </summary>
    public string? DataQuality { get; set; }

    /// <summary>Konkrete Berechnungs-Evidenz (Ort, Menge, Preis, Gebühren, ROI).</summary>
    public string Evidence { get; set; } = string.Empty;

    // Gebühren-Herkunft (Issue #46): Die effektiven Sätze und ihre Herkunft
    // (automatic/manual_override/estimated/unknown) werden zum Zeitpunkt der
    // Analyse persistiert — eine Empfehlung bleibt so nachvollziehbar, ob sie
    // auf echten Skills, manuellen Overrides oder konservativen Schätzungen basiert.
    public double? BrokerFeeRate { get; set; }
    public double? SalesTaxRate { get; set; }
    public string? BrokerFeeOrigin { get; set; }
    public string? SalesTaxOrigin { get; set; }
    public string? StandingsOrigin { get; set; }
    public DateTime? FeeEvaluatedAtUtc { get; set; }

    // Lifecycle
    public DateTime DetectedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Status { get; set; } = string.Empty; // "active", "executed", "expired", "invalid"

    // Performance tracking
    public DateTime? ExecutedAt { get; set; }
    public double? ActualProfit { get; set; }
}