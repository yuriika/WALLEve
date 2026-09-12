namespace WALLEve.Models.Holdings;

/// <summary>
/// Rohzeile eines Asset-Items innerhalb eines Snapshots. Speichert die
/// unveränderten ESI-Dimensionen (ItemId/TypeId/Menge/Singleton/Location/
/// Flag/Parent), damit spätere Auswertungen ohne erfundene Zuordnungen
/// auskommen. Es gibt KEINE Eindeutigkeit über ItemId allein: dieselbe
/// ItemId kann in verschiedenen Snapshot-/Owner-Kontexten existieren.
/// </summary>
public class HoldingItem
{
    public long Id { get; set; }

    /// <summary>Zugehöriger Bestands-Snapshot.</summary>
    public long SnapshotId { get; set; }

    /// <summary>Roh-ItemId aus ESI (item_id).</summary>
    public long ItemId { get; set; }

    /// <summary>EVE-TypeId (type_id).</summary>
    public int TypeId { get; set; }

    /// <summary>Menge (quantity).</summary>
    public int Quantity { get; set; }

    /// <summary>Singleton-Flag (is_singleton).</summary>
    public bool IsSingleton { get; set; }

    /// <summary>LocationId (location_id: Station, Struktur oder Container-Item).</summary>
    public long LocationId { get; set; }

    /// <summary>LocationFlag als Rohstring (location_flag), z. B. "Hangar".</summary>
    public string LocationFlag { get; set; } = string.Empty;

    /// <summary>Parent/Container-ItemId (location_type "item"), falls vorhanden.</summary>
    public long? ParentItemId { get; set; }

    public HoldingSnapshot Snapshot { get; set; } = null!;
}