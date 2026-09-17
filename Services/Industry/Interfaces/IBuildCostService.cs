using WALLEve.Models.Industry;

namespace WALLEve.Services.Industry.Interfaces;

/// <summary>
/// Berechnet Baukosten und Build-vs-Buy (#65): Rezept aus der SDE, Marktpreise
/// aus den Snapshots des Vergleichsmarkts (nie Live-ESI), deterministische
/// Formeln aus <see cref="BuildCostMath"/>. Alle Annahmen kommen aus
/// <see cref="BuildCostAssumptions"/> und bleiben in der UI aufklappbar.
/// </summary>
public interface IBuildCostService
{
    /// <summary>
    /// Erstellt die Kostenschätzung für einen Blueprint-Job.
    /// Kein Rezept → Fehlschlag mit Grund; unbekannte Preise/Annahmen bleiben
    /// in der Schätzung sichtbar null mit Grund (nie 0 ISK, nie Fantasiewert).
    /// </summary>
    Task<BuildCostResult> CalculateAsync(
        int blueprintTypeId,
        BuildCostAssumptions assumptions,
        CancellationToken cancellationToken = default);
}