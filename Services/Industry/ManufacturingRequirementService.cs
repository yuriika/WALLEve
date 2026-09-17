using WALLEve.Models.Industry;
using WALLEve.Services.Industry.Interfaces;
using WALLEve.Services.Sde.Interfaces;

namespace WALLEve.Services.Industry;

/// <summary>
/// Orchestriert die Materialbedarfs-Berechnung (#56): Rezept aus der SDE laden,
/// Parameter validieren, ME/TE-Rundungsregeln anwenden und das Ergebnis oder
/// einen expliziten Fehler liefern. Eigene BPO/BPC-Runs-Logik bleibt hier:
/// eine BPC braucht zwingend ihre verbleibenden Runs, ein BPO nutzt das
/// SDE-Produktionslimit als Obergrenze je Job.
/// </summary>
public class ManufacturingRequirementService : IManufacturingRequirementService
{
    private readonly ISdeIndustryRepository _sdeIndustryRepository;

    public ManufacturingRequirementService(ISdeIndustryRepository sdeIndustryRepository)
    {
        _sdeIndustryRepository = sdeIndustryRepository;
    }

    public async Task<ManufacturingRequirementResult> CalculateAsync(
        int blueprintTypeId,
        int materialEfficiency,
        int timeEfficiency,
        int runs,
        bool isCopy,
        int? remainingRuns,
        CancellationToken cancellationToken = default)
    {
        var recipe = await _sdeIndustryRepository.GetManufacturingRecipeAsync(blueprintTypeId, cancellationToken);
        if (recipe is null)
        {
            return ManufacturingRequirementResult.UnknownRecipe(blueprintTypeId);
        }

        // Parameter-Validierung: nie still korrigieren, nie einen Nullbedarf erzeugen.
        if (!IsWithin(materialEfficiency, ManufacturingMath.MinMaterialEfficiency, ManufacturingMath.MaxMaterialEfficiency))
        {
            return ManufacturingRequirementResult.InvalidRuns(
                $"Materialeffizienz {materialEfficiency} liegt außerhalb des Bereichs " +
                $"{ManufacturingMath.MinMaterialEfficiency}..{ManufacturingMath.MaxMaterialEfficiency}.");
        }

        if (timeEfficiency is < 0 or > ManufacturingMath.MaxTimeEfficiency)
        {
            return ManufacturingRequirementResult.InvalidRuns(
                $"Zeiteffizienz {timeEfficiency} liegt außerhalb des Bereichs 0..{ManufacturingMath.MaxTimeEfficiency}.");
        }

        if (runs < 1)
        {
            return ManufacturingRequirementResult.InvalidRuns("Die Anzahl der Runs muss ≥ 1 sein.");
        }

        if (recipe.Materials.Count == 0)
        {
            return ManufacturingRequirementResult.MissingParameters(
                $"Rezept {blueprintTypeId} enthält keine Materialien: kein Materialbedarf ableitbar.");
        }

        if (recipe.ProductQuantity < 1)
        {
            return ManufacturingRequirementResult.MissingParameters(
                $"Rezept {blueprintTypeId} hat keine Produktmenge: keine Produktionszusage möglich.");
        }

        int? maxRuns;
        try
        {
            maxRuns = ManufacturingMath.MaxAllowedRuns(recipe.MaxProductionLimit, isCopy, remainingRuns);
        }
        catch (ArgumentException)
        {
            return ManufacturingRequirementResult.MissingParameters(
                "Verbleibende Runs einer Blueprint-Kopie fehlen: Runs-Limit nicht prüfbar.");
        }

        if (maxRuns is not null && runs > maxRuns.Value)
        {
            return ManufacturingRequirementResult.InvalidRuns(
                $"Angeforderte Runs ({runs}) überschreiten das Limit des Blueprints ({maxRuns.Value}).");
        }

        var materials = recipe.Materials
            .Select(m => new MaterialRequirement(
                m.MaterialTypeId,
                ManufacturingMath.MaterialRequirementForJob(m.BaseQuantity, materialEfficiency, runs)))
            .OrderByDescending(m => m.RequiredQuantity)
            .ThenBy(m => m.MaterialTypeId)
            .ToList();

        var totalSeconds = ManufacturingMath.JobTimeSeconds(recipe.BaseTimeSeconds, timeEfficiency, runs);
        var productQuantity = recipe.ProductQuantity * runs;

        return ManufacturingRequirementResult.Success(materials, totalSeconds, productQuantity);
    }

    private static bool IsWithin(int value, int min, int max) => value >= min && value <= max;
}