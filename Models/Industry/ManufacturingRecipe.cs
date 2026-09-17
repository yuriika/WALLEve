using System.Collections.ObjectModel;

namespace WALLEve.Models.Industry;

/// <summary>
/// SDE-Manufacturing-Rezept (#56), geladen aus der lokalen SDE-SQLite
/// (Fuzzwork-Dump): Materialliste, Produktmenge, Basiszeit und Produktionslimit.
/// Reine Datenquelle — alle Berechnungen laufen über <c>ManufacturingMath</c>.
/// </summary>
public sealed record ManufacturingRecipe
{
    /// <summary>Blueprint-TypeId (SDE industryBlueprints.typeID).</summary>
    public required int BlueprintTypeId { get; init; }

    /// <summary>Produkt-TypeId je Run (industryActivityProducts.productTypeID).</summary>
    public required int ProductTypeId { get; init; }

    /// <summary>Outputmenge je Run (industryActivityProducts.quantity).</summary>
    public required long ProductQuantity { get; init; }

    /// <summary>Basis-Produktionszeit je Run in Sekunden (industryActivity.time, activity 1).</summary>
    public required int BaseTimeSeconds { get; init; }

    /// <summary>
    /// Max. Runs je Job laut SDE (industryBlueprints.maxProductionLimit).
    /// Null = kein SDE-Limit (unbegrenzter Job). Gilt für BPO und BPC gleichermaßen.
    /// </summary>
    public int? MaxProductionLimit { get; init; }

    /// <summary>Basis-Materialien je Run, unverändert aus der SDE.</summary>
    public IReadOnlyList<RecipeMaterial> Materials { get; init; } = Array.Empty<RecipeMaterial>();
}