using WALLEve.Models.Holdings;

namespace WALLEve.Services.Holdings.Interfaces;

/// <summary>
/// Löst die Locations eines Holding-Snapshots auf: NPC-Stationen per SDE,
/// zugängliche Strukturen per ESI, Sonnensysteme per SDE; Container über die
/// reine Parent-Ketten-Auflösung. Fehler (403, fehlende Parents, Zyklen,
/// unbekannte IDs) erzeugen Unresolved mit Grund statt Absturz/falschem System.
/// ESI-Strukturabfragen laufen über den Auth-Kontext des aktuell angemeldeten
/// Charakters (Single-Active-Character-App); ein Snapshot-Owner wird bewusst
/// nicht als Parameter behauptet.
/// </summary>
public interface IHoldingsLocationResolver
{
    /// <summary>Löst alle Items eines Snapshots deterministisch auf. ESI-Strukturzugriffe nutzen den aktuellen Auth-Kontext (kein Owner-Parameter).</summary>
    Task<IReadOnlyList<ResolvedHoldingItem>> ResolveSnapshotAsync(
        IEnumerable<HoldingItem> items,
        CancellationToken ct = default);
}