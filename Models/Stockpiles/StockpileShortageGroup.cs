namespace WALLEve.Models.Stockpiles;

/// <summary>
/// Fehlmengen-Gruppe: alle Shortage-Zeilen eines Orts/Scopes (Issue #59).
/// Der Group-Key ist die Ziel-Location; null = Gesamtbestand. Die Zeilen
/// behalten ihre Quellen getrennt (Physisch/Eingehend/Gebunden werden nie
/// still addiert oder zur Fehlmenge verrechnet — Fehlmenge leitet sich
/// ausschließlich aus dem physischen Bestand ab, #43).
/// </summary>
public sealed class StockpileShortageGroup
{
    /// <summary>Ort-/Container-Scope der Gruppe; null = Gesamtbestand.</summary>
    public long? LocationId { get; init; }

    /// <summary>Shortage-Zeilen dieser Gruppe (nur Zeilen mit belastbarer Fehlmenge &gt; 0).</summary>
    public IReadOnlyList<StockpileCalculationLine> Lines { get; init; } = Array.Empty<StockpileCalculationLine>();

    /// <summary>Summe der Fehlmengen der Gruppe (nur für die Anzeige, 64-Bit, #183).</summary>
    public long? TotalShortage
        => Lines.Count == 0 ? null : Lines.Sum(l => l.Shortage ?? 0);

    /// <summary>true, wenn mindestens eine Zeile der Gruppe partial ist.</summary>
    public bool HasPartialLines => Lines.Any(l => l.IsPartial);
}