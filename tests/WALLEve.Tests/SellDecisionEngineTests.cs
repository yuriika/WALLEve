using WALLEve.Models.Esi.Character;
using WALLEve.Services.Market;
using WALLEve.Services.Market.Interfaces;
using WALLEve.Services.Trading;

namespace WALLEve.Tests;

/// <summary>
/// Tests der ortsgenauen Verkaufsentscheidung (Issue #61): mehrstufige Tiefe, fehlende
/// Quote, getrennte Asset-Orte, Netto-/Gebührenfixtures (Gebühren genau einmal) und
/// gültige Tick-/Queue-Ziele. Fehlende Pflichtdaten ergeben KEINE Empfehlung, sondern
/// eine deutsche Begründung. Alle Fixtures sind deterministisch und ohne Live-ESI.
/// </summary>
public class SellDecisionEngineTests
{
    private const long Jita = 60003760;   // eigener Ort (Station)
    private const long Amarr = 60008494;  // fremder Ort (andere Station, anderes Asset-Lager)

    private readonly IFeeCalculatorService _fees = new FeeCalculatorService();

    private static CharacterSkills SkillsWith(int brokerRelationsLevel, int accountingLevel) => new()
    {
        Skills = new List<CharacterSkill>
        {
            new() { SkillId = 3446, TrainedSkillLevel = brokerRelationsLevel },  // Broker Relations
            new() { SkillId = 16622, TrainedSkillLevel = accountingLevel }      // Accounting
        }
    };

