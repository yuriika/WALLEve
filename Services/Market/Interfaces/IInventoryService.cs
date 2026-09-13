using WALLEve.Models.Esi.Character;

namespace WALLEve.Services.Market.Interfaces;

public interface IInventoryService
{
    Task<List<InventoryItem>> GetInventoryAsync(int characterId);
    Task<PortfolioOverview> GetPortfolioOverviewAsync(int characterId);
    Task<List<InventoryItem>> GetPrioritizedItemsAsync(int characterId, InventorySortMode sortMode = InventorySortMode.Opportunity);
}

public class InventoryItem
{
    /// <summary>Owner des Bestands — Owner/Type-Aggregat ist damit explizit.</summary>
    public int OwnerCharacterId { get; set; }
    public int TypeId { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public int TotalQuantity { get; set; }

    /// <summary>
    /// Explizite Owner/Type/Location-Aggregate: je tatsächlichem Ort eine Zeile
    /// (Stations, Container etc.) statt einer erfundenen „Primary"-Location.
    /// </summary>
    public List<InventoryLocationAggregate> Locations { get; set; } = new();

    /// <summary>
    /// Ortsgebundene Verkaufskontexte: EIN Eintrag je tatsächlichem Ort, mit Menge
    /// und Netto-Erlös genau dieses Ortes. Verhindert, dass Mengen mehrerer Orte
    /// stillschweigend zu einem verkaufbaren Stapel verschmolzen werden.
    /// </summary>
    public List<InventorySellContext> SellContexts { get; set; } = new();

    /// <summary>Summe der Mengen an aufgelösten Handelsplätzen (nicht blockierte Kontexte).</summary>
    public int ResolvedSellQuantity => SellContexts.Where(c => !c.IsBlocked).Sum(c => c.Quantity);

    /// <summary>true = Menge liegt an mehreren Orten: kein gemeinsamer verkaufbarer Stapel.</summary>
    public bool HasMultipleLocations => Locations.Count > 1;

    /// <summary>true = mindestens ein Ort ist kein aufgelöster Handelsplatz (Container/unbekannt).</summary>
    public bool HasUnresolvedLocation => Locations.Any(l => !l.IsMarketVenue);

    /// <summary>true = genau ein aufgelöster Handelsplatz — nur dann ist eine aggregierte Simulation zulässig.</summary>
    public bool CanSimulateAggregate => Locations.Count == 1 && !HasUnresolvedLocation;

    /// <summary>Hinweis für die UI, warum keine aggregierte Verkaufsaktion angeboten wird (leer = unkritisch).</summary>
    public string SellContextNote { get; set; } = string.Empty;

    public double? BestBuyPrice { get; set; }
    public double? BestSellPrice { get; set; }
    public double? AveragePrice { get; set; }
    public double? SpreadPercent { get; set; }
    public double? CostBasisPerUnit { get; set; }

    /// <summary>Anzeige-Text der Cost-Basis-Quelle (Echt/Geschätzt/Manuell) — für die Detailansicht.</summary>
    public string? CostBasisSourceLabel { get; set; }
    public bool IsMined { get; set; }
    public bool IsManufactured { get; set; }
    public double CurrentMarketValue => (BestSellPrice ?? 0) * TotalQuantity;
    public double? TotalCostBasis => CostBasisPerUnit.HasValue ? CostBasisPerUnit.Value * TotalQuantity : null;
    public double? UnrealizedProfit => TotalCostBasis.HasValue ? CurrentMarketValue - TotalCostBasis.Value : null;
    public double? UnrealizedRoiPercent => TotalCostBasis.HasValue && TotalCostBasis.Value > 0 ? (UnrealizedProfit!.Value / TotalCostBasis.Value) * 100 : null;
    public double? EstimatedNetProceeds { get; set; }
    public double? NetProfitAfterFees { get; set; }
    public double? NetRoiAfterFees { get; set; }
    public double? OpportunityScore { get; set; }
    public string Recommendation { get; set; } = string.Empty;
    public string RecommendationReason { get; set; } = string.Empty;
    public string? BuyPriceSource { get; set; }
    public string? SellPriceSource { get; set; }

    /// <summary>
    /// Kontextgebundener Vergleichsmarkt-Quote (Issue #63). null = kein
    /// Vergleichsmarkt konfiguriert. Der Vergleich ist IMMER referenzierend:
    /// er wird nie als ausführbarer Buy-/Sell-Preis des Bestands verwendet und
    /// ändert weder Empfehlung noch Provenienz des automatischen Markt-Quotes
    /// (Wechsel des Vergleichsmarkts erhält die Originalprovenienz).
    /// </summary>
    public ComparisonQuote? ComparisonQuote { get; set; }
    public List<CharacterAsset> RawAssets { get; set; } = new();
}

/// <summary>
/// Bewertungs-Quote des frei gewählten Vergleichsmarkts (Issue #63):
/// Quote-Side, Orderbuchtiefe, Quelle, Datenalter und Reference-only sind an
/// die Holdings-Bewertung gebunden. Der Quote stammt AUSSCHLIESSLICH aus dem
/// Markt-Snapshot der Region genau dieses Vergleichsmarkts — ein fremder
/// Regionspreis wird nie wiederverwendet; fehlt ein Snapshot, bleibt der Preis
/// Unknown bzw. nur als ESI-Referenzpreis sichtbar.
/// </summary>
public class ComparisonQuote
{
    /// <summary>Profilname des Vergleichsmarkts (z. B. „Jita").</summary>
    public string MarketName { get; set; } = string.Empty;

