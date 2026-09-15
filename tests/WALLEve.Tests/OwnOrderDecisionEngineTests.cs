using WALLEve.Models.Esi.Character;
using WALLEve.Services.Market;
using WALLEve.Services.Market.Interfaces;
using WALLEve.Services.Trading;

namespace WALLEve.Tests;

/// <summary>
/// Tests der Entscheidung über eine EIGENE Order (Issue #64): Queue inklusive Buy-Range,
/// gültige Ticks, Relist-Gebühr (Modify) gegen volle Broker-Fee (Cancel-Recreate) und
/// Cooldown. Ein Preis wird nur empfohlen, wenn der erwartete Mehrwert die Gebühr
/// übersteigt; es wird nie eine Order ausgeführt. Alle Fixtures sind deterministisch
/// und ohne Live-ESI.
/// </summary>
public class OwnOrderDecisionEngineTests
{
    private const long Jita = 60003760;   // eigener Ort (Station)
    private const long Amarr = 60008494;  // fremder Ort (andere Station)

    private readonly IFeeCalculatorService _fees = new FeeCalculatorService();

    private static CharacterSkills SkillsWith(
        int brokerRelationsLevel, int accountingLevel, int advancedBrokerRelationsLevel = 0) => new()
    {
        Skills = new List<CharacterSkill>
        {
            new() { SkillId = 3446, TrainedSkillLevel = brokerRelationsLevel },      // Broker Relations
            new() { SkillId = 16622, TrainedSkillLevel = accountingLevel },          // Accounting
            new() { SkillId = 16597, TrainedSkillLevel = advancedBrokerRelationsLevel } // Advanced Broker Relations
        }
    };

