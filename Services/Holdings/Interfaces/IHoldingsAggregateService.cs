using WALLEve.Models.Holdings;

namespace WALLEve.Services.Holdings.Interfaces;

/// <summary>
/// Aggregierte Holdings-Sicht (#57): Type- und Type/Location-Projektionen mit
/// Owner, Qualität und Freshness sowie der Ortsbaum mit Drill-down auf die
/// Rohdimensionen. Owner werden nie vermischt; unbekannte Orte sind separat
/// sichtbar. Liefert null, wenn für den Owner noch kein abgeschlossener
/// Snapshot existiert.
/// </summary>
public interface IHoldingsAggregateService
{
    Task<HoldingsTreeResult?> BuildTreeAsync(OwnerType ownerType, int ownerId, CancellationToken ct = default);
}