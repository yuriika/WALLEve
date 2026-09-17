using WALLEve.Models.Industry;

namespace WALLEve.Services.Industry.Interfaces;

/// <summary>
/// Idempotente Synchronisation der Character-Blueprints (#49).
/// </summary>
public interface IBlueprintsSyncService
{
    /// <summary>
    /// Holt alle Blueprints eines Characters inkl. Paginierung und schreibt sie
    /// additiv: pro (CharacterId, ItemId) werden die mutablen Felder (Ort, Menge,
    /// ME/TE, Runs, Kopierstatus) ersetzt, nie dupliziert. BPO-Sentinel (Runs=-1)
    /// und BPC-Runs werden als Rohwerte erhalten. Liefert ESI null (Fehler/
    /// Abbruch, M0-Vertrag: keine Teildaten), wird nichts geschrieben (Success=false).
    /// </summary>
    Task<BlueprintsSyncResult> SynchronizeAsync(int characterId, CancellationToken ct = default);
}