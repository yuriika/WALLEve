using WALLEve.Models.Industry;

namespace WALLEve.Services.Industry.Interfaces;

/// <summary>
/// Verknüpft das Blueprint-Register mit den Holdings-Assets derselben belegten
/// Identität (#49). Keine TypeId-only-Zuordnung auf einzelne Exemplare.
/// </summary>
public interface IBlueprintHoldingsLinkService
{
    /// <summary>
    /// Verknüpft jeden Blueprint eines Characters mit dem neuesten Holdings-Snapshot
    /// desselben Owners ausschließlich über die ItemId. Ein Blueprint ohne passendes
    /// Asset (fehlendes Asset, kein Snapshot, partieller Sync) wird als
    /// <see cref="BlueprintLinkState.Unknown"/> ausgewiesen — es wird nie eine
    /// Zuordnung über den TypeId erfunden.
    /// </summary>
    Task<List<BlueprintHoldingsLink>> LinkToHoldingsAsync(int characterId, CancellationToken ct = default);
}