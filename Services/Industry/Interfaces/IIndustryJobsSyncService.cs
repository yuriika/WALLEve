using WALLEve.Models.Industry;

namespace WALLEve.Services.Industry.Interfaces;

/// <summary>
/// Idempotente Synchronisation der Character-Industriejobs (#40).
/// </summary>
public interface IIndustryJobsSyncService
{
    /// <summary>
    /// Holt alle aktiven und historischen Industriejobs eines Characters (inkl.
    /// Paginierung) und schreibt sie additiv: pro (CharacterId, JobId) werden die
    /// mutablen Statusfelder ersetzt, nie dupliziert; Jobs außerhalb des
    /// ESI-Fensters (~90 Tage) bleiben erhalten. Liefert der M0-Vertrag null
    /// (Fehler/Abbruch), wird nichts geschrieben (Success=false).
    /// </summary>
    Task<IndustryJobsSyncResult> SynchronizeAsync(int characterId, CancellationToken ct = default);
}