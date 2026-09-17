namespace WALLEve.Models.Industry;

/// <summary>
/// Ergebnis einer Materialbedarfs-Berechnung (#56).
/// Ein Fehlschlag (unbekanntes Rezept, fehlende Parameter, unzulässige Runs)
/// liefert absichtlich KEINE Mengen und keine Produktionszusage — es darf nie
/// ein Null-Materialbedarf als scheinbar valides Ergebnis entstehen.
/// </summary>
public sealed class ManufacturingRequirementResult
{
    private ManufacturingRequirementResult(
        bool isSuccess,
        IReadOnlyList<MaterialRequirement>? materials,
        long totalTimeSeconds,
        long productQuantity,
        string? failureReason)
    {
        IsSuccess = isSuccess;
        Materials = materials;
        TotalTimeSeconds = totalTimeSeconds;
        ProductQuantity = productQuantity;
        FailureReason = failureReason;
    }

    public bool IsSuccess { get; }

    /// <summary>Materialbedarf je Material für den gesamten Job (ME-Waste enthalten). Nur bei Erfolg.</summary>
    public IReadOnlyList<MaterialRequirement>? Materials { get; }

    /// <summary>Gesamtdauer des Jobs in Sekunden (TE berücksichtigt). Nur bei Erfolg.</summary>
    public long TotalTimeSeconds { get; }

    /// <summary>Gesamt-Output des Jobs (Produktmenge je Run × Runs). Nur bei Erfolg.</summary>
    public long ProductQuantity { get; }

    /// <summary>Nutzerlesbare Fehlerursache (deutsch). Nur bei Misserfolg.</summary>
    public string? FailureReason { get; }

    public static ManufacturingRequirementResult Success(
        IReadOnlyList<MaterialRequirement> materials,
        long totalTimeSeconds,
        long productQuantity) =>
        new(true, materials, totalTimeSeconds, productQuantity, null);

    public static ManufacturingRequirementResult UnknownRecipe(int blueprintTypeId) =>
        new(false, null, 0, 0, $"Unbekanntes Rezept für Blueprint {blueprintTypeId}: kein Materialbedarf ableitbar.");

    public static ManufacturingRequirementResult MissingParameters(string detail) =>
        new(false, null, 0, 0, detail);

    public static ManufacturingRequirementResult InvalidRuns(string detail) =>
        new(false, null, 0, 0, detail);
}