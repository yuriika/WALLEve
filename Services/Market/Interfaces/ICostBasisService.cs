using WALLEve.Models.Database;

namespace WALLEve.Services.Market.Interfaces;

/// <summary>Ein Eintrag der Cost-Basis-Übersichtsseite (Item + ermittelter Wert).</summary>
public class CostBasisItemView
{
    public int TypeId { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public int TotalQuantity { get; set; }
    public double MarketValue { get; set; }
    public double? SellPrice { get; set; }

    public CostBasisSource Source { get; set; }
    public double? Value { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public int? EstimateRegionId { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>UI-Zustand: in der Übersicht für die Schätzung ausgewählt.</summary>
    public bool Selected { get; set; }
}

/// <summary>
/// UI-seitige Schnittstelle der Cost-Basis-Funktionen:
/// Schätz-Jobs anstoßen, manuelle Werte setzen, Übersicht laden,
/// Standard-Schätzregion verwalten.
/// </summary>
public interface ICostBasisService
{
    /// <summary>Übersicht aller Bestands-Items mit Cost-Basis-Status.</summary>
    Task<List<CostBasisItemView>> GetItemsAsync(int characterId);

    /// <summary>
    /// Schlanker Lookup: Cost Basis pro Einheit für EIN Item (kein Inventory-Load).
    /// null = keine Cost Basis bekannt (noch nie gespeichert).
    /// </summary>
    Task<double?> GetCostBasisPerUnitAsync(int characterId, int typeId);

    /// <summary>
    /// Stößt einen Schätz-Job für die angegebenen Items in der Region an
    /// (läuft als Hintergrund-Job, Fortschritt in den Settings/auf der Seite).
    /// </summary>
    Task<long> StartEstimateJobAsync(int characterId, IEnumerable<int> typeIds, int regionId);

    /// <summary>Setzt einen manuellen Einkaufspreis (gewinnt immer, Source=Manual).</summary>
    Task SetManualValueAsync(int characterId, int typeId, double value, DateTime? purchaseDate = null);

    /// <summary>Entfernt einen Eintrag → Item ist wieder "offen" (Source=None).</summary>
    Task ResetEntryAsync(int characterId, int typeId);

    /// <summary>Aktuell laufender/pausierter/unterbrochener Job eines Typs, falls vorhanden.</summary>
    Task<BackgroundJob?> GetActiveJobAsync(string jobType, int? characterId = null);

    /// <summary>Standard-Schätzregion (aus AppSettings, Fallback Jita 10000002).</summary>
    Task<int> GetDefaultEstimateRegionAsync();

    /// <summary>Setzt die Standard-Schätzregion persistent.</summary>
    Task SetDefaultEstimateRegionAsync(int regionId);

    /// <summary>Bekannte Handelsregionen für die Auswahl (Id → Name).</summary>
    IReadOnlyDictionary<int, string> KnownRegions { get; }

    /// <summary>JobType-Konstante für Schätz-Jobs (Ausführung im Collector).</summary>
    string EstimateJobType { get; }
}