using WALLEve.Models.Esi.Character;
using WALLEve.Services.Market;

namespace WALLEve.Tests;

/// <summary>
/// Tests für die Gebühren- und Gewinn-Berechnung nach OFFIZIELLEN EVE-Formeln
/// (support.eveonline.com "Broker Fee and Sales Tax" + "Buy and Sell Orders"):
/// Broker 3% Basis (−0.3%/Level, min 1%), Sales Tax 7,5% Basis (min 3,37%),
/// Modify-Fee = max(0, BR×(P2−P1)) + (1−RD)×BR×P2 (min 100 ISK).
/// Skill-IDs SDE-verifiziert: Broker Relations 3446, Advanced Broker Relations 16597, Accounting 16622.
/// </summary>
public class FeeCalculatorServiceTests
{
    private static CharacterSkills SkillsWith(int brokerRelationsLevel, int accountingLevel, int advancedBrokerLevel = 0) => new()
    {
        Skills = new List<CharacterSkill>
        {
            new() { SkillId = 3446, TrainedSkillLevel = brokerRelationsLevel },
            new() { SkillId = 16597, TrainedSkillLevel = advancedBrokerLevel },
            new() { SkillId = 16622, TrainedSkillLevel = accountingLevel }
        }
    };

    // ------------------------------------------------------------------
    // Broker Fee (3% Basis, −0.3%/Level, min 1%)
    // ------------------------------------------------------------------

    [Fact]
    public void BrokerFee_WithoutSkills_IsThreePercent()
    {
        var service = new FeeCalculatorService();
        Assert.Equal(0.03, service.GetBrokerFeeRate(null), 6);
    }

    [Fact]
    public void BrokerFee_Level5_IsOnePointFivePercent()
    {
        var service = new FeeCalculatorService();
        // 3% − 5×0.3% = 1.5% (Floor 1% wird bei Level 5 ohne Standings nicht erreicht)
        Assert.Equal(0.015, service.GetBrokerFeeRate(SkillsWith(5, 0)), 6);
    }

    [Fact]
    public void BrokerFee_DoesNotUseRetailSkill_WrongId3444()
    {
        var service = new FeeCalculatorService();
        // Sicherstellen: Skill 3444 ("Retail") beeinflusst die Broker-Fee NICHT —
        // das war ein früherer Bug (falsche Skill-ID im Calculator).
        var skills = new CharacterSkills
        {
            Skills = new List<CharacterSkill>
            {
                new() { SkillId = 3444, TrainedSkillLevel = 5 } // Retail, irrelevant
            }
        };
        Assert.Equal(0.03, service.GetBrokerFeeRate(skills), 6);
    }

    // ------------------------------------------------------------------
    // Sales Tax (7,5% Basis, −11% relativ/Level, min 3,37%)
    // ------------------------------------------------------------------

    [Fact]
    public void SalesTax_WithoutSkills_IsSevenPointFivePercent()
    {
        var service = new FeeCalculatorService();
        Assert.Equal(0.075, service.GetSalesTaxRate(null), 6);
    }

    [Fact]
    public void SalesTax_Level5_FloorsAtThreePoint37Percent()
    {
        var service = new FeeCalculatorService();
        // 7.5% × (1 − 5×0.11) = 3.375% → Floor 3.37%
        Assert.Equal(0.0337, service.GetSalesTaxRate(SkillsWith(0, 5)), 4);
    }

    [Fact]
    public void SellProceeds_SubtractsFeesFromGross()
    {
        var service = new FeeCalculatorService();
        var result = service.CalculateSellProceeds(1000, 10, null); // 10.000 ISK gross

        Assert.Equal(10_000, result.GrossAmount, 6);
        Assert.Equal(300, result.BrokerFee, 6);   // 3%
        Assert.Equal(750, result.SalesTax, 6);    // 7,5%
        Assert.Equal(8_950, result.NetAmount, 6); // netto nach Fees
        Assert.Equal(10.5, result.EffectiveFeeRatePercent, 6);
    }

