using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Market.Interfaces;

/// <summary>
/// Löst einen Hintergrund-Sync manuell aus („Jetzt ausführen") und zeigt an, ob
/// ein erzwungener Lauf ansteht, den der Collector beim nächsten Loop übernimmt.
/// </summary>
public interface ISyncTriggerService
{
    /// <summary>
    /// Fordert einen sofortigen Lauf des Syncs an. Bei den automatischen Syncs
    /// (Sink/Deduction) wird ein Force-Flag gesetzt, das der Collector im nächsten
    /// Loop konsumiert. InventoryScan startet direkt einen Job, Estimate braucht eine
    /// Item-Auswahl und ist daher nicht sofort auslösbar.
    /// </summary>
    Task<bool> TriggerNowAsync(int characterId, string jobType);

    /// <summary>Fordert alle planmäßigen Character-Syncs unmittelbar an.</summary>
    Task<int> TriggerScheduledNowAsync(int characterId);

    /// <summary>Nimmt ein ausstehendes Force-Flag entgegen (und löscht es).</summary>
    Task<bool> ConsumeForceAsync(int characterId, string jobType);
}