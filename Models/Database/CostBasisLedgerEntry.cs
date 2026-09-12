namespace WALLEve.Models.Database;

/// <summary>
/// Persistiertes, idempotentes Buchungsledger für die Cost-Basis (Issue #52).
///
/// Erfasst Kauf-/Verkaufs-Ereignisse chronologisch mit stabiler Quell-ID
/// (ESI-TransactionId) pro Owner/Type. Der eindeutige Index auf
/// (CharacterId, TypeId, SourceTransactionId) verhindert jeden Duplicate-Import
/// auf Datenbankebene; ein Replay (siehe CostBasisLedgerService) ist dadurch
/// deterministisch und führt bei doppelten/verspäteten Transaktionen zum selben Zustand.
///
/// Das Ledger ist die Replay-Quelle der Cost-Basis-Zustandsmaschine. FIFO bleibt
/// separat ergänzbar: die chronologische Ereignisfolge bleibt hier vollständig
/// erhalten, ein künftiger FIFO-Replay kann dieselben Ereignisse anders konsumieren.
/// </summary>
public class CostBasisLedgerEntry
{
    public long Id { get; set; }

    public int CharacterId { get; set; }

    public int TypeId { get; set; }

    /// <summary>
    /// Stabile Quell-ID aus ESI (TransactionId). Dedup-Schlüssel pro Owner/Type:
    /// dieselbe Transaktion wird nie zweimal importiert.
    /// </summary>
    public long SourceTransactionId { get; set; }

    /// <summary>Chronologischer Zeitpunkt des Ereignisses (ESI-Transaktionsdatum).</summary>
    public DateTime Date { get; set; }

    public bool IsBuy { get; set; }

    public int Quantity { get; set; }

    /// <summary>
    /// Preis pro Einheit in ISK, wie aus der ESI-Transaktion übernommen.
    /// Erwerbsgebühr ist hier genau EINMAL enthalten; Replay- und Analysepfade
    /// schlagen keine erneute Gebühr darauf auf (keine doppelte Erwerbsgebühr).
    /// </summary>
    public double UnitPrice { get; set; }

    /// <summary>Zeitpunkt des lokalen Imports (Audit).</summary>
    public DateTime ImportedAt { get; set; }
}