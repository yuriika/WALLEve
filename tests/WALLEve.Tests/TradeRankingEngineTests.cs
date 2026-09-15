using WALLEve.Models.Trading;
using WALLEve.Services.Trading;

namespace WALLEve.Tests;

/// <summary>
/// Tests der nachvollziehbaren Rang-Bewertung (Issue #54): Jede Dimension
/// (Netto-ISK, ROI, Kapitalbindung, Liquidität, Risiko, ISK/Stunde) wird
/// isoliert geprüft; Null-Kapital und fehlende Füllzeit werden korrekt
/// behandelt; ungültige Bereiche und fehlende Pflichtdaten sind nicht
/// ausführbar (keine LLM-Aufrufe); gleiche Eingaben + Version reproduzieren
/// Ergebnis und Erklärung.
/// </summary>
public class TradeRankingEngineTests
{
    private const string NetProfitDim = "Netto-ISK";
    private const string RoiDim = "ROI";
    private const string CapitalBindingDim = "Kapitalbindung";
    private const string LiquidityDim = "Liquidität";
    private const string RiskDim = "Risiko";
    private const string IskPerHourDim = "ISK/Stunde";

    private static TradeRankingInput Input(
        int id = 1,
        decimal? profit = 10_000m,
        decimal? capital = 100_000m,
        double? liquidity = 0.7,
        double? risk = 0.3,
        double? fillDays = 1.0)
        => new(id, profit, capital, liquidity, risk, fillDays);

    // --- Einzel-Bewertung: Dimensionen getrennt und korrekt berechnet ---

    [Fact]
    public void Dimensions_AreComputedSeparately()
    {
        var result = TradeRankingEngine.Evaluate(Input());

        Assert.True(result.IsActionable);
        Assert.Equal(TradeRankingEngine.AlgorithmVersion, result.AlgorithmVersion);
        Assert.Equal(6, result.Dimensions.Count);

        Assert.Equal(10_000d, result.Dimensions.Single(d => d.Name == NetProfitDim).Value!.Value, 3);
        Assert.Equal(10.0, result.Dimensions.Single(d => d.Name == RoiDim).Value!.Value, 3);
        Assert.Equal(100_000d, result.Dimensions.Single(d => d.Name == CapitalBindingDim).Value!.Value, 3);
        Assert.Equal(0.7, result.Dimensions.Single(d => d.Name == LiquidityDim).Value!.Value, 3);
        Assert.Equal(0.3, result.Dimensions.Single(d => d.Name == RiskDim).Value!.Value, 3);
    }

    [Fact]
    public void Roi_IsProfitOverCapital()
    {
        var result = TradeRankingEngine.Evaluate(Input(profit: 25_000m, capital: 50_000m));

        Assert.Equal(50.0, result.Dimensions.Single(d => d.Name == RoiDim).Value!.Value, 3);
    }

    [Fact]
    public void Roi_IsNegative_WhenProfitIsNegative()
    {
        var result = TradeRankingEngine.Evaluate(Input(profit: -5_000m, capital: 50_000m));

        Assert.True(result.IsActionable, "Negative Gewinne sind bewertbar (schlechter Kandidat), nicht ungültig.");
        Assert.Equal(-10.0, result.Dimensions.Single(d => d.Name == RoiDim).Value!.Value, 3);
    }

    // --- Null-Kapital / fehlende Zeit korrekt behandeln ---

    [Fact]
    public void ZeroCapital_IsNotActionable()
    {
        var result = TradeRankingEngine.Evaluate(Input(capital: 0m));

        Assert.False(result.IsActionable);
        Assert.Contains("Kapital", result.NotActionableReason);
    }

    [Fact]
    public void MissingCapital_IsNotActionable()
    {
        var result = TradeRankingEngine.Evaluate(Input(capital: null));

        Assert.False(result.IsActionable);
        Assert.Contains("Kapital", result.NotActionableReason);
    }

    [Fact]
    public void ZeroFillTime_IsNotActionable()
    {
        var result = TradeRankingEngine.Evaluate(Input(fillDays: 0));

        Assert.False(result.IsActionable);
        Assert.Contains("Füllzeit", result.NotActionableReason);
    }

