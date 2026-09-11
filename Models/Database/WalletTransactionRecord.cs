namespace WALLEve.Models.Database;

/// <summary>
/// Lokale Kopie einer ESI-Wallet-Transaktion.
/// Die ESI-Schnittstelle liefert Transaktionen nur ca. 30 Tage zurück —
/// durch tägliches lokales Spiegeln bleibt die Historie dauerhaft verfügbar
/// und wird für die Cost-Basis-Ermittlung genutzt.
/// </summary>
public class WalletTransactionRecord
{
    public long Id { get; set; }
    public int CharacterId { get; set; }

    /// <summary>Eindeutige Transaktions-ID aus ESI (Dedup-Kriterium).</summary>
    public long TransactionId { get; set; }

    public int TypeId { get; set; }
    public DateTime Date { get; set; }
    public bool IsBuy { get; set; }
    public bool IsPersonal { get; set; }
    public long JournalRefId { get; set; }
    public long LocationId { get; set; }
    public int Quantity { get; set; }
    public double UnitPrice { get; set; }
}