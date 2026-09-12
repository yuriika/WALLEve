using WALLEve.Models.Portfolio;

namespace WALLEve.Services.Holdings.Interfaces;

/// <summary>
/// Erzeugt den historischen Portfolio-Punkt zu einem vollständigen
/// Holdings-Snapshot (Issue #51). Die Erfassung ist rein datenbankbasiert
/// und deterministisch — keine Live-ESI-Abhängigkeit.
/// </summary>
public interface IPortfolioSnapshotService
{
    /// <summary>
    /// Erfasst den Portfolio-Snapshot zum Quell-Snapshot. Idempotent: die
    /// erneute Verarbeitung derselben Quell-Snapshot-ID liefert den bereits
    /// vorhandenen Eintrag zurück und dupliziert keine Historie.
    /// </summary>
    /// <param name="holdingSnapshotId">Id des vollständigen Holdings-Snapshots.</param>
    /// <param name="ct">Abbruch-Token.</param>
    /// <exception cref="InvalidOperationException">Wenn kein Quell-Snapshot existiert.</exception>
    Task<PortfolioSnapshot> CaptureAsync(long holdingSnapshotId, CancellationToken ct = default);
}