    [Fact]
    public void MissingFillTime_KeepsCandidateActionable_AndTimeDimensionsNotComputable()
    {
        var result = TradeRankingEngine.Evaluate(Input(fillDays: null));

        Assert.True(result.IsActionable, "Fehlende Füllzeit darf den Kandidaten nicht insgesamt blockieren.");
        Assert.Null(result.Dimensions.Single(d => d.Name == CapitalBindingDim).Value);
        Assert.Null(result.Dimensions.Single(d => d.Name == IskPerHourDim).Value);
        Assert.Empty(result.Scenarios);
        Assert.Contains("keine Füllzeit-Annahme", result.Explanation);
    }

    // --- Ungültige Bereiche / fehlende Pflichtdaten nicht ausführbar ---

    [Fact]
    public void LiquidityOutsideRange_IsNotActionable()
    {
        var result = TradeRankingEngine.Evaluate(Input(liquidity: 1.5));

        Assert.False(result.IsActionable);
        Assert.Contains("Liquidität", result.NotActionableReason);
    }

    [Fact]
    public void RiskOutsideRange_IsNotActionable()
    {
        var result = TradeRankingEngine.Evaluate(Input(risk: -0.1));

        Assert.False(result.IsActionable);
        Assert.Contains("Risiko", result.NotActionableReason);
    }

    [Fact]
    public void MissingLiquidity_IsNotActionable()
    {
        var result = TradeRankingEngine.Evaluate(Input(liquidity: null));

        Assert.False(result.IsActionable);
        Assert.Contains("Liquidität", result.NotActionableReason);
    }

    [Fact]
    public void MissingProfit_IsNotActionable()
    {
        var result = TradeRankingEngine.Evaluate(Input(profit: null));

        Assert.False(result.IsActionable);
        Assert.Contains("Gewinn", result.NotActionableReason);
    }

    // --- Nicht-endliche Eingaben (NaN/∞) sind ungültig, nicht ausführbar ---

    [Fact]
    public void NanLiquidity_IsNotActionable()
    {
        var result = TradeRankingEngine.Evaluate(Input(liquidity: double.NaN));

        Assert.False(result.IsActionable);
        Assert.Contains("Liquidität", result.NotActionableReason);
    }

    [Fact]
    public void NanRisk_IsNotActionable()
    {
        var result = TradeRankingEngine.Evaluate(Input(risk: double.NaN));

        Assert.False(result.IsActionable);
        Assert.Contains("Risiko", result.NotActionableReason);
    }

    [Fact]
    public void NanFillTime_IsNotActionable()
    {
        var result = TradeRankingEngine.Evaluate(Input(fillDays: double.NaN));

        Assert.False(result.IsActionable);
        Assert.Contains("Füllzeit", result.NotActionableReason);
    }

    [Fact]
    public void InfiniteFillTime_IsNotActionable()
    {
        var result = TradeRankingEngine.Evaluate(Input(fillDays: double.PositiveInfinity));

        Assert.False(result.IsActionable);
        Assert.Contains("Füllzeit", result.NotActionableReason);
    }

    [Fact]
    public void InfiniteLiquidity_IsNotActionable()
    {
        var result = TradeRankingEngine.Evaluate(Input(liquidity: double.PositiveInfinity));

        Assert.False(result.IsActionable);
        Assert.Contains("Liquidität", result.NotActionableReason);
    }

    [Fact]
    public void Rank_ExcludesNonFiniteCandidates()
    {
        var valid = Input(id: 1, profit: 20_000m, capital: 100_000m, liquidity: 0.8, risk: 0.2, fillDays: 3.0);
        var nanRisk = Input(id: 2, profit: 50_000m, capital: 100_000m, liquidity: 0.5, risk: double.NaN, fillDays: 3.0);

        var outcome = TradeRankingEngine.Rank(new[] { valid, nanRisk });

        Assert.Single(outcome.Entries);
        Assert.Equal(1, outcome.Entries.Single().TradingOpportunityId);
        Assert.Contains(2, outcome.ExcludedOpportunityIds);
        Assert.DoesNotContain(2, outcome.Entries.Select(e => e.TradingOpportunityId));
    }