    private static OrderBookLine Buy(
        double price,
        int volume,
        bool reachable = true,
        long locationId = Jita,
        DateTime? issued = null,
        bool own = false)
        => new()
        {
            OrderId = (long)(price * 1000) + volume,
            IsOwn = own,
            IsBuyOrder = true,
            Price = price,
            VolumeRemain = volume,
            VolumeTotal = volume,
            LocationId = locationId,
            IsSameLocation = locationId == Jita,
            CanReachOwnLocation = reachable && !own,
            Issued = issued ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

    private static OrderBookLine Sell(
        double price,
        int volume,
        bool sameLocation = true,
        DateTime? issued = null,
        bool own = false)
        => new()
        {
            OrderId = (long)(price * 1000) + volume + 7,
            IsOwn = own,
            IsBuyOrder = false,
            Price = price,
            VolumeRemain = volume,
            VolumeTotal = volume,
            LocationId = sameLocation ? Jita : Amarr,
            IsSameLocation = sameLocation && !own,
            CanReachOwnLocation = false,
            Issued = issued ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

    private static SellDecisionInput Input(
        int owned = 100,
        double? costBasis = 1_000,
        IReadOnlyList<OrderBookLine>? buys = null,
        IReadOnlyList<OrderBookLine>? sells = null,
        OrderBookDataStatus status = OrderBookDataStatus.Ok,
        double? tickOverride = null,
        CharacterSkills? skills = null,
        long locationId = Jita,
        string? locationName = "Jita IV-4")
        => new()
        {
            TypeId = 34,
            TypeName = "Tritanium",
            LocationId = locationId,
            LocationName = locationName,
            OwnedQuantityAtLocation = owned,
            CostBasisPerUnit = costBasis,
            BuySide = buys ?? Array.Empty<OrderBookLine>(),
            SellSide = sells ?? Array.Empty<OrderBookLine>(),
            ForeignDataStatus = status,
            TickSizeOverride = tickOverride,
            Skills = skills
        };

    // ------------------------------------------------------------------
    // Sell-now: mehrstufige Tiefe begrenzt die Menge
    // ------------------------------------------------------------------

    [Fact]
    public void SellNow_ExecutesAcrossMultipleDepthLevels_AndAppliesFeesOncePerLevel()
    {
        var buys = new[] { Buy(1_200, 30), Buy(1_150, 40), Buy(1_100, 100) };
        var input = Input(owned: 100, costBasis: 900, buys: buys);

        var result = SellDecisionEngine.Evaluate(input, _fees);

        Assert.True(result.IsActionable);
        var sellNow = Assert.IsType<SellNowPlan>(result.SellNow);
        Assert.True(sellNow.IsEvaluable);

        // 30 @ 1200 + 40 @ 1150 + 30 @ 1100 — Besitz (100) ist bindend, nicht die Tiefe (170).
        Assert.Equal(100, sellNow.Quantity);
        Assert.Equal(0, sellNow.UnfilledQuantity);
        Assert.False(sellNow.IsPartialFill);
        Assert.Equal(3, sellNow.LevelsUsed);
        Assert.Equal(1_200, sellNow.BestPrice, 6);
        Assert.Equal((30 * 1_200 + 40 * 1_150 + 30 * 1_100) / 100.0, sellNow.AveragePrice, 6);

        // Gebühren: Summe der Stufen-Ergebnisse — keine Stufe doppelt, keine Zusatzgebühr.
        var expectedGross = 30 * 1_200.0 + 40 * 1_150.0 + 30 * 1_100.0;
        var expectedBroker = 30 * 1_200.0 * 0.03 + 40 * 1_150.0 * 0.03 + 30 * 1_100.0 * 0.03;
        var expectedTax = 30 * 1_200.0 * 0.075 + 40 * 1_150.0 * 0.075 + 30 * 1_100.0 * 0.075;

        Assert.Equal(expectedGross, sellNow.GrossAmount, 6);
        Assert.Equal(expectedBroker, sellNow.BrokerFee, 6);
        Assert.Equal(expectedTax, sellNow.SalesTax, 6);
        Assert.Equal(expectedGross - expectedBroker - expectedTax, sellNow.NetAmount, 6);
        Assert.Equal(sellNow.GrossAmount - sellNow.BrokerFee - sellNow.SalesTax, sellNow.NetAmount, 6);

        // Gewinn gegen die gespeicherte Cost Basis (Erwerbskosten bereits enthalten).
        Assert.Equal(sellNow.NetAmount - 900 * 100, sellNow.NetProfit!.Value, 6);
        Assert.True(sellNow.IsProfitable);
        Assert.Equal(SellDecisionAction.SellNow, result.RecommendedAction);
    }

    [Fact]
    public void SellNow_MultiLevelNet_EqualsAggregateFeeCalculation()
    {
        var buys = new[] { Buy(2_000, 50), Buy(1_900, 50) };
        var input = Input(owned: 100, costBasis: 1_000, buys: buys);

        var sellNow = SellDecisionEngine.Evaluate(input, _fees).SellNow!;

        // Lineare Gebührensätze: Summe der Stufen ist identisch zur Rechnung auf die Gesamtmenge
        // — die Gebühren werden genau einmal auf die ausgeführte Wertsumme erhoben.
        var single = _fees.CalculateSellProceeds(sellNow.AveragePrice, sellNow.Quantity, null);
        Assert.Equal(single.GrossAmount, sellNow.GrossAmount, 6);
        Assert.Equal(single.NetAmount, sellNow.NetAmount, 6);
    }

    [Fact]
    public void SellNow_PartialFill_WhenDepthIsSmallerThanOwnership()
    {
        var buys = new[] { Buy(1_200, 100) };
        var input = Input(owned: 500, costBasis: 900, buys: buys);

        var result = SellDecisionEngine.Evaluate(input, _fees);
        var sellNow = result.SellNow!;

        Assert.True(sellNow.IsEvaluable);
        Assert.Equal(100, sellNow.Quantity);
        Assert.Equal(400, sellNow.UnfilledQuantity);
        Assert.True(sellNow.IsPartialFill);
        Assert.Contains("400 Stück deckt die Nachfrage nicht ab", result.RecommendationReason);
    }

    [Fact]
    public void SellNow_IgnoresOrdersThatCannotReachOwnLocation()
    {
        // Der teurere Käufer sitzt an einem anderen Ort mit station-Range → keine Nachfrage hier.
        var buys = new[] { Buy(1_400, 50, reachable: false, locationId: Amarr), Buy(1_200, 100) };
        var input = Input(owned: 100, costBasis: 900, buys: buys);

        var result = SellDecisionEngine.Evaluate(input, _fees);
        var sellNow = result.SellNow!;

        Assert.Equal(1_200, sellNow.BestPrice, 6);
        Assert.Equal(100, sellNow.Quantity);
        Assert.Single(SellDecisionEngine.ReachableBuyDepth(buys));
        Assert.Equal(1, SellDecisionEngine.IgnoredBuyOrderCount(buys));
        Assert.Contains(result.Evidence, e => e.Contains("erreichen den Ort nicht"));
    }

    [Fact]
    public void SellNow_NotEvaluable_WithoutReachableDemand()
    {
        var buys = new[] { Buy(1_400, 50, reachable: false, locationId: Amarr) };
        var input = Input(owned: 100, costBasis: 900, buys: buys);

        var result = SellDecisionEngine.Evaluate(input, _fees);

        Assert.False(result.SellNow!.IsEvaluable);
        Assert.Contains("Keine erreichbare Buy-Order", result.SellNow!.NotEvaluableReason);
        Assert.Empty(SellDecisionEngine.ReachableBuyDepth(buys));
    }

    // ------------------------------------------------------------------
    // Getrennte Asset-Orte
    // ------------------------------------------------------------------

    [Fact]
    public void SeparateAssetLocation_ProducesNoRecommendation()
    {
        // Besitz liegt an einem anderen Ort als dem bewerteten: hier ist nichts verkaufbar.
        var buys = new[] { Buy(1_400, 100) };
        var input = Input(owned: 0, costBasis: 900, buys: buys);

        var result = SellDecisionEngine.Evaluate(input, _fees);

        Assert.False(result.IsActionable);
        Assert.Contains("Keine Bestandsmenge am Ort", result.NotActionableReason);
        Assert.Equal(SellDecisionAction.Hold, result.RecommendedAction);
        Assert.Null(result.SellNow);
        Assert.Null(result.List);
    }

    [Fact]
    public void SeparateAssetLocation_SellQuotesElsewhereAreNotCompetition()
    {
        var sells = new[] { Sell(1_000, 200, sameLocation: false), Sell(1_100, 50) };
        var input = Input(owned: 100, costBasis: 900, buys: new[] { Buy(1_050, 100) }, sells: sells);

        var result = SellDecisionEngine.Evaluate(input, _fees);

        // Nur die Quote am eigenen Ort zählt: bestes Angebot ist 1.100, nicht 1.000.
        Assert.Equal(1_100, result.List!.BestCompetingSellPrice, 6);
        Assert.Equal(1, SellDecisionEngine.IgnoredSellQuoteCount(sells));
        Assert.Contains(result.Evidence, e => e.Contains("Sell-Order(s) an anderen Orten"));
    }

    // ------------------------------------------------------------------
    // List: gültiges Tick-/Queue-Ziel
    // ------------------------------------------------------------------

    [Fact]
    public void List_UndercutsBestQuoteByOneTick_AndReportsQueueTarget()
    {
        // Günstigstes Angebot am Ort = 1.000 ISK; die neue Order unterbietet um eine Tick-Stufe.
        var sells = new[] { Sell(1_000, 200), Sell(1_050, 50) };
        var input = Input(owned: 300, costBasis: 800, buys: new[] { Buy(900, 50) }, sells: sells);

        var list = SellDecisionEngine.Evaluate(input, _fees).List!;

        Assert.True(list.IsEvaluable);
        Assert.Equal(1.0, list.TickSize, 6);          // ≥ 1.000 ISK → ganze ISK-Schritte
        Assert.Equal(999.0, list.TargetPrice, 6);     // 1.000 − eine Tick-Stufe
        Assert.Equal(1.0, list.UndercutAmount, 6);
        Assert.Equal(300, list.Quantity);             // Listmenge = Besitz am Ort
        Assert.Equal(1, list.QueuePosition);          // kein billigeres Angebot am Ort
        Assert.Equal(0, list.CheaperQuantityAhead);
        Assert.Equal(250, list.CompetingQuantityBehind); // 200 @ 1.000 + 50 @ 1.050 hinter der neuen Order
        Assert.True(list.WouldBeBestAtLocation);
        Assert.False(list.IsBelowBreakEven);
    }

    [Fact]
    public void List_UsesCentTicksBelowThousandIsk()
    {
        var sells = new[] { Sell(100.0, 10) };
        var input = Input(owned: 10, costBasis: 50, buys: new[] { Buy(80, 10) }, sells: sells);

        var list = SellDecisionEngine.Evaluate(input, _fees).List!;

        Assert.Equal(0.01, list.TickSize, 6);
        Assert.Equal(99.99, list.TargetPrice, 6);
        Assert.Equal(0.01, list.UndercutAmount, 6);
        Assert.True(list.WouldBeBestAtLocation);
        Assert.Equal(1, list.QueuePosition);
        Assert.Equal(0, list.CheaperQuantityAhead);
        Assert.Equal(10, list.CompetingQuantityBehind);
    }

    [Fact]
    public void List_FlagsTargetBelowBreakEven()
    {
        // Cost Basis 2.000 → Break-even ≈ 2.234,60; Zielpreis 1.999 liegt darunter.
        var sells = new[] { Sell(2_000, 50) };
        var input = Input(owned: 50, costBasis: 2_000, buys: new[] { Buy(1_500, 50) }, sells: sells);

        var list = SellDecisionEngine.Evaluate(input, _fees).List!;

        Assert.True(list.IsBelowBreakEven);
        Assert.False(list.IsProfitable);
        Assert.True(list.BreakEvenPrice > list.TargetPrice);
    }

    [Fact]
    public void List_NotEvaluable_WithoutQuoteAtOwnLocation()
    {
        var input = Input(owned: 100, costBasis: 900, buys: new[] { Buy(1_200, 100) }, sells: Array.Empty<OrderBookLine>());

        var result = SellDecisionEngine.Evaluate(input, _fees);

        Assert.False(result.List!.IsEvaluable);
        Assert.Contains("Kein Angebot am eigenen Ort", result.List!.NotEvaluableReason);
        Assert.Equal(SellDecisionAction.SellNow, result.RecommendedAction);
        Assert.Contains(result.Evidence, e => e.Contains("kein Angebot am eigenen Ort"));
    }

    [Fact]
    public void List_PriceTargetIsAlwaysAValidTickStep()
    {
        var sells = new[] { Sell(1_234.56, 10) };
        var input = Input(owned: 10, costBasis: 900, buys: new[] { Buy(1_100, 10) }, sells: sells);

        var list = SellDecisionEngine.Evaluate(input, _fees).List!;

        Assert.Equal(1_233.0, list.TargetPrice, 6);
        Assert.Equal(list.TargetPrice, SellDecisionEngine.RoundDownToTick(list.TargetPrice, list.TickSize), 6);
        Assert.True(list.TargetPrice < list.BestCompetingSellPrice);
    }

    [Fact]
    public void TickRules_FollowPriceRanges()
    {
        Assert.Equal(0.01, SellDecisionEngine.DeriveTickSize(999.99), 6);
        Assert.Equal(1.0, SellDecisionEngine.DeriveTickSize(1_000.0), 6);
        Assert.Equal(99.99, SellDecisionEngine.RoundDownToTick(99.99, 0.01), 6);
        Assert.Equal(1_233.0, SellDecisionEngine.RoundDownToTick(1_233.99, 1.0), 6);
        // Nie aufrunden: ein gerundeter Preis wäre höher als beabsichtigt (und damit ungültig unterboten).
        Assert.True(SellDecisionEngine.RoundDownToTick(1_233.99, 1.0) <= 1_233.99);
    }

    // ------------------------------------------------------------------
    // Empfehlung
    // ------------------------------------------------------------------

    [Fact]
    public void Recommendation_PrefersHigherEvidencedNetProfit()
    {
        // Sofort 1.000 ISK, List-Ziel 1.200 ISK → List ist belegbar besser.
        var buys = new[] { Buy(1_000, 100) };
        var sells = new[] { Sell(1_201, 100) };
        var input = Input(owned: 100, costBasis: 800, buys: buys, sells: sells);

        var result = SellDecisionEngine.Evaluate(input, _fees);

        Assert.Equal(1_200.0, result.List!.TargetPrice, 6);
        Assert.Equal(SellDecisionAction.List, result.RecommendedAction);
        Assert.Contains("Empfehlung list", result.RecommendationReason);
        Assert.Contains("Alternative", result.RecommendationReason);
    }

    [Fact]
    public void Recommendation_PrefersSellNowOnEqualProfit_BecauseExecutionIsEvidenced()
    {
        // Beide Optionen netto identisch: SellNow ist durch die vorhandene Tiefe belegt.
        var buys = new[] { Buy(1_000, 100) };
        var sells = new[] { Sell(1_001, 100) };
        var input = Input(owned: 100, costBasis: 800, buys: buys, sells: sells);

        var result = SellDecisionEngine.Evaluate(input, _fees);

        Assert.Equal(1_000.0, result.SellNow!.AveragePrice, 6);
        Assert.Equal(1_000.0, result.List!.TargetPrice, 6);
        Assert.Equal(result.SellNow.NetProfit!.Value, result.List.NetProfit!.Value, 6);
        Assert.Equal(SellDecisionAction.SellNow, result.RecommendedAction);
    }

    [Fact]
    public void Recommendation_HoldsWhenBothOptionsAreBelowCostBasis()
    {
        var buys = new[] { Buy(1_000, 100) };
        var sells = new[] { Sell(1_010, 100) };
        var input = Input(owned: 100, costBasis: 2_000, buys: buys, sells: sells);

        var result = SellDecisionEngine.Evaluate(input, _fees);

        Assert.Equal(SellDecisionAction.Hold, result.RecommendedAction);
        Assert.Contains("keine Option erreicht die Cost Basis", result.RecommendationReason);
        Assert.Contains("Verlust", result.RecommendationReason);
        Assert.Equal(100, result.Hold.Quantity);
        Assert.False(result.SellNow!.IsProfitable);
        Assert.False(result.List!.IsProfitable);
    }

    [Fact]
    public void Recommendation_HoldsWithoutAnyOption()
    {
        var input = Input(owned: 100, costBasis: 900);

        var result = SellDecisionEngine.Evaluate(input, _fees);

        Assert.Equal(SellDecisionAction.Hold, result.RecommendedAction);
        Assert.False(result.SellNow!.IsEvaluable);
        Assert.False(result.List!.IsEvaluable);
        Assert.Contains("Hold", result.RecommendationReason);
    }

    // ------------------------------------------------------------------
    // Gebühren / Break-even
    // ------------------------------------------------------------------

    [Fact]
    public void Fees_AreChargedOnceAndBreakEvenUsesStoredBasis()
    {
        var buys = new[] { Buy(1_500, 40) };
        var sells = new[] { Sell(1_600, 40) };
        var input = Input(owned: 40, costBasis: 1_000, buys: buys, sells: sells, skills: SkillsWith(5, 5));

        var result = SellDecisionEngine.Evaluate(input, _fees);
        var sellNow = result.SellNow!;
        var list = result.List!;

        var expectedSellNow = _fees.CalculateSellProceeds(1_500, 40, input.Skills);
        Assert.Equal(expectedSellNow.GrossAmount, sellNow.GrossAmount, 6);
        Assert.Equal(expectedSellNow.BrokerFee, sellNow.BrokerFee, 6);
        Assert.Equal(expectedSellNow.SalesTax, sellNow.SalesTax, 6);
        Assert.Equal(expectedSellNow.NetAmount, sellNow.NetAmount, 6);

        var expectedList = _fees.CalculateSellProceeds(list.TargetPrice, 40, input.Skills);
        Assert.Equal(expectedList.NetAmount, list.NetAmount, 6);

        // Keine zweite Buy-Gebühr: Break-even enthält die gespeicherte Basis genau einmal.
        var expectedBreakEven = _fees.CalculateBreakEvenSellPriceForStoredBasis(1_000, input.Skills);
        Assert.Equal(expectedBreakEven, sellNow.BreakEvenPrice, 6);
        Assert.Equal(expectedBreakEven, list.BreakEvenPrice, 6);
        Assert.Equal(sellNow.NetAmount - 1_000 * 40, sellNow.NetProfit!.Value, 6);
        Assert.Contains(result.Evidence, e => e.Contains("ohne zweite Buy-Gebühr"));
    }

    // ------------------------------------------------------------------
    // Pflichtdaten / Nicht ausführbar
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(null, "Cost Basis unbekannt")]
    [InlineData(0.0, "Ungültige Cost Basis")]
    [InlineData(-5.0, "Ungültige Cost Basis")]
    [InlineData(double.NaN, "Ungültige Cost Basis")]
    public void NoRecommendation_WithoutUsableCostBasis(double? costBasis, string expectedReason)
    {
        var input = Input(owned: 100, costBasis: costBasis, buys: new[] { Buy(1_200, 100) }, sells: new[] { Sell(1_300, 100) });

        var result = SellDecisionEngine.Evaluate(input, _fees);

        Assert.False(result.IsActionable);
        Assert.Contains(expectedReason, result.NotActionableReason);
        Assert.Equal(SellDecisionAction.Hold, result.RecommendedAction);
        Assert.Null(result.SellNow);
        Assert.Null(result.List);
        Assert.NotEmpty(result.Evidence);
    }

    [Fact]
    public void NoRecommendation_WhenForeignOrderBookFailed()
    {
        var input = Input(
            owned: 100,
            costBasis: 900,
            buys: new[] { Buy(1_200, 100) },
            sells: new[] { Sell(1_300, 100) },
            status: OrderBookDataStatus.Failed);

        var result = SellDecisionEngine.Evaluate(input, _fees);

        Assert.False(result.IsActionable);
        Assert.Contains("ESI-Abruf fehlgeschlagen", result.NotActionableReason);
        Assert.Equal(SellDecisionAction.Hold, result.RecommendedAction);
    }

    [Fact]
    public void InvalidTickOverride_Throws()
    {
        foreach (var tick in new[] { 0.0, -1.0, double.NaN })
        {
            var input = Input(owned: 100, costBasis: 900, buys: new[] { Buy(1_200, 100) }, sells: new[] { Sell(1_300, 100) }, tickOverride: tick);
            Assert.Throws<ArgumentOutOfRangeException>(() => SellDecisionEngine.Evaluate(input, _fees));
        }
    }

    // ------------------------------------------------------------------
    // Determinismus / Evidenz
    // ------------------------------------------------------------------

    [Fact]
    public void SameInputs_ProduceIdenticalResultAndEvidence()
    {
        var buys = new[] { Buy(1_200, 30), Buy(1_150, 40) };
        var sells = new[] { Sell(1_250, 60), Sell(1_240, 60) };
        var input = Input(owned: 50, costBasis: 900, buys: buys, sells: sells);

        var first = SellDecisionEngine.Evaluate(input, _fees);
        var second = SellDecisionEngine.Evaluate(input, _fees);

        Assert.Equal(SellDecisionEngine.AlgorithmVersion, first.AlgorithmVersion);
        Assert.Equal(first.RecommendedAction, second.RecommendedAction);
        Assert.Equal(first.RecommendationReason, second.RecommendationReason);
        Assert.Equal(first.Evidence, second.Evidence);
        Assert.Equal(first.SellNow!.NetAmount, second.SellNow!.NetAmount, 6);
        Assert.Equal(first.List!.TargetPrice, second.List!.TargetPrice, 6);
    }

    [Fact]
    public void Evidence_DocumentsQuantityBoundTickAndFeesOnce()
    {
        var buys = new[] { Buy(1_200, 30), Buy(1_150, 40) };
        var sells = new[] { Sell(1_250, 60) };
        var input = Input(owned: 50, costBasis: 900, buys: buys, sells: sells, locationName: "Jita IV-4");

        var result = SellDecisionEngine.Evaluate(input, _fees);

        Assert.Contains(result.Evidence, e => e.Contains("Jita IV-4") && e.Contains("50 Stück im Besitz"));
        Assert.Contains(result.Evidence, e => e.Contains("2 erreichbare Buy-Order(s)"));
        Assert.Contains(result.Evidence, e => e.Contains("ausführbar insgesamt 70 Stück"));
        Assert.Contains(result.Evidence, e => e.Contains("Gebühren genau einmal"));
        Assert.Contains(result.Evidence, e => e.Contains("gültiger Tick"));
        Assert.Contains(result.Evidence, e => e.Contains("Queue-Ziel Position 1"));
    }

    [Fact]
    public void Evidence_NamesConservativeFeeEstimateWithoutSkills()
    {
        var input = Input(owned: 10, costBasis: 900, buys: new[] { Buy(1_200, 10) }, sells: new[] { Sell(1_300, 10) });

        var result = SellDecisionEngine.Evaluate(input, _fees);

        Assert.True(
            result.Evidence.Any(e => e.Contains("konservativen Schätzung des Gebührenrechners")),
            string.Join(" | ", result.Evidence));
    }

    [Fact]
    public void FeeCalculatorWithoutSkills_UsesConservativeRates()
    {
        // Referenz der konservativen Sätze (ohne Skills/Standings), damit die Evidenz oben nachvollziehbar bleibt.
        Assert.Equal(0.03, _fees.GetBrokerFeeRate(null), 6);
        Assert.Equal(0.075, _fees.GetSalesTaxRate(null), 6);
    }
}
