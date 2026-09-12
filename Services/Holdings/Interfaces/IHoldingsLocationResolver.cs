using WALLEve.Models.Holdings;

namespace WALLEve.Services.Holdings.Interfaces;

/// <summary>
/// Löst die Locations eines Holding-Snapshots auf: NPC-Stationen per SDE,
/// zugängliche Strukturen per ESI, Sonnensysteme per SDE; Container über die
/// reine Parent-Ketten-Auflösung. Fehler (403, fehlende Parents, Zyklen,
/// unbekannte IDs) erzeugen Unresolved mit Grund statt Absturz/falschem System.
/// </summary>
public interface IHoldingsLocationResolver
{
    /// <summary>Löst alle Items eines Snapshots deterministisch auf. CharacterId für strukturabhängige ESI-Zugriffe.</summary>
    Task<IReadOnlyList<ResolvedHoldingItem>> ResolveSnapshotAsync(
        IEnumerable<HoldingItem> items,
        int characterId,
        CancellationToken ct = default);
}