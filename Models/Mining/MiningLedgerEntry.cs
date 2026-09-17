namespace WALLEve.Models.Mining;

/// <summary>
/// Persistente Zeile des persönlichen Mining-Ledgers (#39).
/// Additiv: pro (CharacterId, Date, TypeId, SolarSystemId) existiert genau
/// eine Zeile; eine korrigierte Tagesmenge ersetzt den alten Wert (kein
/// Aufaddieren). Zeilen außerhalb des ESI-Antwortfensters bleiben erhalten.
/// </summary>
public class MiningLedgerEntry
{
    public long Id { get; set; }

    public int CharacterId { get; set; }

    /// <summary>Nur Datum (00:00 Uhr), Tages-Schlüssel des ESI-Ledgers.</summary>
    public DateTime Date { get; set; }

    public int TypeId { get; set; }

    public int SolarSystemId { get; set; }

    /// <summary>Kumulierte Tagesmenge dieser Kombination (ESI-quote).</summary>
    public long Quantity { get; set; }

    public DateTime UpdatedAt { get; set; }
}