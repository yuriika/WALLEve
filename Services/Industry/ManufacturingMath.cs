namespace WALLEve.Services.Industry;

/// <summary>
/// Reine, deterministische Formeln für den Materialbedarf aus Rezept, Runs und
/// ME/TE (#56). Keine I/O, keine ESI-Abhängigkeit, keine stillen Korrekturen —
/// ungültige Eingaben werfen <see cref="ArgumentOutOfRangeException"/>, damit der
/// Aufrufer sie als Fehler behandeln kann (nie Nullkosten, nie Produktionszusage).
///
/// Fixierte Rundungsregel (Quellen, siehe auch Tests in ManufacturingMathTests):
/// - Material-Waste je ME: 10 % Basis bei ME 0, reduziert um den Faktor
///   <c>1 / (1 + ME)</c>: <c>wasteFactor = 0.1 / (1 + ME)</c> mit ME als Ganzzahl
///   0..10 (EVE-Research-Korridor; ESI liefert ME ab 0, vgl. BlueprintEntry).
/// - Die Rundung erfolgt pro JOB, nicht pro Run ("The rounding is done per job
///   instead of per run"), und jeder Fertigungs-Job verlangt mindestens eine
///   Einheit je Material und Run. Quelle: EVE University Wiki, "Manufacturing"
///   (Abschnitt "Beware of rounding errors").
///   Daraus folgt konservativ (nach oben gerundet — eine Unterschätzung des
///   Bedarfs ist ausgeschlossen):
///   <c>Bedarf = max(runs, ceil(base × runs × (1 + 0.1 / (1 + ME))))</c>
/// - Zeit: <c>Jobzeit = ceil(baseSekunden × (1 - TE / 100)) × runs</c>, TE 0..20
///   (EVE-Research-Korridor; 1 % Zeitersparnis je TE-Punkt).
/// - Die SDE-Werte (Fuzzwork-SQLite) sind die Fixierung der Basisdaten; EVE Ref
///   dient nur als nachvollziehbare Referenzfixture in Tests, nie als
///   Runtime-Abhängigkeit.
/// </summary>
public static class ManufacturingMath
{
    /// <summary>Basis-Waste von 10 % bei ME 0.</summary>
    public const decimal BaseWasteFactor = 0.1m;

    /// <summary>Untergrenze ME: 0 (BPO/BPC-Forschungsstand; negative ME derzeit nicht unterstützt).</summary>
    public const int MinMaterialEfficiency = 0;

    /// <summary>Obergrenze ME: 10 (max. Materialeffizienz-Forschung).</summary>
    public const int MaxMaterialEfficiency = 10;

    /// <summary>Obergrenze TE: 20 (max. Zeiteffizienz-Forschung).</summary>
    public const int MaxTimeEfficiency = 20;

    /// <summary>
    /// Materialbedarf eines Materials für einen kompletten Job mit ME-Anpassung.
    /// </summary>
    /// <param name="baseQuantity">Rohmenge je Run aus der SDE (≥ 1).</param>
    /// <param name="materialEfficiency">ME 0..10.</param>
    /// <param name="runs">Runs des Jobs (≥ 1).</param>
    /// <returns>Gesamtmenge für den Job (ME-Waste inklusive, pro Job gerundet).</returns>
    /// <exception cref="ArgumentOutOfRangeException">Bei ungültigem baseQuantity, ME oder runs.</exception>
    public static long MaterialRequirementForJob(int baseQuantity, int materialEfficiency, int runs)
    {
        ValidateBaseQuantity(baseQuantity);
        ValidateMaterialEfficiency(materialEfficiency);
        ValidateRuns(runs);

        // Bedarf = max(runs, ceil(base × runs × (1 + 0.1 / (1 + ME))))
        // Exakt als ganzzahlige Rational-Rechnung formuliert (identisch zu
        // ceil(base×runs + base×runs/(10×(1+ME))), da base×runs ganzzahlig ist):
        // Waste = ceil(base×runs / (10 × (1 + ME))) — keine Float-/Decimal-Rundungsfehler.
        var baseTotal = (long)baseQuantity * runs;
        var wasteDenominator = 10L * (1L + materialEfficiency);
        var wasteCeiled = (baseTotal + wasteDenominator - 1) / wasteDenominator;
        var total = baseTotal + wasteCeiled;
        return Math.Max(runs, total);
    }