    // --- Szenarien: konservativ/realistisch/optimistisch, keine exakte Füllzeit ---

    [Fact]
    public void Scenarios_OrderByFillTimeMultiplier_AndIskPerHour()
    {
        var result = TradeRankingEngine.Evaluate(Input(profit: 10_000m, fillDays: 1.0));

        Assert.Equal(3, result.Scenarios.Count);

        var conservative = result.Scenarios.Single(s => s.Name == "Konservativ");
        var realistic = result.Scenarios.Single(s => s.Name == "Realistisch");
        var optimistic = result.Scenarios.Single(s => s.Name == "Optimistisch");

        Assert.True(conservative.IskPerHour < realistic.IskPerHour);
        Assert.True(realistic.IskPerHour < optimistic.IskPerHour);

        // 10.000 ISK / 24 h = 416,67 ISK/h im realistischen Szenario.
        Assert.Equal(416.67, realistic.IskPerHour!.Value, 2);

        // Die Erklärung weist explizit auf die Annahme hin (keine exakte Füllzeit-Behauptung).
        Assert.Contains("keine exakte Füllzeit", result.Explanation);
        Assert.Equal(1.5, conservative.FillTimeMultiplier, 3);
        Assert.Equal(0.6, optimistic.FillTimeMultiplier, 3);
    }

    // --- Reproduzierbarkeit: gleiche Eingaben + Version = gleiches Ergebnis + Erklärung ---

    [Fact]
    public void SameInputs_ReproduceResultAndExplanation()
    {
        var a = TradeRankingEngine.Evaluate(Input());
        var b = TradeRankingEngine.Evaluate(Input());

        Assert.Equal(a.IsActionable, b.IsActionable);
        Assert.Equal(a.AlgorithmVersion, b.AlgorithmVersion);
        Assert.Equal(a.Explanation, b.Explanation);

        // Collections sind Referenztypen: elementweise vergleichen.
        Assert.Equal(a.Scenarios.Select(s => s.IskPerHour), b.Scenarios.Select(s => s.IskPerHour));
        Assert.Equal(
            a.Dimensions.Select(d => (d.Name, d.Value, d.Unit, d.Weight, d.Explanation)),
            b.Dimensions.Select(d => (d.Name, d.Value, d.Unit, d.Weight, d.Explanation)));
    }

    // --- Rangliste ---

    [Fact]
    public void Rank_HigherRoi_CandidateWins()
    {
        var better = Input(id: 1, profit: 100_000m, capital: 1_000_000m); // 10 % ROI
        var worse = Input(id: 2, profit: 50_000m, capital: 1_000_000m);   //  5 % ROI

        var outcome = TradeRankingEngine.Rank(new[] { better, worse });

        Assert.Equal(TradeRankingEngine.AlgorithmVersion, outcome.AlgorithmVersion);
        Assert.Equal(2, outcome.Entries.Count);
        Assert.Empty(outcome.ExcludedOpportunityIds);
        Assert.Equal(1, outcome.Entries[0].TradingOpportunityId);
        Assert.Equal(2, outcome.Entries[1].TradingOpportunityId);

        foreach (var entry in outcome.Entries)
        {
            Assert.InRange(entry.WeightedScore, 0, 100);
            foreach (var point in entry.DimensionPoints.Values)
                Assert.InRange(point, 0, 100);
        }
    }

    [Fact]
    public void Rank_HigherRisk_GetsLowerPoints()
    {
        var lowRisk = Input(id: 1, risk: 0.2);
        var highRisk = Input(id: 2, risk: 0.9);

        var outcome = TradeRankingEngine.Rank(new[] { lowRisk, highRisk });

        var riskPoints = outcome.Entries.ToDictionary(e => e.TradingOpportunityId, e => e.DimensionPoints[RiskDim]);
        Assert.True(riskPoints[1] > riskPoints[2], "Risiko ist invertiert: niedrigeres Risiko muss mehr Punkte geben.");
        Assert.Equal(100, riskPoints[1], 2);
        Assert.Equal(0, riskPoints[2], 2);
    }

