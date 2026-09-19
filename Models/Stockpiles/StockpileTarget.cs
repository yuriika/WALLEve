using System.ComponentModel.DataAnnotations;
using WALLEve.Models.Holdings;

namespace WALLEve.Models.Stockpiles;

/// <summary>
/// Persistiertes Stockpile-Ziel (Issue #36): der gewünschte Bestand eines
/// EVE-Type für einen Owner, optional auf einen Ort/Container begrenzt,
/// mit Notiz und Archivstatus. Bestandsberechnung ist bewusst NICHT Teil
/// dieser Issue — hier wird nur gespeichert und validiert.
/// </summary>
public class StockpileTarget
{
    public long Id { get; set; }

    /// <summary>Besitzer-Dimension (Character oder Corporation).</summary>
    public OwnerType OwnerType { get; set; }

    /// <summary>Owner-id (CharacterId bzw. CorporationId).</summary>
    public int OwnerId { get; set; }

    /// <summary>EVE-Type des Ziel-Items.</summary>
    public int TypeId { get; set; }

    /// <summary>
    /// Zielbestand in Einheiten. Muss größer als 0 sein — negative Ziele
    /// werden vom Service abgelehnt (Akzeptanzkriterium #36).
    /// 64-Bit, damit Summen oberhalb int.MaxValue korrekt bleiben (#183).
    /// </summary>
    public long Quantity { get; set; }

    /// <summary>
    /// Optionaler Ort-/Container-Scope (Station, Struktur oder Container-ItemId).
    /// null = Ziel gilt für den gesamten Bestand des Owners.
    /// </summary>
    public long? LocationId { get; set; }

    /// <summary>Freitext-Notiz zum Ziel.</summary>
    [MaxLength(500)]
    public string? Note { get; set; }

    /// <summary>
    /// Archivstatus: archivierte Ziele bleiben nachvollziehbar in der Datenbank,
    /// werden aber von der Standard-Ansicht ausgeblendet.
    /// </summary>
    public bool IsArchived { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}