    // ------------------------------------------------------------------
    // Modify-Fee (Preisänderung) — offizielle Formel
    // ------------------------------------------------------------------

    [Fact]
    public void ModifyFee_PriceCut_PaysOnlyRelistShare()
    {
        var service = new FeeCalculatorService();
        // 6→5 ISK × 10.000: max(0, BR×(50k−60k))=0; (1−0.5)×0.03×50k = 750
        var fee = service.CalculateOrderModifyFee(6.0, 5.0, 10_000, null);

        Assert.Equal(750.0, fee, 2);
    }

    [Fact]
    public void ModifyFee_PriceIncrease_PaysRelistPlusDifference()
    {
        var service = new FeeCalculatorService();
        // 6→7 ISK × 10.000: max(0, 0.03×10k)=300; 0.5×0.03×70k=1050 → 1350
        var fee = service.CalculateOrderModifyFee(6.0, 7.0, 10_000, null);

        Assert.Equal(1_350.0, fee, 2);
    }

    [Fact]
    public void ModifyFee_AdvancedBrokerRelations_ReducesRelistShare()
    {
        var service = new FeeCalculatorService();
        var skills = SkillsWith(0, 0, advancedBrokerLevel: 5); // RD = 50% + 5×6% = 80%

        // Preissenkung 6→5 × 10.000: (1−0.8)×0.03×50k = 300
        var cut = service.CalculateOrderModifyFee(6.0, 5.0, 10_000, skills);
        // Preiserhöhung 6→7 × 10.000: 300 (Differenz) + (1−0.8)×0.03×70k = 420 → 720
        var raise = service.CalculateOrderModifyFee(6.0, 7.0, 10_000, skills);

        Assert.Equal(300.0, cut, 2);
        Assert.Equal(720.0, raise, 2);
    }

    [Fact]
    public void ModifyFee_NeverBelow100Isk()
    {
        var service = new FeeCalculatorService();
        // Kleine Order: 6→5 × 10 Einheiten → 0 + 0.5×0.03×50 = 0.75 → Minimum 100 ISK
        var fee = service.CalculateOrderModifyFee(6.0, 5.0, 10, null);

        Assert.Equal(100.0, fee, 2);
    }

    [Fact]
    public void RelistDiscount_ScalesWithAdvancedBrokerRelations()
    {
        var service = new FeeCalculatorService();
        Assert.Equal(0.50, service.GetRelistDiscountRate(null), 6);
        Assert.Equal(0.80, service.GetRelistDiscountRate(SkillsWith(5, 5, advancedBrokerLevel: 5)), 6);
    }

    // ------------------------------------------------------------------
    // Trade Profit & Break-even
    // ------------------------------------------------------------------

    [Fact]
    public void TradeProfit_WithSkills_IsPositive()
    {
        var service = new FeeCalculatorService();
        var skills = SkillsWith(5, 5); // Broker 1.5%, Tax 3.375%

        var profit = service.CalculateTradeProfit(90, 100, 1000, skills);

        // Kauf: 90.000 × 1.015 = 91.350; Verkauf: 100.000 × (1−0.015−0.03375) = 95.125
        Assert.Equal(3_775, profit.NetProfit, 1);
        Assert.True(profit.RoiPercent > 4.1 && profit.RoiPercent < 4.2);
        // Break-even: 90 × 1.015 / 0.95125 ≈ 96.03
        Assert.Equal(96.03, profit.BreakEvenSellPrice, 2);
    }

    [Fact]
    public void BreakEvenSellPrice_ScalesWithFees()
    {
        var service = new FeeCalculatorService();

        var noSkills = service.CalculateBreakEvenSellPrice(100, 1, null); // 100×1.03/0.895
        var maxSkills = service.CalculateBreakEvenSellPrice(100, 1, SkillsWith(5, 5)); // 100×1.015/0.95125

        Assert.Equal(115.08, noSkills, 2);
        Assert.Equal(106.70, maxSkills, 2);
        Assert.True(maxSkills < noSkills); // bessere Skills → niedrigerer Break-even
    }
}