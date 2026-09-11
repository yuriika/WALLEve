using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Market;

/// <summary>
/// Auswahl-Logik für automatisch getrackte Bestands-Items.
/// Rein und testbar: bestimmt aus der Bestandsliste die Top-N Typen nach
/// Marktwert (0 = Auto-Tracking aus).
/// </summary>
public static class TrackSelection
{
    /// <summary>
    /// Liefert die TypeIds der Top-N Bestands-Items nach aktuellem Marktwert.
    /// </summary>
    /// <param name="items">Bestands-Items (aus IInventoryService).</param>
    /// <param name="limit">Maximale Anzahl; 0 oder negativ = kein Auto-Tracking.</param>
    public static List<int> SelectTopValueItems(IEnumerable<InventoryItem> items, int limit)
    {
        if (limit <= 0) return new List<int>();

        return items
            .OrderByDescending(i => i.CurrentMarketValue)
            .Take(limit)
            .Select(i => i.TypeId)
            .ToList();
    }
}