    public int RegionId { get; set; }
    public int SystemId { get; set; }

    /// <summary>
    /// Quelle des Vergleichs-Quotes: „market-snapshot" = lokaler Order-Buch-Snapshot
    /// der Vergleichsmarkt-Region; „esi-reference" = kein lokaler Snapshot, nur der
    /// globale ESI-Referenzpreis (Reference-only); „unknown" = keine Marktdaten.
    /// Wird zusammen mit Side, Tiefe, Alter und Reference-only dargestellt.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Beste Order-Seite am Vergleichsmarkt aus dem Snapshot; null = Seite nicht vorhanden.</summary>
    public double? BestBuyPrice { get; set; }
    public double? BestSellPrice { get; set; }

    /// <summary>Kumulierte Orderbuchtiefe (Volumina) am Vergleichsmarkt.</summary>
    public long BuyVolume { get; set; }
    public long SellVolume { get; set; }

    /// <summary>Zeitstempel des Snapshots am Vergleichsmarkt; null = kein lokaler Snapshot vorhanden.</summary>
    public DateTime? SnapshotTimestamp { get; set; }

    /// <summary>ESI-Referenzpreis (adjusted/average) — niemals ein ausführbarer Quote.</summary>
    public double? AveragePrice { get; set; }

    /// <summary>true = Snapshot älter als die Ausführbarkeitsgrenze: nur noch als Referenz sichtbar.</summary>
    public bool IsStale { get; set; }

    /// <summary>Deutscher Hinweis zur Datenlage am Vergleichsmarkt (leer = frischer zweiseitiger Quote).</summary>
    public string Note { get; set; } = string.Empty;
}

/// <summary>
/// Owner/Type/Location-Aggregat: Menge und Roh-Assets EINES Typs an EINEM
/// konkreten Ort. Wird explizit aus den Roh-Assets gruppiert; Container
/// (location_type "item") bleiben als unaufgelöste LocationId erhalten.
/// </summary>
public class InventoryLocationAggregate
{
    public long LocationId { get; set; }

    /// <summary>ESI location_type: "station", "solar_system", "item" (Container) oder "other".</summary>
    public string LocationType { get; set; } = string.Empty;

    public string LocationFlag { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public List<CharacterAsset> RawAssets { get; set; } = new();

    /// <summary>SDE-Name des Ortes, falls aufgelöst; null = unbekannt.</summary>
    public string? LocationName { get; set; }

    /// <summary>true = Handelsplatz (Station), an dem ein Verkauf ortsgebunden geplant werden kann.</summary>
    public bool IsMarketVenue => string.Equals(LocationType, "station", StringComparison.OrdinalIgnoreCase);

    /// <summary>Anzeige-Bezeichnung; Container/unbekannte Orte bleiben als Roh-ID sichtbar.</summary>
    public string LocationLabel
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(LocationName)) return LocationName;
            return LocationType.ToLowerInvariant() switch
            {
                "station" => $"Station {LocationId}",
                "solar_system" => $"System {LocationId}",
                "item" => $"Container {LocationId} (unaufgelöst)",
                _ => $"Unbekannter Ort {LocationId}"
            };
        }
    }

    /// <summary>Grund, warum an diesem Ort keine ortsgebundene Verkaufsempfehlung möglich ist (null = zulässig).</summary>
    public string? SellContextBlockReason => IsMarketVenue
        ? null
        : $"Ort ist kein aufgelöster Handelsplatz (LocationType \"{LocationType}\") — ortsgebundene Verkaufsempfehlung blockiert.";
}

/// <summary>
/// Ortsgebundener Verkaufskontext: Menge und Netto-Erlös EINES tatsächlichen Ortes.
/// Ein Kontext pro Ort — Mengen mehrerer Orte werden nie zu einem verkaufbaren Stapel
/// verschmolzen; Orte ohne aufgelösten Handelsplatz sind blockiert.
/// </summary>
public class InventorySellContext
{
    public long LocationId { get; set; }
    public string LocationType { get; set; } = string.Empty;
    public string LocationLabel { get; set; } = string.Empty;
    public int Quantity { get; set; }

    /// <summary>Verkaufspreis (beste Quelle) am Ortskontext; null = keine aktuellen Marktdaten.</summary>
    public double? SellPrice { get; set; }

    /// <summary>Netto-Erlös nach Fees genau für die Menge DIESES Ortes; null = blockiert/kein Preis.</summary>
    public double? EstimatedNetProceeds { get; set; }

    /// <summary>true = dieser Ort kann keinem Verkauf zugeordnet werden (Container/unbekannt).</summary>
    public bool IsBlocked { get; set; }

    public string? BlockReason { get; set; }
}

public class PortfolioOverview
{
    public int TotalItemTypes { get; set; }
    public long TotalQuantity { get; set; }
    public double TotalMarketValue { get; set; }
    public double? TotalCostBasis { get; set; }
    public int ItemCountWithCostBasis { get; set; }
    public int SellRecommendations { get; set; }
    public int HoldRecommendations { get; set; }
    public int WatchRecommendations { get; set; }
}

public enum InventorySortMode
{
    Opportunity,
    MarketValue,
    Profit,
    Roi,
    Quantity,
    Name
}