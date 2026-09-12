namespace WALLEve.Models.Database;

/// <summary>Herkunft einer Cost-Basis.</summary>
public enum CostBasisSource
{
    /// <summary>Noch nichts ermittelt (offen).</summary>
    None = 0,
    /// <summary>Echter Preis aus einer Wallet-Transaktion (automatisch, final).</summary>
    Transaction = 1,
    /// <summary>Schätzung aus Marktdaten (Vorschlag, gilt als offen bis bestätigt/überschrieben).</summary>
    Estimate = 2,
    /// <summary>Manuell gesetzt (final, gewinnt immer).</summary>
    Manual = 3
}

/// <summary>
/// Cost Basis (Einkaufspreis) pro Item und Charakter.
/// Ein Eintrag pro (CharacterId, TypeId) — die Quelle bestimmt die Vertrauensstufe.
/// </summary>
public class CostBasisEntry
{
    public long Id { get; set; }
    public int CharacterId { get; set; }
    public int TypeId { get; set; }

    /// <summary>
    /// Erwerbskosten pro Einheit in ISK (vollständige Anschaffungskosten).
    /// Invariante: Verknüpfte Erwerbskosten sind hier genau EINMAL enthalten.
    /// Analysepfade (Marktanalyse, Order-Intelligence, Portfolio) verwenden diesen
    /// Wert direkt und schlagen KEINE erneute Buy-Brokergebühr darauf auf.
    /// </summary>
    public double? Value { get; set; }

    public CostBasisSource Source { get; set; }

    /// <summary>Ca. Kaufdatum, falls bekannt (z.B. manuell angegeben oder aus Transaktion).</summary>
    public DateTime? PurchaseDate { get; set; }

    /// <summary>Schätzregion (nur bei Source=Estimate).</summary>
    public int? EstimateRegionId { get; set; }

    public DateTime UpdatedAt { get; set; }
}