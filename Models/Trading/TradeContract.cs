namespace WALLEve.Models.Trading;

/// <summary>
/// Unveränderlicher Trade-Vertrag (Issue #37): vollständig reproduzierbare Eingaben
/// und berechnete Ausgaben EINER Trade-Opportunity, additiv persistiert. Ein Vertrag
/// wird nie überschrieben — er dokumentiert genau die Analyse, die ihn erzeugt hat
/// (Algorithmusversion, Eingaben, Quellen wie Snapshot-IDs, Gebühren, Route, Alter).
/// </summary>
/// <remarks>
/// Eine Tabelle für alle Vertragsarten mit <see cref="Kind"/> als Diskriminator:
/// EF Core kann Table-per-Concrete-Type auf SQLite nicht mit automatischer
/// Schlüsselgenerierung abbilden (fehlende Sequence-Unterstützung) — die gewählte
/// flache Abbildung entspricht dem Repo-Stil. Die artspezifischen Spalten sind
/// nullable; DASS sie gefüllt sein müssen, erzwingt die Factory
/// (<see cref="TradeContractFactory"/>): Fehlende Pflichtfelder/Quellen ergeben
/// einen NICHT ausführbaren Vertrag (IsActionable = false mit Begründung) statt
/// scheinbar gültiger Default-Nullwerte. Legacy-Datensätze tragen IsLegacy = true
/// und keine erfundene neue Evidenz.
/// </remarks>
public class TradeContract
{
    public int Id { get; init; }

    /// <summary>Verweis auf die TradingOpportunity, aus der dieser Vertrag entstand.</summary>
    public int TradingOpportunityId { get; init; }

    /// <summary>Art des Trades (Diskriminator).</summary>
    public TradeKind Kind { get; init; }

    /// <summary>Owner (Charakter), für den dieser Trade gilt.</summary>
    public int CharacterId { get; init; }

    /// <summary>Gehandeltes Item.</summary>
    public int TypeId { get; init; }

    /// <summary>Version der Analyse, die diesen Vertrag erzeugt hat (z. B. "inventory-sell-v1").</summary>
    public string AlgorithmVersion { get; init; } = string.Empty;

    /// <summary>Zeitpunkt der Vertrags-Erstellung (Alter des Vertrags).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// false = Pflichtfelder/-Quellen fehlen: Der Vertrag ist nicht ausführbar.
    /// Die Begründung steht in <see cref="NotActionableReason"/>.
    /// </summary>
    public bool IsActionable { get; init; }

    /// <summary>Begründung (Deutsch), warum der Vertrag nicht ausführbar ist; null = ausführbar.</summary>
    public string? NotActionableReason { get; init; }

    /// <summary>
    /// true = vor der Vertrags-Erfassung migrierter Datensatz: Eingaben sind nicht
    /// vollständig reproduzierbar, es wurde keine neue Evidenz erfunden.
    /// </summary>
    public bool IsLegacy { get; init; }

    /// <summary>Evidenztext (Deutsch, unverändert roundtrip-fähig).</summary>
    public string Evidence { get; init; } = string.Empty;

    // --- gemeinsame Ausgaben (decimal statt double: ISK-Beträge roundtrip-fähig) ---

    /// <summary>Geschätzter Netto-Gewinn nach Gebühren (ISK).</summary>
    public decimal EstimatedProfit { get; init; }

    /// <summary>Benötigtes Kapital für den Trade (ISK).</summary>
    public decimal RequiredCapital { get; init; }

    /// <summary>Geschätzter Netto-ROI in Prozent.</summary>
    public decimal NetRoiPercent { get; init; }

    // --- InventorySell (Bestandsverkauf) ---

    /// <summary>Gespeicherte Cost Basis je Einheit (ISK).</summary>
    public decimal? UnitCostBasis { get; init; }

    /// <summary>Ausführbarer Verkaufspreis je Einheit (ISK, InventorySell/StationTrade/RouteTrade).</summary>
    public decimal? SellPricePerUnit { get; init; }

    /// <summary>Menge (Einheiten) — Pflichteingabe aller Vertragsarten.</summary>
    public int? Quantity { get; init; }

    /// <summary>Verkaufsort (Station/Citadel) — InventorySell/StationTrade/RouteTrade.</summary>
    public long? SellLocationId { get; init; }

