using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Industry;

namespace WALLEve.Services.Industry.Interfaces;

/// <summary>
/// Gleicht geplante Materialbedarfe (#56) gegen die Holdings des Characters
/// ab (Issue #62). Physische Basis ist der jüngste abgeschlossene
/// Holding-Snapshot des Owners; jede Asset-Zeile zählt genau einmal —
/// gleiches Material in mehreren Orten/Plänen wird nie mehrfach verfügbar
/// behauptet. Orders werden vom Aufrufer bereitgestellt (keine Live-ESI-
/// Abhängigkeit in deterministischen Tests); fehlende Quellen markieren
/// partial statt falscher Nullstände.
/// </summary>
public interface IMaterialDemandService
{
    /// <summary>
    /// Berechnet für alle Bedarfs-Pläne eines Characters die Material-Aggregate
    /// mit dedupliziertem physischen Pool, getrennten Buy-/Sell-Mengen und
    /// einmaliger Fehlmenge je Material.
    /// </summary>
    /// <param name="characterId">Angemeldeter Character (Owner-Isolation).</param>
    /// <param name="requests">Bedarfe je Plan.</param>
    /// <param name="orders">
    /// Aktive persönliche Market-Orders (GET /characters/{id}/orders/).
    /// null = Quelle nicht verfügbar; die Zeilen werden dann partial markiert
    /// und Inbound/Bound bleiben null.
    /// </param>
    /// <param name="ordersAvailable">
    /// true, wenn <paramref name="orders"/> den vollständigen, aktuellen
    /// Order-Bestand repräsentiert; false markiert die Zeilen als partial.
    /// </param>
    Task<IReadOnlyList<MaterialDemandMatch>> MatchDemandAsync(
        int characterId,
        IReadOnlyList<MaterialDemandRequest> requests,
        IReadOnlyList<MarketOrder>? orders = null,
        bool ordersAvailable = false,
        CancellationToken ct = default);
}