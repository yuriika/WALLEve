using WALLEve.Models.Industry;

namespace WALLEve.Services.Industry.Interfaces;

/// <summary>
/// Berechnet den Materialbedarf und die Jobdauer für ein Fertigungs-Rezept (#56).
/// Liefert bei unbekanntem Rezept oder fehlenden/unzulässigen Parametern einen
/// Fehler statt eines Nullbedarfs — nie eine Produktionszusage ohne Basis.
/// </summary>
public interface IManufacturingRequirementService
{
    /// <param name="blueprintTypeId">Blueprint-TypeId (SDE).</param>
    /// <param name="materialEfficiency">ME 0..10.</param>
    /// <param name="timeEfficiency">TE 0..20.</param>
    /// <param name="runs">Runs des Jobs.</param>
    /// <param name="isCopy">true = BPC, false = BPO.</param>
    /// <param name="remainingRuns">Verbleibende Runs einer BPC (bei BPO null).</param>
    Task<ManufacturingRequirementResult> CalculateAsync(
        int blueprintTypeId,
        int materialEfficiency,
        int timeEfficiency,
        int runs,
        bool isCopy,
        int? remainingRuns,
        CancellationToken cancellationToken = default);
}