    /// <summary>Anzeige-Name des Verkaufsorts (aufgelöst).</summary>
    public string? SellLocationLabel { get; init; }

    /// <summary>Brokergebühr in ISK (InventorySell, aus FeeCalculator mit echten Skills).</summary>
    public decimal? BrokerFee { get; init; }

    /// <summary>Verkaufssteuer in ISK (InventorySell, aus FeeCalculator mit echten Skills).</summary>
    public decimal? SalesTax { get; init; }

    /// <summary>Markt-Snapshot als Quote-Quelle (InventorySell/OrderAdjustment).</summary>
    public int? MarketSnapshotId { get; init; }

    /// <summary>Quelle der Cost Basis: "transaction", "estimate", "manual"; null = unbekannt (InventorySell).</summary>
    public string? CostBasisSource { get; init; }

    /// <summary>Netto-Erlös nach Gebühren (ISK, InventorySell/StationTrade/RouteTrade).</summary>
    public decimal? EstimatedNetProceeds { get; init; }

    /// <summary>Break-even-Verkaufspreis für die gespeicherte Basis (ISK, InventorySell).</summary>
    public decimal? BreakEvenPrice { get; init; }

    // --- OrderAdjustment (Order-Preisänderung) ---

    /// <summary>ID der bestehenden Order (OrderAdjustment).</summary>
    public long? OrderId { get; init; }

    /// <summary>Ort der Order (OrderAdjustment).</summary>
    public long? LocationId { get; init; }

    /// <summary>Aktueller Preis je Einheit (OrderAdjustment).</summary>
    public decimal? CurrentPricePerUnit { get; init; }

    /// <summary>Vorgeschlagener neuer Preis je Einheit (OrderAdjustment).</summary>
    public decimal? SuggestedPricePerUnit { get; init; }

    /// <summary>Brokergebühren-Rate des Charakters in Prozent (OrderAdjustment/StationTrade/RouteTrade).</summary>
    public decimal? BrokerRatePercent { get; init; }

    /// <summary>Relist-Discount (Advanced Broker Relations) in Prozent (OrderAdjustment).</summary>
    public decimal? RelistDiscountPercent { get; init; }

    /// <summary>Vorgeschlagene Modify-Fee in ISK (OrderAdjustment).</summary>
    public decimal? ModifyFee { get; init; }

    /// <summary>Zusätzlicher Netto-Erlös nach der Preisänderung (OrderAdjustment).</summary>
    public decimal? EstimatedExtraNetProceeds { get; init; }

    // --- StationTrade / RouteTrade ---

    /// <summary>Kauf-Ort (StationTrade/RouteTrade).</summary>
    public long? BuyLocationId { get; init; }

    /// <summary>Kauf-Region (StationTrade).</summary>
    public int? BuyRegionId { get; init; }

    /// <summary>Verkaufs-Region (StationTrade).</summary>
    public int? SellRegionId { get; init; }

    /// <summary>Kaufpreis je Einheit (StationTrade/RouteTrade).</summary>
    public decimal? BuyPricePerUnit { get; init; }

    /// <summary>Kauf-Snapshot als Quote-Quelle (StationTrade/RouteTrade).</summary>
    public int? BuyMarketSnapshotId { get; init; }

    /// <summary>Verkaufs-Snapshot als Quote-Quelle (StationTrade/RouteTrade).</summary>
    public int? SellMarketSnapshotId { get; init; }

    /// <summary>Verkaufssteuer-Rate in Prozent (StationTrade/RouteTrade).</summary>
    public decimal? SalesTaxPercent { get; init; }

    /// <summary>Sprungdistanz zwischen den Stationen (StationTrade).</summary>
    public int? JumpDistance { get; init; }

    /// <summary>Start-System der Route (RouteTrade).</summary>
    public int? StartSystemId { get; init; }

    /// <summary>Ziel-System der Route (RouteTrade).</summary>
    public int? EndSystemId { get; init; }

    /// <summary>Anzahl Sprünge der Route (RouteTrade).</summary>
    public int? JumpCount { get; init; }

    /// <summary>Route als kompaktes JSON: {"systems":[1,2,3],"highsec":2,"lowsec":1,"nullsec":0}.</summary>
    public string? RouteJson { get; init; }
}