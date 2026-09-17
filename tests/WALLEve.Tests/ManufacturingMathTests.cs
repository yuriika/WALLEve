using WALLEve.Services.Industry;

namespace WALLEve.Tests;

/// <summary>
/// Deterministische Fixtures für die fixierte Rundungs- und Waste-Regel (#56):
/// - Material-Waste 10 % bei ME 0, reduziert um 1/(1+ME); ME 0..10.
/// - Rundung pro JOB (nicht pro Run), mindestens eine Einheit je Material/Run
///   (Quelle: EVE University Wiki, "Manufacturing", Abschnitt "Beware of rounding
///   errors": ein Mehrfach-Run-Job braucht dadurch weniger als mehrere Einzeljobs).
/// - Basisdaten fixiert aus der lokalen Fuzzwork-SDE (Bantam-BPO typeID 683:
///   Tritanium 24000, Pyerite 4500, Mexallon 1875, Isogen 375; Basiszeit 6000 s).
///   EVE Ref dient nur als nachvollziehbare Referenzfixture, nie als
///   Runtime-Abhängigkeit.
/// </summary>
public class ManufacturingMathTests
{
    // Basismengen Bantam-BPO (SDE industryActivityMaterials, activity 1).
    private const int TritaniumBase = 24000;
    private const int PyeriteBase = 4500;
    private const int MexallonBase = 1875;
    private const int IsogenBase = 375;

    [Theory]
    [InlineData(TritaniumBase, 26400)] // 24000 × 1,1 = 26400 exakt
    [InlineData(PyeriteBase, 4950)]    // 4500 × 1,1 = 4950 exakt
    [InlineData(MexallonBase, 2063)]   // 1875 × 1,1 = 2062,5 → Rundungsgrenze: aufgerundet
    [InlineData(IsogenBase, 413)]      // 375 × 1,1 = 412,5 → Rundungsgrenze: aufgerundet
    public void SingleRunMe0_AppliesTenPercentWasteRoundedUp(int baseQuantity, long expected)
    {
        Assert.Equal(expected, ManufacturingMath.MaterialRequirementForJob(baseQuantity, materialEfficiency: 0, runs: 1));
    }

    [Fact]
    public void TwoRunsInOneJob_RoundPerJob_NotPerRun()
    {
        // 2 Runs in EINEM Job: ceil(1875 × 2 × 1,1) = 4125.
        var singleJob = ManufacturingMath.MaterialRequirementForJob(MexallonBase, materialEfficiency: 0, runs: 2);

        // 2 Einzeljobs wären 2 × 2063 = 4126. Der Mehrfach-Run-Job braucht weniger —
        // genau das dokumentierte "rounding is done per job instead of per run".
        Assert.Equal(4125, singleJob);
        Assert.True(singleJob < 2 * ManufacturingMath.MaterialRequirementForJob(MexallonBase, materialEfficiency: 0, runs: 1));
    }

    [Theory]
    [InlineData(0, TritaniumBase, 26400)]
    [InlineData(5, TritaniumBase, 24400)]  // 24000 × (1 + 0,1/6) = exakt 24400
    [InlineData(10, TritaniumBase, 24219)] // 24000 + floor-frei: 24000×1,00909… = 24218,18 → 24219
    [InlineData(5, IsogenBase, 382)]       // 375 × 1,01666… = 381,25 → 382 (Rundungsgrenze)
    [InlineData(10, IsogenBase, 379)]      // 375 × 1,00909… = 378,41 → 379
    public void WasteFactor_FollowsOneOverOnePlusMe(int me, int baseQuantity, long expected)
    {
        Assert.Equal(expected, ManufacturingMath.MaterialRequirementForJob(baseQuantity, me, runs: 1));
    }

    [Fact]
    public void Me10_OneUnitPerRun_StillRequiresOnePerRunMinimum()
    {
        // 100 Runs einer 1-Einheit-Materials bei ME 10: Bedarf = max(100, ceil(100×1,00909))
        // bleibt mindestens 101 — konservativ, nie unter dem Run-Minimum.
        var perJob = ManufacturingMath.MaterialRequirementForJob(1, materialEfficiency: 10, runs: 100);
        Assert.Equal(101, perJob);
        Assert.True(perJob >= 100);
    }

    [Fact]
    public void Me0_OneUnitPerRun_RoundsTheTenPercentUp()
    {
        // 1 Einheit bei ME 0: 10 % Waste = 0,1 → aufgerundet 2.
        Assert.Equal(2, ManufacturingMath.MaterialRequirementForJob(1, materialEfficiency: 0, runs: 1));
    }

    [Theory]
    [InlineData(0, 6000, 1, 6000)]
    [InlineData(20, 6000, 1, 4800)] // 20 % Zeitersparnis
    [InlineData(20, 601, 1, 481)]   // 601 × 0,8 = 480,8 → Rundungsgrenze: aufgerundet
    [InlineData(20, 601, 2, 962)]   // je-Run gerundet, dann × Runs
    [InlineData(10, 600, 3, 1620)]  // 600 × 0,9 = 540 je Run × 3
    public void JobTimeSeconds_AppliesTeAndRoundsPerRun(int te, int baseSeconds, int runs, long expected)
    {
        Assert.Equal(expected, ManufacturingMath.JobTimeSeconds(baseSeconds, te, runs));
    }

    [Theory]
    [InlineData(0)]   // Basismenge 0 → kein Nullkosten-Pfad
    [InlineData(-5)]  // negative Basismenge
    public void MaterialRequirementForJob_RejectsInvalidBaseQuantity(int baseQuantity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ManufacturingMath.MaterialRequirementForJob(baseQuantity, materialEfficiency: 0, runs: 1));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public void MaterialRequirementForJob_RejectsInvalidMe(int me)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ManufacturingMath.MaterialRequirementForJob(10, me, runs: 1));
    }

    [Fact]
    public void JobTimeSeconds_RejectsInvalidTe()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ManufacturingMath.JobTimeSeconds(6000, 21, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ManufacturingMath.JobTimeSeconds(6000, -1, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Runs_BelowOneAreRejected(int runs)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ManufacturingMath.MaterialRequirementForJob(10, materialEfficiency: 0, runs));
        Assert.Throws<ArgumentOutOfRangeException>(() => ManufacturingMath.JobTimeSeconds(6000, 0, runs));
    }

    [Theory]
    [InlineData(30, false, null, 30)]        // BPO mit SDE-Limit
    [InlineData(null, false, null, null)]    // BPO ohne SDE-Limit → unbegrenzt
    [InlineData(30, true, 10, 10)]           // BPC: Limit 30, verbleibend 10 → 10
    [InlineData(30, true, 40, 30)]           // BPC: Limit 30, verbleibend 40 → 30
    [InlineData(null, true, 7, 7)]           // BPC: nur verbleibende Runs
    public void MaxAllowedRuns_CombinesSdeLimitAndCopyRuns(int? sdeLimit, bool isCopy, int? remaining, int? expected)
    {
        Assert.Equal(expected, ManufacturingMath.MaxAllowedRuns(sdeLimit, isCopy, remaining));
    }

    [Fact]
    public void MaxAllowedRuns_BpcWithoutRemainingRuns_Throws()
    {
        Assert.Throws<ArgumentException>(() => ManufacturingMath.MaxAllowedRuns(30, isCopy: true, remainingRuns: null));
    }
}