namespace WALLEve.Models.Portfolio;

/// <summary>
/// Kategorie-Projektion eines Historien-Punkts (Issue #38): eine Zeile je
/// Kategorie mit getrennten Mengen/Werten für freien Bestand und Escrow.
/// Das Kategorie-Label stammt aus dem SDE (invGroups.groupName); ohne
/// verfügbare Benennung bleibt die Zeile unter "Unbekannt" — es wird keine
/// erfundene Kategorie vergeben.
/// </summary>
public sealed class PortfolioCategoryValue
{
    /// <summary>Kategorie-Label (SDE-Gruppenname) oder "Unbekannt".</summary>
    public string Category { get; set; } = "Unbekannt";

    /// <summary>Roh-Item-Zeilen im freien Bestand dieser Kategorie.</summary>
    public int ItemCount { get; set; }

    /// <summary>Menge des freien Bestands dieser Kategorie.</summary>
    public long Quantity { get; set; }

    /// <summary>Marktwert des freien Bestands; null ohne As-of-Quote.</summary>
    public double? Value { get; set; }

    /// <summary>Roh-Item-Zeilen dieser Kategorie, die in Sell-Orders gebunden sind.</summary>
    public int EscrowItemCount { get; set; }

    /// <summary>Gebundene Menge dieser Kategorie (separater Bucket).</summary>
    public long EscrowQuantity { get; set; }

    /// <summary>Marktwert der gebundenen Menge; null ohne As-of-Quote.</summary>
    public double? EscrowValue { get; set; }
}