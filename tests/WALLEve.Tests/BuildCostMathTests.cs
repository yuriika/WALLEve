using WALLEve.Services.Industry;

namespace WALLEve.Tests;

/// <summary>
/// Deterministische Fixtures für die Baukosten-/Build-vs-Buy-Formeln (#65):
/// - Einmalige Kostenanrechnung: jede Prozent-Komponente der Job-Gebühr wird
///   GENAU EINMAL auf den geschätzten Item-Wert angewendet (nie pro Materialzeile,
///   nie zusätzlich je Run). Quelle: EVE-Formel „Total job cost" (EVE University,
///   „Manufacturing"): EIV × (Cost Index + Facility-Steuer + SCC-Zuschlag).
/// - Die Job-Basis ist der ME0-Materialwert („cost estimation of the materials for
///   a ME0 blueprint"): besseres ME senkt die Materialkosten, aber NICHT die
///   Job-Gebühr.
/// - Unbekannte Preise/Annahmen ergeben null mit Grund — nie 0 ISK, nie einen
///   erfundenen Wert. SCC-Zuschlag fix 4 % (Viridian Tax Reforms, 2024-02-01).
/// - Kosten runden auf ganze ISK nach oben, Erlöse nach unten (konservativ).
///   Ankerpreise (Fixture): Tritanium 4 ISK, Pyerite 8 ISK, Mexallon 16 ISK,
///   Isogen 40 ISK; Basismengen Bantam-BPO (SDE, typeID 683) wie in
///   ManufacturingMathTests.
/// </summary>
public class BuildCostMathTests
{
    // Basismengen Bantam-BPO (SDE industryActivityMaterials, activity 1, je Run).
    private const int TritaniumBase = 24000;
    private const int PyeriteBase = 4500;
    private const int MexallonBase = 1875;
    private const int IsogenBase = 375;

    private const decimal TritaniumPrice = 4m;
    private const decimal PyeritePrice = 8m;
    private const decimal MexallonPrice = 16m;
    private const decimal IsogenPrice = 40m;

    // ME0-Job-Bedarf (ManufacturingMath): 26400/4950/2063/413.
    private static readonly (long Qty, decimal? Price)[] Me0Lines =
    {
        (26400, TritaniumPrice),
        (4950, PyeritePrice),
        (2063, MexallonPrice),
        (413, IsogenPrice)
    };

    // ME0-Basismengen je Run (EIV-Basis).
    private static readonly (long BaseQty, decimal? Price)[] BaseMaterials =
    {
        (TritaniumBase, TritaniumPrice),
        (PyeriteBase, PyeritePrice),
        (MexallonBase, MexallonPrice),
        (IsogenBase, IsogenPrice)
    };

    // Erwartete Werte 1 Run ME0: Summe exakt (26400×4 + 4950×8 + 2063×16 + 413×40).
    private const decimal Me0MaterialCost = 194728m;
    // EIV: Σ Basismenge×Preis = 177000 (exakt, 1 Run).
    private const decimal Ev1Run = 177000m;

    [Fact]
    public void MaterialCost_OneRunMe0_MatchesDocumentedSum()
    {
        Assert.Equal(Me0MaterialCost, BuildCostMath.MaterialCost(Me0Lines));
    }

    [Fact]
    public void MaterialCost_SingleUnknownPrice_IsNull_NeverZero()
    {
        var lines = new (long, decimal?)[] { (26400, TritaniumPrice), (4950, null) };
        Assert.Null(BuildCostMath.MaterialCost(lines));
    }

    [Fact]
    public void EstimatedItemValue_UsesMe0Base_IndependentOfBlueprintQuality()
    {
        // EIV beruht auf den ME0-Basismengen — der Blueprint-ME ändert ihn nicht.
        Assert.Equal(Ev1Run, BuildCostMath.EstimatedItemValue(BaseMaterials, runs: 1));
    }

