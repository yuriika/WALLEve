using WALLEve.Models.Holdings;

namespace WALLEve.Models.Portfolio;

/// <summary>
/// Historischer Portfolio-Punkt (Issue #51): unveränderliche Qualitätszähler
/// eines VOLLSTÄNDIGEN Holdings-Snapshots zum Erfassungszeitpunkt.
///
/// Die Zähler werden beim Erfassen als Kopie gespeichert und danach nie
/// aktualisiert — spätere Preise oder nachträgliche Cost-Basis-Buchungen
/// überschreiben die historische Provenienz nicht. Fehlende Bewertungs-/
/// Cost-Basis-Daten sind explizit als Unknown-Zähler abgelegt; die Erfassung
/// wartet nicht auf spätere Kostenbuchung oder Portfolio-UI.
/// </summary>
public class PortfolioSnapshot
{
    public long Id { get; set; }

    /// <summary>Quell-Snapshot (HoldingSnapshot). Eindeutig: die erneute
    /// Verarbeitung derselben Quell-Snapshot-ID dupliziert keine Historie.</summary>
    public long HoldingSnapshotId { get; set; }

    /// <summary>Owner-Dimension aus dem Quell-Snapshot (denormalisiert).</summary>
    public OwnerType OwnerType { get; set; }

    /// <summary>EVE-ID des Owners (CharacterId bzw. CorporationId).</summary>
    public int OwnerId { get; set; }

    /// <summary>Zeitpunkt der Erfassung (historischer Punkt).</summary>
    public DateTime CapturedAt { get; set; }

    /// <summary>Anzahl der Rohzeilen im Quell-Snapshot.</summary>
    public int TotalItems { get; set; }

    /// <summary>Zeilen, deren Typ zum Erfassungszeitpunkt bewertbar war
    /// (mindestens ein Markt-Snapshot vorhanden).</summary>
    public int ValuedItemCount { get; set; }

    /// <summary>Zeilen ohne verfügbare Bewertung — explizit Unknown.</summary>
    public int UnknownValuationItemCount { get; set; }

    /// <summary>Zeilen, deren (Owner, Typ) zum Erfassungszeitpunkt eine
    /// Cost-Basis hatte (CostBasisEntry vorhanden). Nur Character-Owner.</summary>
    public int CostBasisKnownItemCount { get; set; }

    /// <summary>Zeilen ohne bekannte Cost-Basis — explizit Unknown.</summary>
    public int UnknownCostBasisItemCount { get; set; }

    public HoldingSnapshot SourceSnapshot { get; set; } = null!;
}