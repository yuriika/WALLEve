using WALLEve.Models.Stockpiles;

namespace WALLEve.Services.Stockpiles;

/// <summary>
/// Deterministische Gruppierung der Shortage-Zeilen nach Ort (Issue #59).
/// Reine Funktion ohne DB-/ESI-Zugriffe, damit Regressionstests ohne
/// Live-Abhängigkeiten auskommen.
///
/// Regeln:
/// - Nur Zeilen mit belastbarer Fehlmenge (Shortage &gt; 0) zählen. Zeilen mit
///   null-Shortage (Quelle blockiert) sind KEINE Fehlmengen und werden nicht
///   als 0-Fehlmenge geführt — sie erscheinen im Fehlmengen-Kontext nicht.
/// - Archivierte Ziele sind nur mit includeArchived enthalten (bleiben sonst
///   außen vor und verfälschen die Beschaffungsliste nicht).
/// - Die Quellen bleiben getrennt: Physisch/Eingehend/Gebunden jeder Zeile
///   werden nie still addiert; die Fehlmenge bleibt an die physische Basis
///   gebunden (#43). Nur die Anzeige-Summe wird gebildet.
/// - Determinismus: Gruppen aufsteigend nach LocationId, Zeilen innerhalb
///   einer Gruppe aufsteigend nach TypeId. Gesamtbestand (null) zuerst,
///   dann scoped Orte.
/// </summary>
public static class StockpileShortageGrouper
{
    public static IReadOnlyList<StockpileShortageGroup> GroupByLocation(
        IReadOnlyList<StockpileCalculationLine> lines,
        bool includeArchived = false)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var shortage = lines
            .Where(l => (!l.IsArchived || includeArchived) && l.Shortage is > 0)
            .OrderBy(l => l.TypeId)
            .ToList();

        return shortage
            .GroupBy(l => l.LocationId)
            .OrderBy(g => g.Key.HasValue)
            .ThenBy(g => g.Key)
            .Select(g => new StockpileShortageGroup
            {
                LocationId = g.Key,
                Lines = g.ToList()
            })
            .ToList();
    }
}