    [Fact]
    public void Rank_CapitalBinding_IsInverted()
    {
        var shortHolding = Input(id: 1, fillDays: 1.0);
        var longHolding = Input(id: 2, fillDays: 30.0);

        var outcome = TradeRankingEngine.Rank(new[] { shortHolding, longHolding });

        var bindingPoints = outcome.Entries.ToDictionary(e => e.TradingOpportunityId, e => e.DimensionPoints[CapitalBindingDim]);
        Assert.True(bindingPoints[1] > bindingPoints[2],
            "Kapitalbindung ist invertiert: kürzere Bindung muss mehr Punkte geben.");
    }

    [Fact]
    public void Rank_EqualCandidates_GetNeutralPointsAndStableOrder()
    {
        var outcome = TradeRankingEngine.Rank(new[] { Input(id: 1), Input(id: 2) });

        Assert.Equal(2, outcome.Entries.Count);
        // Alle Dimensionen identisch: min-max-Spanne 0 → neutrale 50 Punkte je Dimension.
        foreach (var entry in outcome.Entries)
        {
            Assert.Equal(50.0, entry.WeightedScore, 2);
            foreach (var point in entry.DimensionPoints.Values)
                Assert.Equal(50.0, point, 2);
        }
        // Stabil: gleiche Scores bleiben in Eingabereihenfolge.
        Assert.Equal(1, outcome.Entries[0].TradingOpportunityId);
        Assert.Equal(2, outcome.Entries[1].TradingOpportunityId);
    }

    [Fact]
    public void Rank_ExcludesNonActionableCandidates()
    {
        var valid = Input(id: 1);
        var invalid = Input(id: 2, capital: 0m);

        var outcome = TradeRankingEngine.Rank(new[] { valid, invalid });

        Assert.Single(outcome.Entries);
        Assert.Equal(1, outcome.Entries[0].TradingOpportunityId);
        Assert.Equal(new[] { 2 }, outcome.ExcludedOpportunityIds);
        Assert.Contains("2", outcome.Explanation);
    }

    [Fact]
    public void Rank_AllExcluded_ReturnsEmptyEntries()
    {
        var outcome = TradeRankingEngine.Rank(new[] { Input(id: 9, capital: 0m) });

        Assert.Empty(outcome.Entries);
        Assert.Equal(new[] { 9 }, outcome.ExcludedOpportunityIds);
    }

    // --- Regression (Review #131): fehlende Zeitannahme in gemischter Rangliste ---

    [Fact]
    public void Rank_MissingFillTime_DoesNotFabricateTimeDimensionPoints()
    {
        var withTime = Input(id: 1, fillDays: 5.0);
        var withoutTime = Input(id: 2, fillDays: null);

        var outcome = TradeRankingEngine.Rank(new[] { withTime, withoutTime });

        Assert.Equal(2, outcome.Entries.Count);
        Assert.Empty(outcome.ExcludedOpportunityIds);

        var withPoints = outcome.Entries.Single(e => e.TradingOpportunityId == 1).DimensionPoints;
        var withoutPoints = outcome.Entries.Single(e => e.TradingOpportunityId == 2).DimensionPoints;

        // Kandidat mit Zeitannahme hat alle sechs Dimensionen; der ohne keine zeitbasierten —
        // insbesondere KEINE erfundene 0 für Kapitalbindung/ISK/Stunde.
        Assert.Contains(CapitalBindingDim, withPoints.Keys);
        Assert.Contains(IskPerHourDim, withPoints.Keys);
        Assert.DoesNotContain(CapitalBindingDim, withoutPoints.Keys);
        Assert.DoesNotContain(IskPerHourDim, withoutPoints.Keys);
        Assert.Equal(4, withoutPoints.Count);
    }