    [Fact]
    public void MaterialCost_ScalesWithRuns()
    {
        // 2 Runs ME0: 26400→52800, 4950→9900, 2063→4124,6 aufgerundet 4125, 413→825.
        var twoRunLines = new (long, decimal?)[]
        {
            (52800, TritaniumPrice),
            (9900, PyeritePrice),
            (4125, MexallonPrice),
            (825, IsogenPrice)
        };

        Assert.Equal(389400m, BuildCostMath.MaterialCost(twoRunLines));
    }

    [Fact]
    public void JobFee_EachPercentAppliedExactlyOnce_OneTimeAccrual()
    {
        // EIV 177000, Cost Index 5 %, Facility 2 %, SCC 4 % → genau 11 % einmalig.
        var components = BuildCostMath.JobCostComponents(Ev1Run, systemCostIndexPercent: 5, facilityTaxPercent: 2);

        Assert.Equal(8850m, components.SystemCostIndexFee);  // 177000 × 5 %
        Assert.Equal(3540m, components.FacilityTax);         // 177000 × 2 %
        Assert.Equal(7080m, components.SccSurcharge);        // 177000 × 4 %
        Assert.Equal(19470m, components.Total);              // 177000 × 11 % — nicht × Materialanzahl

        // Gegenprobe: wäre jede Komponente pro Materialzeile (4 Zeilen) angewendet
        // worden, wäre die Gebühr 4× so hoch — das Fixture schließt das aus.
        Assert.Equal(BuildCostMath.RoundUpToIsk(Ev1Run * 0.11m), components.Total);
        Assert.True(components.Total < Ev1Run * 0.11m * 4m);
    }

    [Fact]
    public void JobFee_ScalesLinearlyWithRuns_ButNotPerRunSurcharge()
    {
        // 2 Runs: EIV verdoppelt sich; der Prozentsatz bleibt einmalig angewendet.
        var components = BuildCostMath.JobCostComponents(2m * Ev1Run, systemCostIndexPercent: 5, facilityTaxPercent: 2);

        Assert.Equal(354000m * 0.05m, components.SystemCostIndexFee);
        Assert.Equal(354000m * 0.02m, components.FacilityTax);
        Assert.Equal(354000m * 0.04m, components.SccSurcharge);
        Assert.Equal(19470m * 2m, components.Total);
    }

    [Fact]
    public void JobFee_UnchangedByMaterialEfficiency_Me10SameFeeAsMe0()
    {
        // ME 10 senkt den Materialbedarf (Materialkosten), aber die Job-Gebühr
        // bleibt auf ME0-Basis — EVE rechnet mit dem ME0-Item-Wert.
        var componentsMe10 = BuildCostMath.JobCostComponents(Ev1Run, systemCostIndexPercent: 5, facilityTaxPercent: 2);
        var componentsMe0 = BuildCostMath.JobCostComponents(Ev1Run, systemCostIndexPercent: 5, facilityTaxPercent: 2);

        Assert.Equal(componentsMe0.Total, componentsMe10.Total);
        Assert.Equal(19470m, componentsMe10.Total);
    }

    [Fact]
    public void JobFee_UnknownFacilityTax_LeavesFacilityAndTotalUnknown_SccStillKnown()
    {
        // Unbekannte Structure-Kosten: Facility null, Gesamt null — nie 0 ISK.
        var components = BuildCostMath.JobCostComponents(Ev1Run, systemCostIndexPercent: 5, facilityTaxPercent: null);

        Assert.Equal(8850m, components.SystemCostIndexFee);
        Assert.Null(components.FacilityTax);
        Assert.Equal(7080m, components.SccSurcharge);
        Assert.Null(components.Total);
    }

