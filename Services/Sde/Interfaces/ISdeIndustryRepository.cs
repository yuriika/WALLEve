using WALLEve.Models.Industry;

namespace WALLEve.Services.Sde.Interfaces;

/// <summary>
/// Lädt Fertigungs-Rezepte aus der lokalen SDE (#56).
/// Null = unbekannter Blueprint (kein Rezept vorhanden).
/// </summary>
public interface ISdeIndustryRepository
{
    Task<ManufacturingRecipe?> GetManufacturingRecipeAsync(int blueprintTypeId, CancellationToken cancellationToken = default);
}