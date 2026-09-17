namespace WALLEve.Models.Portfolio;

/// <summary>
/// Persistierte Orts-Projektion eines Historien-Punkts (Issue #38): eine Zeile
/// je (LocationId, LocationFlag) mit getrennten Mengen/Werten für den freien
/// Bestand (Assets) und die in Sell-Orders gebundenen Items (Escrow).
///
/// Eingefroren beim ersten Auswerten: Die Zeilen werden gemeinsam mit dem
/// Punkt persistiert und bei einer erneuten Auswertung unverändert zurückgegeben
/// — später hinzugefügte As-of-Quote oder Kategorie-Auflösungen ändern die
/// Projektionen eines bestehenden Punkts nicht mehr (Review #157, Blocker 1).
/// </summary>
public sealed class PortfolioHistoryLocation
{
    public long Id { get; set; }

    /// <summary>Zugehöriger eingefrorener Historien-Punkt.</summary>
    public long PointId { get; set; }

    public PortfolioHistoryPoint Point { get; set; } = null!;

    /// <summary>LocationId der Rohzeilen (Station, Struktur oder Container-Item).</summary>
    public long LocationId { get; set; }

    /// <summary>LocationFlag der Rohzeilen, z. B. "Hangar" oder "CorpSellOrder".</summary>
    public string LocationFlag { get; set; } = string.Empty;

    /// <summary>Roh-Item-Zeilen im freien Bestand an diesem Ort.</summary>
    public int ItemCount { get; set; }

    /// <summary>Menge des freien Bestands an diesem Ort (unabhängig vom Quote-Bestand).</summary>
    public long Quantity { get; set; }

    /// <summary>Marktwert des freien Bestands an diesem Ort; null ohne As-of-Quote.</summary>
    public double? Value { get; set; }

    /// <summary>Roh-Item-Zeilen, die an diesem Ort in Sell-Orders gebunden sind.</summary>
    public int EscrowItemCount { get; set; }

    /// <summary>Gebundene Menge an diesem Ort (separater Bucket, nie Doppelzählung).</summary>
    public long EscrowQuantity { get; set; }

    /// <summary>Marktwert der gebundenen Menge an diesem Ort; null ohne As-of-Quote.</summary>
    public double? EscrowValue { get; set; }
}