    [Fact]
    public void Rank_MissingFillTime_DoesNotCorruptMinMaxForOtherCandidates()
    {
        var shortHolding = Input(id: 1, fillDays: 1.0);
        var longHolding = Input(id: 2, fillDays: 30.0);
        var noTime = Input(id: 3, fillDays: null);

        var outcome = TradeRankingEngine.Rank(new[] { shortHolding, longHolding, noTime });

        var bindingPoints = outcome.Entries.ToDictionary(e => e.TradingOpportunityId, e => e.DimensionPoints);
        // min/max über NUR vorhandene Werte: kurze Bindung → 100, lange → 0;
        // der zeitlose Kandidat darf das Spannen-Minimum nicht auf 0 ziehen und
        // keine Kapitalbindungs-Punkte erhalten.
        Assert.Equal(100.0, bindingPoints[1][CapitalBindingDim], 2);
        Assert.Equal(0.0, bindingPoints[2][CapitalBindingDim], 2);
        Assert.False(bindingPoints[3].ContainsKey(CapitalBindingDim));
        Assert.False(bindingPoints[3].ContainsKey(IskPerHourDim));
    }

    [Fact]
    public void Rank_MissingFillTime_RenormalizesWeightsPerCandidate()
    {
        // Kandidat 1 ohne Zeitannahme dominiert alle gemeinsamen Dimensionen;
        // Kandidat 2 hat zusätzlich zeitbasierte Dimensionen (dort einziger Wert → neutrale 50).
        var noTime = Input(id: 1, profit: 20_000m, capital: 100_000m, liquidity: 0.8, risk: 0.2, fillDays: null);
        var withTime = Input(id: 2, profit: 10_000m, capital: 100_000m, liquidity: 0.5, risk: 0.8, fillDays: 5.0);

        var outcome = TradeRankingEngine.Rank(new[] { noTime, withTime });

        var noTimeEntry = outcome.Entries.Single(e => e.TradingOpportunityId == 1);
        var withTimeEntry = outcome.Entries.Single(e => e.TradingOpportunityId == 2);

        // Gemeinsame Dimensionen: noTime überall 100, withTime überall 0.
        Assert.Equal(100.0, noTimeEntry.DimensionPoints[NetProfitDim], 2);
        Assert.Equal(100.0, noTimeEntry.DimensionPoints[RoiDim], 2);
        Assert.Equal(100.0, noTimeEntry.DimensionPoints[LiquidityDim], 2);
        Assert.Equal(100.0, noTimeEntry.DimensionPoints[RiskDim], 2);
        Assert.Equal(0.0, withTimeEntry.DimensionPoints[NetProfitDim], 2);

        // Zeitbasierte Dimensionen von withTime: einziger Wert → neutrale 50 (nicht 0, nicht 100).
        Assert.Equal(50.0, withTimeEntry.DimensionPoints[CapitalBindingDim], 2);
        Assert.Equal(50.0, withTimeEntry.DimensionPoints[IskPerHourDim], 2);

        // Gewichtung je Kandidat über dessen aktive Dimensionen: noTime gewichtet ohne
        // Kapitalbindung/ISK/Stunde (100 über die vier gemeinsamen, Gewicht 0,75) —
        // kein Vorteil durch erfundene „0-Kapitalbindung". withTime hat alle sechs
        // Dimensionen aktiv (die vier gemeinsamen mit 0 Punkten, die zwei zeitbasierten
        // mit je 50): (50×0,10 + 50×0,15) / Gewicht 1,0 = 12,5.
        Assert.Equal(100.0, noTimeEntry.WeightedScore, 2);
        Assert.Equal(12.5, withTimeEntry.WeightedScore, 2);

        // Rang: noTime vor withTime; der ohne Zeitannahme gewinnt durch die gemeinsamen
        // Dimensionen, nicht durch eine erfundene Kapitalbindung.
        Assert.Equal(1, noTimeEntry.Rank);
        Assert.Equal(2, withTimeEntry.Rank);
        Assert.Equal(4, noTimeEntry.DimensionPoints.Count);
        Assert.Equal(6, withTimeEntry.DimensionPoints.Count);
    }