    /// <summary>
    /// Gesamtdauer eines Jobs in Sekunden mit TE-Anpassung.
    /// </summary>
    /// <param name="baseTimeSeconds">Basiszeit je Run in Sekunden aus der SDE (≥ 1).</param>
    /// <param name="timeEfficiency">TE 0..20 (1 % Zeitersparnis je Punkt).</param>
    /// <param name="runs">Runs des Jobs (≥ 1).</param>
    /// <returns>Jobdauer in Sekunden.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Bei ungültiger baseTimeSeconds, TE oder runs.</exception>
    public static long JobTimeSeconds(int baseTimeSeconds, int timeEfficiency, int runs)
    {
        if (baseTimeSeconds < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(baseTimeSeconds), "Basiszeit muss ≥ 1 sein.");
        }

        ValidateTimeEfficiency(timeEfficiency);
        ValidateRuns(runs);

        var perRun = (long)Math.Ceiling((decimal)baseTimeSeconds * (1m - timeEfficiency / 100m));
        return perRun * runs;
    }

    /// <summary>
    /// Maximal zulässige Runs für einen Job.
    /// BPO: SDE-Produktionslimit (null = unbegrenzt). BPC: Minimum aus
    /// SDE-Produktionslimit und verbleibenden Kopie-Runs.
    /// </summary>
    /// <param name="maxProductionLimit">SDE industryBlueprints.maxProductionLimit (null = kein Limit).</param>
    /// <param name="isCopy">true = BPC (Kopie), false = BPO (Original).</param>
    /// <param name="remainingRuns">Verbleibende Runs der Kopie; bei BPC erforderlich.</param>
    /// <returns>Max. erlaubte Runs, oder null wenn unbegrenzt.</returns>
    /// <exception cref="ArgumentException">Bei BPC ohne Angabe der verbleibenden Runs.</exception>
    public static int? MaxAllowedRuns(int? maxProductionLimit, bool isCopy, int? remainingRuns)
    {
        if (isCopy && remainingRuns is null)
        {
            throw new ArgumentException("Verbleibende Runs einer BPC müssen angegeben sein.", nameof(remainingRuns));
        }

        if (maxProductionLimit is null)
        {
            return isCopy ? remainingRuns : null;
        }

        return isCopy ? Math.Min(maxProductionLimit.Value, remainingRuns!.Value) : maxProductionLimit.Value;
    }

    private static void ValidateBaseQuantity(int baseQuantity)
    {
        if (baseQuantity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(baseQuantity), "Basismenge muss ≥ 1 sein (kein Nullkosten-Pfad).");
        }
    }

    private static void ValidateMaterialEfficiency(int materialEfficiency)
    {
        if (materialEfficiency is < MinMaterialEfficiency or > MaxMaterialEfficiency)
        {
            throw new ArgumentOutOfRangeException(
                nameof(materialEfficiency),
                $"ME muss im Bereich {MinMaterialEfficiency}..{MaxMaterialEfficiency} liegen.");
        }
    }

    private static void ValidateTimeEfficiency(int timeEfficiency)
    {
        if (timeEfficiency is < 0 or > MaxTimeEfficiency)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeEfficiency),
                $"TE muss im Bereich 0..{MaxTimeEfficiency} liegen.");
        }
    }

    private static void ValidateRuns(int runs)
    {
        if (runs < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(runs), "Runs müssen ≥ 1 sein.");
        }
    }
}