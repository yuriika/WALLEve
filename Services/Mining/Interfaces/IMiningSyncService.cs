using WALLEve.Models.Mining;

namespace WALLEve.Services.Mining.Interfaces;

/// <summary>
/// Idempotente Synchronisation des persönlichen Mining-Ledgers (#39).
/// </summary>
public interface IMiningSyncService
{
    /// <summary>
    /// Holt das vollständige persönliche Mining-Ledger eines Charakters und
    /// schreibt es additiv: pro (CharacterId, Date, TypeId, SolarSystemId)
    /// wird die Tagesmenge ersetzt, nie aufaddiert; Zeilen außerhalb des
    /// ESI-Fensters bleiben erhalten. Liefert der M0-Vertrag null
    /// (Fehler/Abbruch), wird nichts geschrieben (Success=false).
    /// </summary>
    Task<MiningSyncResult> SynchronizeAsync(int characterId, CancellationToken ct = default);
}