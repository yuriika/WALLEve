namespace WALLEve.Models.Portfolio;

/// <summary>
/// Persistierte Kategorie-Projektion eines Historien-Punkts (Issue #38): eine
/// Zeile je Kategorie mit getrennten Mengen/Werten für freien Bestand und
/// Escrow. Das Kategorie-Label stammt aus dem SDE (invGroups.groupName); ohne
/// verfügbare Benennung bleibt die Zeile unter "Unbekannt" — es wird keine
/// erfundene Kategorie vergeben.
///
/// Eingefroren beim ersten Auswerten: Die Zeilen werden gemeinsam mit dem
/// Punkt persistiert und bei einer erneuten Auswertung unverändert zurückgegeben
/// — später geänderte SDE-Gruppennamen ändern die Projektionen eines
/// bestehenden Punkts nicht mehr (Review #157, Blocker 1).
/// </summary>
public sealed class PortfolioHistoryCategory
{
    public long Id { get; set; }

    /// <summary>Zugehöriger eingefrorener Historien-Punkt.</summary>
    public long PointId { get; set; }

    public PortfolioHistoryPoint Point { get; set; } = null!;

    /// <summary>Kategorie-Label (SDE-Gruppenname) oder "Unbekannt".</summary>
    public string Category { get; set; } = "Unbekannt";

    /// <summary>Roh-Item-Zeilen im freien Bestand dieser Kategorie.</summary>
    public int ItemCount { get; set; }

    /// <summary>Menge des freien Bestands dieser Kategorie (unabhängig vom Quote-Bestand).</summary>
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