    private static OrderBookLine Sell(double price, int volume, bool own = false, long locationId = Jita)
        => new()
        {
            OrderId = (long)(price * 1000) + volume + 7,
            IsOwn = own,
            IsBuyOrder = false,
            Price = price,
            VolumeRemain = volume,
            VolumeTotal = volume,
            LocationId = locationId,
            IsSameLocation = locationId == Jita,
            CanReachOwnLocation = false,
            Issued = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

    private static OrderBookLine Buy(
        double price, int volume, bool reachable = true, bool own = false, long locationId = Jita)
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
            Issued = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

    private static OwnOrderDecisionInput OwnSellOrder(
        double price,
        int quantity,
        IReadOnlyList<OrderBookLine>? sells = null,
        IReadOnlyList<OrderBookLine>? buys = null,
        double minutesSinceLastChange = 60,
        CharacterSkills? skills = null,
        double? costBasis = null,
        OrderBookDataStatus status = OrderBookDataStatus.Ok,
        double? targetPriceOverride = null,
        double? tickOverride = null,
        double? cooldownOverride = null)
        => new()
        {
            OrderId = 4711,
            TypeName = "Tritanium",
            LocationId = Jita,
            LocationName = "Jita IV-4",
            IsBuyOrder = false,
            CurrentPrice = price,
            RemainingQuantity = quantity,
            MinutesSinceLastChange = minutesSinceLastChange,
            SameLocationSellQuotes = sells ?? Array.Empty<OrderBookLine>(),
            ReachableBuyOrders = buys ?? Array.Empty<OrderBookLine>(),
            ForeignDataStatus = status,
            CostBasisPerUnit = costBasis,
            TargetPriceOverride = targetPriceOverride,
            TickSizeOverride = tickOverride,
            CooldownMinutesOverride = cooldownOverride,
            Skills = skills
        };

    private static OwnOrderDecisionInput OwnBuyOrder(
        double price,
        int quantity,
        IReadOnlyList<OrderBookLine>? buys = null,
        double minutesSinceLastChange = 60,
        CharacterSkills? skills = null)
        => new()
        {
            OrderId = 4712,
            TypeName = "Tritanium",
            LocationId = Jita,
            LocationName = "Jita IV-4",
            IsBuyOrder = true,
            CurrentPrice = price,
            RemainingQuantity = quantity,
            MinutesSinceLastChange = minutesSinceLastChange,
            SameLocationSellQuotes = Array.Empty<OrderBookLine>(),
            ReachableBuyOrders = buys ?? Array.Empty<OrderBookLine>(),
            Skills = skills
        };

    // ------------------------------------------------------------------
    // Modify nur bei positivem Mehrwert (Gebühr ≤ Mehrwert ⇒ kein Modify)
    // ------------------------------------------------------------------

    [Fact]
    public void Modify_NotRecommended_WhenFeeExceedsBenefit()
    {
        // 1 ISK Preiserhöhung auf 10 Stück: Mehrwert 8,95 ISK gegen 150,45 ISK Relist-Gebühr.
        var input = OwnSellOrder(1_000, 10, sells: new[] { Sell(1_002, 100) });

        var result = OwnOrderDecisionEngine.Evaluate(input, _fees);
        var modify = result.Modify!;

        Assert.True(modify.IsEligible);
        Assert.Equal(1_001, modify.TargetPrice, 6);
        Assert.Equal(8.95, modify.ExpectedBenefit!.Value, 6);
        Assert.Equal(150.45, modify.ActionFee, 6);
        Assert.Equal(-141.5, modify.NetBenefit!.Value, 6);
        Assert.True(modify.NetBenefit < 0);

        Assert.Equal(OwnOrderAction.Wait, result.RecommendedAction);
        Assert.Contains("die Gebühr übersteigt den Mehrwert", result.RecommendationReason);
        Assert.Contains("deshalb kein Modify", result.RecommendationReason);
    }

    [Fact]
    public void Modify_Recommended_WhenBenefitExceedsRelistFee()
    {
        // 9 ISK Preiserhöhung auf 5.000 Stück mit Broker Relations 5 / Accounting 5 / ABR 5.
        var skills = SkillsWith(5, 5, 5);
        var input = OwnSellOrder(1_000, 5_000, sells: new[] { Sell(1_010, 5_000) }, skills: skills);

        var result = OwnOrderDecisionEngine.Evaluate(input, _fees);
        var modify = result.Modify!;
        var cancelRecreate = result.CancelRecreate!;

        Assert.Equal(1_009, modify.TargetPrice, 6);
        Assert.True(modify.IsOnValidTick);
        Assert.Equal(42_806.25, modify.ExpectedBenefit!.Value, 6);
        Assert.Equal(15_810.0, modify.ActionFee, 6);
        Assert.Equal(26_996.25, modify.NetBenefit!.Value, 6);

        Assert.Equal(OwnOrderAction.Modify, result.RecommendedAction);
        Assert.Contains("Empfehlung modify", result.RecommendationReason);
        Assert.Contains("Es wird nichts ausgeführt", result.RecommendationReason);

        // Cancel-Recreate wäre zulässig, ist aber teurer (volle Broker-Fee) und damit kein Kandidat.
        Assert.True(cancelRecreate.IsEligible);
        Assert.Equal(75_675.0, cancelRecreate.ActionFee, 6);
        Assert.True(cancelRecreate.NetBenefit < 0);
    }

    [Fact]
    public void CancelRecreate_CostsFullBrokerFee_AndWinsOnlyWhenModifyIsBlocked()
    {
        var skills = SkillsWith(5, 5, 5);
        var sells = new[] { Sell(1_020, 5_000) };

        // Außerhalb des Cooldowns: Modify ist die günstigere Änderung und gewinnt.
        var freeModify = OwnOrderDecisionEngine.Evaluate(
            OwnSellOrder(1_000, 5_000, sells: sells, skills: skills, minutesSinceLastChange: 60), _fees);

        Assert.Equal(OwnOrderAction.Modify, freeModify.RecommendedAction);
        Assert.Equal(16_710.0, freeModify.Modify!.ActionFee, 6);
        Assert.Equal(73_658.75, freeModify.Modify.NetBenefit!.Value, 6);

        // Innerhalb des Cooldowns: Modify ist gesperrt, Cancel-Recreate bleibt als belegte Option.
        var blockedModify = OwnOrderDecisionEngine.Evaluate(
            OwnSellOrder(1_000, 5_000, sells: sells, skills: skills, minutesSinceLastChange: 2), _fees);

        Assert.False(blockedModify.Modify!.IsEligible);
        Assert.Contains("Cooldown", blockedModify.Modify.IneligibleReason);
        Assert.Equal(76_425.0, blockedModify.CancelRecreate!.ActionFee, 6);
        Assert.Equal(13_943.75, blockedModify.CancelRecreate.NetBenefit!.Value, 6);
        Assert.Equal(OwnOrderAction.CancelRecreate, blockedModify.RecommendedAction);

        // Die volle Broker-Fee der neuen Order ist immer teurer als die Relist-Gebühr.
        Assert.True(blockedModify.CancelRecreate.ActionFee > blockedModify.Modify.ActionFee);
    }

    // ------------------------------------------------------------------
    // Cooldown-Grenzen
    // ------------------------------------------------------------------

    [Fact]
    public void Cooldown_Gate_BlocksModifyJustBelowAndAllowsAtExactBoundary()
    {
        var skills = SkillsWith(5, 5, 5);
        var sells = new[] { Sell(1_020, 5_000) };

        var justBelow = OwnOrderDecisionEngine.Evaluate(
            OwnSellOrder(1_000, 5_000, sells: sells, skills: skills,
                minutesSinceLastChange: OwnOrderDecisionEngine.DefaultModifyCooldownMinutes - 0.001), _fees);
        var atBoundary = OwnOrderDecisionEngine.Evaluate(
            OwnSellOrder(1_000, 5_000, sells: sells, skills: skills,
                minutesSinceLastChange: OwnOrderDecisionEngine.DefaultModifyCooldownMinutes), _fees);

        Assert.False(justBelow.Modify!.IsEligible);
        Assert.Contains("Cooldown", justBelow.Modify.IneligibleReason);
        Assert.True(atBoundary.Modify!.IsEligible);
        Assert.Equal(OwnOrderAction.Modify, atBoundary.RecommendedAction);

        // Abweichender Cooldown (explizit konfiguriert) verschiebt die Grenze mit.
        var customBelow = OwnOrderDecisionEngine.Evaluate(
            OwnSellOrder(1_000, 5_000, sells: sells, skills: skills, minutesSinceLastChange: 14.999,
                cooldownOverride: 15), _fees);
        var customAt = OwnOrderDecisionEngine.Evaluate(
            OwnSellOrder(1_000, 5_000, sells: sells, skills: skills, minutesSinceLastChange: 15,
                cooldownOverride: 15), _fees);

        Assert.False(customBelow.Modify!.IsEligible);
        Assert.True(customAt.Modify!.IsEligible);
        Assert.Contains("Cooldown noch nicht abgelaufen", customBelow.Modify.IneligibleReason);
    }

    // ------------------------------------------------------------------
    // Gültige Ticks
    // ------------------------------------------------------------------

    [Fact]
    public void Tick_TargetIsAlwaysAValidTickStep()
    {
        // Ab 1.000 ISK gilt der 1-ISK-Schritt: 1.002,5 → 1.001 (immer ABrunden).
        var large = OwnOrderDecisionEngine.Evaluate(
            OwnSellOrder(1_000, 100, sells: new[] { Sell(1_002.5, 100) }), _fees);

        Assert.Equal(1.0, large.Modify!.TickSize, 6);
        Assert.Equal(1_001.0, large.Modify.TargetPrice, 6);
        Assert.True(large.Modify.IsOnValidTick);

        // Unter 1.000 ISK gilt der Cent-Schritt: 250,00 → 249,99.
        var small = OwnOrderDecisionEngine.Evaluate(
            OwnSellOrder(249, 1_000, sells: new[] { Sell(250, 1_000) }), _fees);

        Assert.Equal(0.01, small.Modify!.TickSize, 6);
        Assert.Equal(249.99, small.Modify.TargetPrice, 6);
        Assert.True(small.Modify.IsOnValidTick);
        Assert.True(small.Modify.ExpectedBenefit > 0);
    }

    [Fact]
    public void Tick_OffTickTargetOverride_IsRejectedWithoutRecommendation()
    {
        var input = OwnSellOrder(
            1_000, 100, sells: new[] { Sell(1_002, 100) }, targetPriceOverride: 1_000.5, tickOverride: 1.0);

        var result = OwnOrderDecisionEngine.Evaluate(input, _fees);

        Assert.False(result.Modify!.IsEligible);
        Assert.Contains("gültigen Tick-Stufe", result.Modify.IneligibleReason);
        Assert.Equal(OwnOrderAction.Wait, result.RecommendedAction);
    }

    [Fact]
    public void Tick_TargetBelowOneTickStep_HasNoDerivablePrice()
    {
        // Beste Konkurrenzquote unterhalb eines Tick-Schritts → kein positiver Zielpreis.
        var input = OwnSellOrder(1.0, 100, sells: new[] { Sell(0.005, 100) });

        var result = OwnOrderDecisionEngine.Evaluate(input, _fees);

        Assert.False(result.Modify!.IsEligible);
        Assert.Contains("positiven Zielpreis", result.Modify.IneligibleReason);
        Assert.Equal(OwnOrderAction.Wait, result.RecommendedAction);
    }

    [Fact]
    public void InvalidOverrides_AndInvalidElapsedTime_Throw()
    {
        var sells = new[] { Sell(1_002, 100) };

        foreach (var tick in new[] { 0.0, -1.0, double.NaN })
        {
            var input = OwnSellOrder(1_000, 100, sells: sells, tickOverride: tick);
            Assert.Throws<ArgumentOutOfRangeException>(() => OwnOrderDecisionEngine.Evaluate(input, _fees));
        }

        foreach (var target in new[] { 0.0, -5.0, double.PositiveInfinity })
        {
            var input = OwnSellOrder(1_000, 100, sells: sells, targetPriceOverride: target);
            Assert.Throws<ArgumentOutOfRangeException>(() => OwnOrderDecisionEngine.Evaluate(input, _fees));
        }

        var negativeCooldown = OwnSellOrder(1_000, 100, sells: sells, cooldownOverride: -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => OwnOrderDecisionEngine.Evaluate(negativeCooldown, _fees));

        var negativeElapsed = OwnSellOrder(1_000, 100, sells: sells, minutesSinceLastChange: -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => OwnOrderDecisionEngine.Evaluate(negativeElapsed, _fees));
    }

    // ------------------------------------------------------------------
    // Queue und Buy-Range
    // ------------------------------------------------------------------

    [Fact]
    public void Queue_ReportsPositionAndQuantities_AndIgnoresOwnOrder()
    {
        var sells = new[]
        {
            Sell(1_000, 100, own: true),  // eigene Order — darf die Konkurrenz nicht verfälschen
            Sell(990, 200),
            Sell(995, 100),
            Sell(1_010, 300)
        };
        var buys = new[] { Buy(999, 100), Buy(995, 50), Buy(1_200, 999, reachable: false) };

        var result = OwnOrderDecisionEngine.Evaluate(
            OwnSellOrder(1_000, 100, sells: sells, buys: buys), _fees);
        var wait = result.Wait!;

        Assert.Equal(3, wait.QueuePosition);
        Assert.Equal(300, wait.CompetingQuantityAhead);
        Assert.Equal(300, wait.CompetingQuantityBehind);
        Assert.Equal(990.0, wait.BestCompetingPrice!.Value, 6);
        Assert.False(wait.IsBestAtOwnLocation);

        // Buy-Range: nur erreichbare Buy-Orders zählen — die teurere, nicht erreichbare nicht.
        Assert.Equal(150, wait.ReachableDemandQuantity);
        Assert.Equal(999.0, wait.BestReachableBuyPrice!.Value, 6);

        Assert.Contains(result.Evidence, e => e.Contains("Queue: Position 3"));
        Assert.Contains(result.Evidence, e => e.Contains("150 Stück erreichbare Nachfrage"));
    }

    [Fact]
    public void OwnBuyOrder_ReportsReachableCompetition_AndRecommendsWait()
    {
        // Verkäufer bedienen den HÖCHSTEN erreichbaren Käufer: die 1.005 konkurriert, die 990 nicht.
        var buys = new[] { Buy(990, 200), Buy(1_005, 100) };

        var result = OwnOrderDecisionEngine.Evaluate(OwnBuyOrder(1_000, 100, buys: buys), _fees);

        Assert.Equal(2, result.Wait!.QueuePosition);
        Assert.Equal(100, result.Wait.CompetingQuantityAhead);
        Assert.Equal(200, result.Wait.CompetingQuantityBehind);

        Assert.Equal(OwnOrderAction.Wait, result.RecommendedAction);
        Assert.Contains("Buy-Order", result.RecommendationReason);
        Assert.False(result.Modify!.IsEligible);
        Assert.Contains("nicht belegbar", result.Modify.IneligibleReason);
        Assert.True(result.Modify.ActionFee > 0);   // Gebühr wird berichtet, nicht empfohlen
    }

    // ------------------------------------------------------------------
    // Pflichtdaten
    // ------------------------------------------------------------------

    [Fact]
    public void NoRecommendation_WhenForeignOrderBookFailed()
    {
        var input = OwnSellOrder(
            1_000, 100, sells: new[] { Sell(1_002, 100) }, status: OrderBookDataStatus.Failed);

        var result = OwnOrderDecisionEngine.Evaluate(input, _fees);

        Assert.False(result.IsActionable);
        Assert.Contains("nicht verfügbar", result.NotActionableReason);
        Assert.Equal(OwnOrderAction.Wait, result.RecommendedAction);
        Assert.Null(result.Wait);
        Assert.Null(result.Modify);
        Assert.Null(result.CancelRecreate);
        Assert.NotEmpty(result.Evidence);
        Assert.NotEmpty(result.Assumptions);
    }

    [Fact]
    public void NoRecommendation_WithoutRemainingQuantity()
    {
        var result = OwnOrderDecisionEngine.Evaluate(
            OwnSellOrder(1_000, 0, sells: new[] { Sell(1_002, 100) }), _fees);

        Assert.False(result.IsActionable);
        Assert.Contains("Restmenge", result.NotActionableReason);
        Assert.Equal(OwnOrderAction.Wait, result.RecommendedAction);
    }

    // ------------------------------------------------------------------
    // Break-even
    // ------------------------------------------------------------------

    [Fact]
    public void Modify_BlockedWhenTargetIsBelowBreakEven()
    {
        var skills = SkillsWith(5, 5, 5);

        // Cost Basis 2.000 → Break-even ≈ 2.102,50 ISK; der Zielpreis 1.009 läge darunter.
        var loss = OwnOrderDecisionEngine.Evaluate(
            OwnSellOrder(1_000, 1_000, sells: new[] { Sell(1_010, 1_000) }, skills: skills, costBasis: 2_000), _fees);

        Assert.Equal(2_102.50, loss.Wait!.BreakEvenPrice!.Value, 1);
        Assert.True(loss.Wait.IsBelowBreakEven);
        Assert.False(loss.Modify!.IsEligible);
        Assert.Contains("Break-even", loss.Modify.IneligibleReason);
        Assert.False(loss.CancelRecreate!.IsEligible);
        Assert.Contains("Break-even", loss.CancelRecreate.IneligibleReason);
        Assert.Equal(OwnOrderAction.Wait, loss.RecommendedAction);

        // Mit tragfähiger Cost Basis bleibt das Modify erlaubt und rentabel.
        var profit = OwnOrderDecisionEngine.Evaluate(
            OwnSellOrder(1_000, 1_000, sells: new[] { Sell(1_010, 1_000) }, skills: skills, costBasis: 500), _fees);

        Assert.True(profit.Modify!.IsEligible);
        Assert.Equal(3_162.0, profit.Modify.ActionFee, 6);
        Assert.Equal(5_399.25, profit.Modify.NetBenefit!.Value, 6);
        Assert.Equal(OwnOrderAction.Modify, profit.RecommendedAction);
    }

    // ------------------------------------------------------------------
    // Adapter aus dem Orderbuch-Kontext (#31/#61)
    // ------------------------------------------------------------------

    [Fact]
    public void FromOrderBookContext_MapsAndFiltersOwnAndUnreachableOrders()
    {
        var context = new OrderBookContext
        {
            TypeId = 34,
            TypeName = "Tritanium",
            OwnOrderId = 99,
            OwnIsBuyOrder = false,
            OwnPrice = 1_000,
            OwnRemaining = 100,
            OwnLocationId = Jita,
            OwnLocationName = "Jita IV-4",
            CostBasisPerUnit = 500,
            ForeignDataStatus = OrderBookDataStatus.Ok,
            SellSide = new List<OrderBookLine>
            {
                Sell(990, 200, locationId: Amarr),                          // fremd, anderer Ort
                Sell(1_000, 100, own: true)                                  // eigene Order
            },
            BuySide = new List<OrderBookLine>
            {
                Buy(995, 100),                                              // erreichbar
                Buy(1_200, 999, reachable: false)                            // nicht erreichbar
            }
        };

        var input = OwnOrderDecisionEngine.FromOrderBookContext(
            context, minutesSinceLastChange: 30, skills: SkillsWith(5, 5, 5));

        Assert.Equal(99, input.OrderId);
        Assert.Equal(1_000.0, input.CurrentPrice, 6);
        Assert.Equal(100, input.RemainingQuantity);
        Assert.Equal(30, input.MinutesSinceLastChange, 6);
        Assert.Equal(500.0, input.CostBasisPerUnit!.Value, 6);
        Assert.Single(input.SameLocationSellQuotes);                       // eigene Order entfernt
        Assert.Single(input.ReachableBuyOrders);                            // nicht erreichbare entfernt
        Assert.False(input.ReachableBuyOrders[0].IsBuyOrder == false);

        var result = OwnOrderDecisionEngine.Evaluate(input, _fees);

        Assert.True(result.IsActionable);
        Assert.Equal(100, result.Wait!.ReachableDemandQuantity);
        Assert.Equal(995.0, result.Wait.BestReachableBuyPrice!.Value, 6);
    }

    // ------------------------------------------------------------------
    // Determinismus / keine Orderausführung
    // ------------------------------------------------------------------

    [Fact]
    public void SameInputs_ProduceIdenticalResult_AndDocumentNoExecution()
    {
        var input = OwnSellOrder(1_000, 5_000, sells: new[] { Sell(1_010, 5_000) }, skills: SkillsWith(5, 5, 5));

        var first = OwnOrderDecisionEngine.Evaluate(input, _fees);
        var second = OwnOrderDecisionEngine.Evaluate(input, _fees);

        Assert.Equal(OwnOrderDecisionEngine.AlgorithmVersion, first.AlgorithmVersion);
        Assert.Equal(first.RecommendedAction, second.RecommendedAction);
        Assert.Equal(first.RecommendationReason, second.RecommendationReason);
        Assert.Equal(first.Evidence, second.Evidence);
        Assert.Equal(first.Assumptions, second.Assumptions);
        Assert.Equal(first.Modify!.NetBenefit!.Value, second.Modify!.NetBenefit!.Value, 6);

        // Die Engine empfiehlt nur — Annahmen und Belege benennen das ausdrücklich.
        Assert.Contains(first.Assumptions, a => a.Contains("keine ESI-Schreiborder"));
        Assert.Contains(first.Assumptions, a => a.Contains("Relist-Rabatt"));
        Assert.Contains(first.Evidence, e => e.Contains("gültige Tick-Stufe"));
        Assert.Contains(first.Evidence, e => e.Contains("Cooldown"));
    }
}