    [Fact]
    public void JobFee_UnknownSystemCostIndex_LeavesIndexFeeAndTotalUnknown()
    {
        var components = BuildCostMath.JobCostComponents(Ev1Run, systemCostIndexPercent: null, facilityTaxPercent: 2);

        Assert.Null(components.SystemCostIndexFee);
        Assert.Equal(3540m, components.FacilityTax);
        Assert.Equal(7080m, components.SccSurcharge);
        Assert.Null(components.Total);
    }

    [Fact]
    public void SellProceedsNet_AppliesSalesTax_RoundedDown()
    {
        // 100000 ISK BestBuy × 1 Stück × (1 − 1 %) = 99000; 0,5 % → floor 99500.
        Assert.Equal(99000m, BuildCostMath.SellProceedsNet(100000m, productQuantity: 1, salesTaxPercent: 1));
        Assert.Equal(99500m, BuildCostMath.SellProceedsNet(100000m, productQuantity: 1, salesTaxPercent: 0.5));
        Assert.Equal(100000m, BuildCostMath.SellProceedsNet(100000m, productQuantity: 1, salesTaxPercent: 0));
    }

    [Fact]
    public void SellProceedsNet_UnknownBuyPrice_IsNull()
    {
        Assert.Null(BuildCostMath.SellProceedsNet(null, productQuantity: 1, salesTaxPercent: 1));
    }

    [Fact]
    public void BuyCost_AppliesBrokerFee_RoundedUp()
    {
        // 200000 ISK BestSell × 1 × (1 + 1 %) = 202000; 0,5 % → ceil 201000.
        Assert.Equal(202000m, BuildCostMath.BuyCost(200000m, productQuantity: 1, brokerFeePercent: 1));
        Assert.Equal(201000m, BuildCostMath.BuyCost(200000m, productQuantity: 1, brokerFeePercent: 0.5));
    }

    [Fact]
    public void BuildVsBuySavings_PositiveMeansBuildingPays()
    {
        // Buy 202000 − Build 194728 = 7272 → Bauen lohnt sich.
        Assert.Equal(7272m, BuildCostMath.BuildVsBuySavings(202000m, 194728m));
        // Gegenprobe: Verkaufspreis unter den Baukosten → negativ (Bauen lohnt nicht).
        Assert.Equal(-7272m, BuildCostMath.BuildVsBuySavings(187456m, 194728m));
    }

    [Fact]
    public void BuildVsBuySavings_UnknownSide_IsNull()
    {
        Assert.Null(BuildCostMath.BuildVsBuySavings(null, 194728m));
        Assert.Null(BuildCostMath.BuildVsBuySavings(202000m, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void MaterialCost_InvalidQuantity_Throws(long quantity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BuildCostMath.MaterialCost(new (long, decimal?)[] { (quantity, 4m) }));
    }

    [Fact]
    public void EstimatedItemValue_InvalidRuns_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildCostMath.EstimatedItemValue(BaseMaterials, runs: 0));
    }

    [Fact]
    public void JobCostComponents_NegativePercentages_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BuildCostMath.JobCostComponents(Ev1Run, systemCostIndexPercent: -1, facilityTaxPercent: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BuildCostMath.JobCostComponents(Ev1Run, systemCostIndexPercent: 5, facilityTaxPercent: -0.5));
        // Auch im sonst unbekannten Facility-Zweig wird der Cost Index validiert.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BuildCostMath.JobCostComponents(Ev1Run, systemCostIndexPercent: -5, facilityTaxPercent: null));
    }

    [Fact]
    public void JobCostComponents_NegativeItemValue_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BuildCostMath.JobCostComponents(-1m, systemCostIndexPercent: 5, facilityTaxPercent: 2));
    }

    [Fact]
    public void SellProceedsNet_NegativeSalesTax_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BuildCostMath.SellProceedsNet(100000m, productQuantity: 1, salesTaxPercent: -1));
    }

    [Fact]
    public void BuyCost_NegativeBrokerFee_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BuildCostMath.BuyCost(200000m, productQuantity: 1, brokerFeePercent: -0.5));
    }
}