    // --- Annahmen-Validierung (Review-Blocker: Faktoren/Gewichte dürfen kein NaN/∞ erzeugen) ---

    private static TradeRankingAssumptions Assumptions(
        double conservative = 1.5,
        double realistic = 1.0,
        double optimistic = 0.6,
        double netProfitWeight = 0.25,
        double roiWeight = 0.25,
        double capitalBindingWeight = 0.10,
        double liquidityWeight = 0.15,
        double riskWeight = 0.10,
        double iskPerHourWeight = 0.15)
        => new(
            conservative,
            realistic,
            optimistic,
            netProfitWeight,
            roiWeight,
            capitalBindingWeight,
            liquidityWeight,
            riskWeight,
            iskPerHourWeight);

    private static TradeRankingAssumptions WithMultiplier(TradeRankingAssumptions a, string scenario, double value)
        => scenario switch
        {
            "konservativ" => a with { ConservativeFillTimeMultiplier = value },
            "realistisch" => a with { RealisticFillTimeMultiplier = value },
            "optimistisch" => a with { OptimisticFillTimeMultiplier = value },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

    private static TradeRankingAssumptions WithWeight(TradeRankingAssumptions a, string dimension, double value)
        => dimension switch
        {
            NetProfitDim => a with { NetProfitWeight = value },
            RoiDim => a with { RoiWeight = value },
            CapitalBindingDim => a with { CapitalBindingWeight = value },
            LiquidityDim => a with { LiquidityWeight = value },
            RiskDim => a with { RiskWeight = value },
            IskPerHourDim => a with { IskPerHourWeight = value },
            _ => throw new ArgumentOutOfRangeException(nameof(dimension))
        };

    [Fact]
    public void DefaultAssumptions_ProduceOnlyFiniteValues()
    {
        var result = TradeRankingEngine.Evaluate(Input());

        Assert.True(result.IsActionable);
        Assert.All(result.Dimensions, d => Assert.True(d.Value is null || double.IsFinite(d.Value.Value)));
        Assert.All(result.Scenarios, s => Assert.True(
            (s.AssumedFillHours is double assumedHours && double.IsFinite(assumedHours))
            && (s.IskPerHour is double achievedPerHour && double.IsFinite(achievedPerHour))));
    }

    [Fact]
    public void InvalidFillTimeMultipliers_ThrowForEveryScenario()
    {
        // Faktor 0, negativ, NaN und ∞ sind je Szenario ungültig — sie würden sonst
        // Infinity/NaN für ISK/Stunde und Szenario-Ausgabe erzeugen.
        foreach (var scenario in new[] { "konservativ", "realistisch", "optimistisch" })
        {
            foreach (var invalid in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity })
            {
                var assumptions = WithMultiplier(TradeRankingAssumptions.Default, scenario, invalid);
                var ex = Assert.Throws<ArgumentOutOfRangeException>(
                    () => { TradeRankingEngine.Evaluate(Input(), assumptions); });
                Assert.Contains(scenario, ex.Message);
                Assert.Contains("Füllzeit-Faktor", ex.Message);
            }
        }
    }

    [Fact]
    public void InvalidWeights_ThrowForEveryDimension()
    {
        // Jedes einzelne Gewicht: negativ, NaN und ∞ sind ungültig — kein Pfad für
        // NaN in Score/Normalisierung, egal welche Dimension betroffen ist.
        var dimensions = new[] { NetProfitDim, RoiDim, CapitalBindingDim, LiquidityDim, RiskDim, IskPerHourDim };
        foreach (var dimension in dimensions)
        {
            foreach (var invalid in new[] { -0.1, double.NaN, double.PositiveInfinity })
            {
                var assumptions = WithWeight(TradeRankingAssumptions.Default, dimension, invalid);
                var ex = Assert.Throws<ArgumentOutOfRangeException>(
                    () => { TradeRankingEngine.Evaluate(Input(), assumptions); });
                Assert.Contains(dimension, ex.Message);
                Assert.Contains("Gewicht", ex.Message);
            }
        }
    }

