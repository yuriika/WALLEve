using WALLEve.Models.Esi.Character;
using WALLEve.Models.Holdings;

namespace WALLEve.Services.Holdings.Interfaces;

/// <summary>
/// Synchronisiert Character-Assets über den M0-ESI-Vertrag in vollständige
/// Bestands-Snapshots. Publiziert wird erst bei Gesamterfolg: Ein Fehler,
/// Abbruch oder eine Exception hinterlässt keinen neuen Snapshot — der zuletzt
/// vollständig publizierte Bestand bleibt unverändert erhalten.
/// </summary>
public interface IHoldingsSyncService
{
    /// <summary>
    /// Führt einen atomaren Sync-Lauf für einen Character aus.
    /// Protokolliert den Lauf als HoldingSyncRun; nur ein gültiges Gesamtergebnis
    /// (auch gültig leer) erzeugt einen neuen HoldingSnapshot.
    /// Wirft OperationCanceledException bei Abbruch, sonstige ESI-Fehler bei Misserfolg.
    /// </summary>
    Task<HoldingSyncRun> SynchronizeAsync(int characterId, CancellationToken ct = default);

    /// <summary>
    /// Der publizierte Bestand eines Characters: der Snapshot des zuletzt
    /// vollständig abgeschlossenen Sync-Laufs (inklusive Items).
    /// Liefert null, wenn noch kein Lauf erfolgreich publiziert wurde.
    /// </summary>
    Task<HoldingSnapshot?> GetLatestSnapshotAsync(int characterId, CancellationToken ct = default);
}