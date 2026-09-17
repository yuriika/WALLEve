using WALLEve.Models.Holdings;
using WALLEve.Models.Portfolio;

namespace WALLEve.Services.Portfolio.Interfaces;

/// <summary>
/// Auswertung vollständiger Holdings-Snapshots zu Historien-Punkten (Issue #38).
/// Deterministisch und ohne Live-ESI: alle Lookups laufen gebündelt über die
/// lokale Datenbank; Preise werden ausschließlich als Markt-Snapshots des
/// aktivierten Hubs zum Sync-Zeitpunkt (As-of) verwendet.
/// </summary>
public interface IPortfolioHistoryService
{
    /// <summary>
    /// Wertet den vollständigen Holdings-Snapshot aus und liefert den
    /// eingefrorenen Historien-Punkt plus Ort-/Kategorie-Projektionen.
    /// Idempotent: die erneute Verarbeitung derselben Quell-Snapshot-ID
    /// liefert den bereits vorhandenen Punkt zurück und überschreibt nichts
    /// (historische Marktprovenienz bleibt erhalten).
    /// </summary>
    /// <param name="holdingSnapshotId">Id des vollständigen Holdings-Snapshots.</param>
    /// <param name="ct">Abbruch-Token.</param>
    /// <exception cref="InvalidOperationException">
    /// Wenn kein Quell-Snapshot existiert oder der zugehörige Sync-Lauf nicht
    /// vollständig abgeschlossen ist (kein Punkt aus Partial-Sync).
    /// </exception>
    Task<PortfolioHistoryEvaluation> EvaluateAsync(long holdingSnapshotId, CancellationToken ct = default);

    /// <summary>
    /// Liefert die eingefrorenen Historien-Punkte eines Owners in zeitlicher
    /// Ordnung (älteste zuerst). Nur persistierte Punkte — ein späterer
    /// Marktwechsel ändert die Provenienz bestehender Punkte nie (AC #47-1),
    /// fehlende Zeiträume bleiben als Lücken sichtbar. Keine Live-ESI.
    /// </summary>
    Task<IReadOnlyList<PortfolioHistoryPoint>> GetHistoryAsync(OwnerType ownerType, int ownerId, CancellationToken ct = default);

    /// <summary>
    /// Detail eines einzelnen Punkts für den Drill-down: der eingefrorene
    /// Punkt samt persistierter Ort-/Kategorie-Projektionen (Evidenz-Quellen
    /// und Qualitätszustand). Liefert null, wenn der Punkt nicht existiert.
    /// </summary>
    Task<PortfolioHistoryEvaluation?> GetPointAsync(long pointId, CancellationToken ct = default);
}