    [Fact]
    public void ZeroWeightTotal_Throws()
    {
        var assumptions = Assumptions(
            netProfitWeight: 0, roiWeight: 0, capitalBindingWeight: 0,
            liquidityWeight: 0, riskWeight: 0, iskPerHourWeight: 0);

        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => { TradeRankingEngine.Evaluate(Input(), assumptions); });
        Assert.Contains("Gewichtssumme", ex.Message);
    }

    [Fact]
    public void Rank_WithInvalidAssumptions_Throws()
    {
        var inputs = new[] { Input(id: 1), Input(id: 2, profit: 20_000m) };

        // Wie bei Evaluate: ungültige Annahmen sind Konfigurationsfehler, kein Kandidaten-Defekt.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => { TradeRankingEngine.Rank(inputs, Assumptions(realistic: 0.0)); });
        Assert.Throws<ArgumentOutOfRangeException>(
            () => { TradeRankingEngine.Rank(inputs, Assumptions(riskWeight: double.NaN)); });
    }

    [Fact]
    public void Rank_UnnormalizedButValidWeights_ProduceFiniteScores()
    {
        // Gewichte müssen nicht auf 1 summieren: der Score wird je Kandidat über dessen
        // aktive Dimensionen renormalisiert. Eine skalierte, valide Gewichtsmenge
        // (Summe 2,0) liefert daher dieselben Scores wie die Defaults — kein NaN, kein ∞.
        var inputs = new[] { Input(id: 1, profit: 30_000m), Input(id: 2, profit: 5_000m, risk: 0.8) };
        var scaled = Assumptions(
            netProfitWeight: 0.5, roiWeight: 0.5, capitalBindingWeight: 0.2,
            liquidityWeight: 0.3, riskWeight: 0.2, iskPerHourWeight: 0.3);

        var baseline = TradeRankingEngine.Rank(inputs, TradeRankingAssumptions.Default);
        var outcome = TradeRankingEngine.Rank(inputs, scaled);

        Assert.All(outcome.Entries, e => Assert.True(double.IsFinite(e.WeightedScore)));
        Assert.All(outcome.Entries, e => Assert.All(e.DimensionPoints.Values, p => Assert.True(double.IsFinite(p))));
        Assert.Equal(
            baseline.Entries.Select(e => e.WeightedScore),
            outcome.Entries.Select(e => e.WeightedScore));
    }

    // --- Review-Runde 2: keine 0/0- oder ∞/∞-Pfade (NaN-Freiheit auch bei Extremwerten) ---

    [Fact]
    public void LargeFiniteFillTime_IsNotActionable_WithClearReason()
    {
        // Sehr große, aber endliche Füllzeit: Kapital × Füllzeit überläuft im double-Bereich
        // zu ∞. Der Kandidat ist deterministisch NICHT ausführbar (klare Begründung), statt
        // eine ∞-Kapitalbindung und daraus 0/NaN-Ergebnisse zu erzeugen.
        var input = Input(id: 1, profit: 10_000m, capital: 1_000m, fillDays: 1e308);

        var result = TradeRankingEngine.Evaluate(input);

        Assert.False(result.IsActionable);
        Assert.Contains("Füllzeit", result.NotActionableReason);
        Assert.Contains("darstellbaren", result.NotActionableReason);

        var outcome = TradeRankingEngine.Rank(new[] { input });
        Assert.Empty(outcome.Entries);
        Assert.Contains(1, outcome.ExcludedOpportunityIds);
        Assert.Contains("1", outcome.Explanation);
    }

    [Fact]
    public void TinyHoursDerivedValue_IsNotActionable_WithClearReason()
    {
        // Winzige, aber endliche Füllzeit: Füllzeit × 24 h × Faktor überläuft zu ∞
        // (Szenario-Stunden nicht endlich darstellbar). Auch dieser Pfad ist
        // deterministisch „nicht ausführbar“ — nie ein erfundener ∞- oder 0-ISK/h-Wert.
        var input = Input(id: 1, profit: 10_000m, capital: 0.0000000000000000000000000001m, fillDays: 8e306);

        var result = TradeRankingEngine.Evaluate(input);

        Assert.False(result.IsActionable);
        Assert.Contains("Füllzeit × 24 h", result.NotActionableReason);
    }

    [Fact]
    public void Rank_ZeroActiveWeight_ExcludesCandidate_WithoutNaNScores()
    {
        // Review-Blocker: Alle gemeinsamen Gewichte 0, positives Gewicht nur auf einer
        // optionalen Dimension, die dem Kandidaten fehlt (keine Füllzeit) → die Summe
        // seiner aktiven Gewichte ist 0. Eine Division 0/0 ergäbe NaN; der Kandidat wird
        // deterministisch ausgeschlossen und in der Erklärung als „nicht rangierbar“ genannt.
        var assumptions = Assumptions(
            netProfitWeight: 0, roiWeight: 0, capitalBindingWeight: 1.0,
            liquidityWeight: 0, riskWeight: 0, iskPerHourWeight: 0);

        var noTime = Input(id: 1, profit: 20_000m, capital: 100_000m, liquidity: 0.8, risk: 0.2, fillDays: null);
        var withTime = Input(id: 2, profit: 10_000m, capital: 100_000m, liquidity: 0.5, risk: 0.8, fillDays: 5.0);

        var outcome = TradeRankingEngine.Rank(new[] { noTime, withTime }, assumptions);

        Assert.DoesNotContain(1, outcome.Entries.Select(e => e.TradingOpportunityId));
        Assert.Contains(1, outcome.ExcludedOpportunityIds);
        Assert.Contains("nicht rangierbar", outcome.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.All(outcome.Entries, e =>
        {
            Assert.True(double.IsFinite(e.WeightedScore));
            Assert.All(e.DimensionPoints.Values, p => Assert.True(double.IsFinite(p)));
        });

        // Der Kandidat mit Füllzeit bleibt bewertbar: nur seine Kapitalbindung trägt Gewicht 1.
        var withTimeEntry = Assert.Single(outcome.Entries);
        Assert.Equal(2, withTimeEntry.TradingOpportunityId);
        Assert.Equal(1, withTimeEntry.Rank);
        Assert.Equal(50.0, withTimeEntry.DimensionPoints[CapitalBindingDim], 2);
    }

    [Fact]
    public void Rank_ExtremeFiniteValues_KeepNormalizationFinite()
    {
        // Extrem kleine, aber endliche Füllzeit erzeugt riesige endliche ISK/Stunde-Werte
        // (≈ ±9,9e307). Die Spanne max - min würde zu ∞ überlaufen; der alte Ausdruck
        // (value - min) / span lieferte dann ∞/∞ = NaN. Die skalierte Normalisierung bleibt
        // endlich und mathematisch identisch: 100 Punkte für das Maximum, 0 für das Minimum.
        decimal hugeProfit = 10_000_000_000_000_000_000_000_000_000m; // 1e28 (decimal-max-tauglich)
        var a = Input(id: 1, profit: hugeProfit, capital: 1_000m, liquidity: 0.7, risk: 0.3, fillDays: 4.2e-282);
        var b = Input(id: 2, profit: -hugeProfit, capital: 1_000m, liquidity: 0.7, risk: 0.3, fillDays: 4.2e-282);

        var outcome = TradeRankingEngine.Rank(new[] { a, b });

        Assert.All(outcome.Entries, e =>
        {
            Assert.True(double.IsFinite(e.WeightedScore));
            Assert.All(e.DimensionPoints.Values, p => Assert.True(double.IsFinite(p)));
        });
        Assert.Equal(100.0, outcome.Entries.Single(e => e.TradingOpportunityId == 1).DimensionPoints[IskPerHourDim], 2);
        Assert.Equal(0.0, outcome.Entries.Single(e => e.TradingOpportunityId == 2).DimensionPoints[IskPerHourDim], 2);
        Assert.Equal(1, outcome.Entries[0].TradingOpportunityId); // Kandidat A (positiver Gewinn) vorn
    }
}
