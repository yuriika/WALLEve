namespace WALLEve.Models.Stockpiles;

/// <summary>
/// Ergebniszeile der Stockpile-Berechnung (Issue #43): physisch, eingehend
/// und gebunden werden getrennt geführt. Shortage/Surplus werden NUR gegen
/// den physischen Bestand abgeleitet — Market-Orders werden nie still
/// addiert/subtrahiert (Items in Escrow-Sell-Orders sind nicht Teil der
/// Assets; ein Abzug wäre ein Doppel-Diskont, Akzeptanzkriterium #43-1).
/// Fehlende oder unvollständige Eingabequellen markieren die Zeile als
/// partial und blockieren die Ableitung, statt falsche Nullstände zu melden.
/// </summary>
public sealed class StockpileCalculationLine
{
    /// <summary>Id des zugrunde liegenden Stockpile-Ziels.</summary>
    public long TargetId { get; init; }

    /// <summary>EVE-TypeId des Ziels.</summary>
    public int TypeId { get; init; }

    /// <summary>Ort-/Container-Scope des Ziels (null = gesamter Bestand des Owners).</summary>
    public long? LocationId { get; init; }

    /// <summary>Zielbestand in Einheiten.</summary>
    public int TargetQuantity { get; init; }

    /// <summary>true, wenn das Ziel archiviert ist (bleibt bei includeArchived nachvollziehbar).</summary>
    public bool IsArchived { get; init; }

    /// <summary>
    /// Physischer Bestand im Ziel-Scope (Summe der Asset-Zeilen).
    /// null = nicht ableitbar (Quelle fehlt oder Scope nicht abgedeckt).
    /// </summary>
    public int? Physical { get; init; }

    /// <summary>
    /// Eingehende Menge: offenes Volumen aktiver Buy-Orders des Types im Scope.
    /// null = Orders-Quelle unvollständig (nicht als 0 präsentieren).
    /// </summary>
    public int? Inbound { get; init; }

    /// <summary>
    /// Gebundene Menge: offenes Volumen aktiver Sell-Orders des Types im Scope.
    /// null = Orders-Quelle unvollständig (nicht als 0 präsentieren).
    /// </summary>
    public int? Bound { get; init; }

    /// <summary>
    /// Fehlmenge = max(0, Ziel − physisch). null, wenn die Ableitung blockiert ist
    /// (physische Basis fehlt/umvollständig).
    /// </summary>
    public int? Shortage { get; init; }

    /// <summary>Überschuss = max(0, physisch − Ziel). null, wenn die Ableitung blockiert ist.</summary>
    public int? Surplus { get; init; }

    /// <summary>
    /// true, wenn mindestens eine Eingabequelle fehlt oder unvollständig ist —
    /// die Werte sind dann nicht vollständig vertrauenswürdig und dürfen nicht
    /// als belastbarer Null-/Vollbestand präsentiert werden.
    /// </summary>
    public bool IsPartial { get; init; }

    /// <summary>
    /// Konkreter Grund der Partial-Markierung:
    /// "physical-source-missing" | "scope-not-covered" | "orders-source-missing"
    /// (bei mehreren Gründen zählt der erste in dieser Prioritätsreihenfolge).
    /// </summary>
    public string? PartialReason { get; init; }
}